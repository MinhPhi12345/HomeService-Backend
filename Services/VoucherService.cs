using System.Security.Cryptography;
using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// EP10 – Mã giảm giá (FR-32) và quà tặng tự động (FR-33).
/// Nguyên tắc (BR-V01 → BR-V05): chỉ đơn trả trước; 1 mã/đơn; khách trả tối thiểu MinPayable;
/// Tasker vẫn được tính theo giá gốc, nền tảng chịu phần giảm; hủy/hết hạn mà không thực hiện → trả lại lượt.
/// Các hàm ghi dữ liệu chạy trong transaction của người gọi (người gọi SaveChanges), trừ chỗ ghi rõ.
/// </summary>
public class VoucherService
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // bỏ I, O, 0, 1 cho dễ đọc

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly PromotionOptions _opt;

    public VoucherService(AppDbContext db, INotificationService notifications, IOptions<PromotionOptions> options)
    {
        _db = db;
        _notifications = notifications;
        _opt = options.Value;
    }

    public decimal MinPayable => _opt.MinPayable;

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static IReadOnlyList<int> ParseServiceIds(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();

    /// <summary>
    /// Số tiền giảm của mã cho một đơn trị giá orderValue (BR-V02): theo % (làm tròn, không vượt mức tối đa) hoặc cố định;
    /// luôn để khách trả tối thiểu minPayable.
    /// </summary>
    public static decimal CalculateDiscount(VoucherDiscountType type, decimal value, decimal? maxDiscount,
        decimal orderValue, decimal minPayable)
    {
        var discount = type == VoucherDiscountType.Percent ? Money.Round(orderValue * value / 100m) : value;
        if (type == VoucherDiscountType.Percent && maxDiscount is > 0) discount = Math.Min(discount, maxDiscount.Value);
        discount = Math.Min(discount, orderValue - minPayable);
        return Math.Max(0, Money.Round(discount));
    }

    /// <summary>Kiểm tra mã cho đơn sắp tạo. Hợp lệ → (mã, số tiền giảm); không hợp lệ → 400 kèm lý do cụ thể.</summary>
    public async Task<(Voucher Voucher, decimal Discount)> ValidateAsync(int customerId, string? code,
        int serviceCategoryId, decimal orderValue, PaymentMethod method)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length == 0) throw ApiException.BadRequest("Vui lòng nhập mã giảm giá.");

        var voucher = await _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Code == normalized);
        if (voucher is null || (voucher.AssignedCustomerId is not null && voucher.AssignedCustomerId != customerId))
            throw ApiException.BadRequest("Mã giảm giá không tồn tại.");

        var now = DateTime.UtcNow;
        if (!voucher.IsActive) throw ApiException.BadRequest("Chương trình khuyến mãi này đang tạm dừng.");
        if (voucher.StartsAt > now) throw ApiException.BadRequest("Mã giảm giá chưa đến thời gian áp dụng.");
        if (voucher.EndsAt <= now) throw ApiException.BadRequest("Mã giảm giá đã hết hạn.");
        if (method == PaymentMethod.Cash)
            throw ApiException.BadRequest("Mã giảm giá chỉ áp dụng cho đơn thanh toán trước (ví điện tử, thẻ).");

        var serviceIds = ParseServiceIds(voucher.ServiceCategoryIds);
        if (serviceIds.Count > 0 && !serviceIds.Contains(serviceCategoryId))
            throw ApiException.BadRequest("Mã giảm giá không áp dụng cho dịch vụ này.");
        if (orderValue < voucher.MinOrderValue)
            throw ApiException.BadRequest($"Đơn tối thiểu {MoneyFormat.Vnd(voucher.MinOrderValue)} mới dùng được mã này.");

        if (voucher.Audience == VoucherAudience.NewCustomers
            && await _db.Bookings.AnyAsync(b => b.CustomerId == customerId && b.Status == BookingStatus.Paid))
            throw ApiException.BadRequest("Mã này chỉ dành cho khách hàng chưa có đơn hoàn tất.");

        if (voucher.TotalLimit is not null && voucher.UsedCount >= voucher.TotalLimit)
            throw ApiException.BadRequest("Mã giảm giá đã hết lượt sử dụng.");
        var usedByMe = await _db.VoucherRedemptions.CountAsync(r => r.VoucherId == voucher.Id && r.CustomerId == customerId
                                                                   && r.Status != VoucherRedemptionStatus.Released);
        if (usedByMe >= voucher.PerCustomerLimit)
            throw ApiException.BadRequest("Bạn đã dùng hết lượt của mã này.");

        var discount = CalculateDiscount(voucher.DiscountType, voucher.DiscountValue, voucher.MaxDiscount, orderValue, _opt.MinPayable);
        if (discount <= 0) throw ApiException.BadRequest("Mã giảm giá không áp dụng được cho đơn này.");
        return (voucher, discount);
    }

    /// <summary>
    /// Gắn mã vào đơn vừa tạo (đã có Id): tăng lượt dùng NGUYÊN TỬ (2 khách dùng cùng lúc không vượt TotalLimit),
    /// ghi lượt "đang giữ" và chốt giá gốc / số tiền giảm vào đơn.
    /// </summary>
    public async Task ApplyAsync(Booking booking, Voucher voucher, decimal discount)
    {
        var rows = await _db.Vouchers
            .Where(v => v.Id == voucher.Id && (v.TotalLimit == null || v.UsedCount < v.TotalLimit))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.UsedCount, v => v.UsedCount + 1));
        if (rows == 0) throw ApiException.BadRequest("Mã giảm giá đã hết lượt sử dụng.");

        booking.VoucherId = voucher.Id;
        booking.VoucherCode = voucher.Code;
        booking.DiscountAmount = discount;
        _db.VoucherRedemptions.Add(new VoucherRedemption
        {
            VoucherId = voucher.Id,
            CustomerId = booking.CustomerId,
            BookingId = booking.Id,
            DiscountAmount = discount,
        });
    }

    /// <summary>Đơn bị hủy / hết hạn mà không thực hiện (BR-V04): trả lại lượt dùng mã. Gọi nhiều lần vẫn an toàn.</summary>
    public async Task<bool> ReleaseAsync(int bookingId)
    {
        var redemption = await _db.VoucherRedemptions
            .FirstOrDefaultAsync(r => r.BookingId == bookingId && r.Status == VoucherRedemptionStatus.Applied);
        if (redemption is null) return false;
        redemption.Status = VoucherRedemptionStatus.Released;
        redemption.ReleasedAt = DateTime.UtcNow;
        await _db.Vouchers.Where(v => v.Id == redemption.VoucherId && v.UsedCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.UsedCount, v => v.UsedCount - 1));
        return true;
    }

    /// <summary>Đơn đã hoàn tất / hủy có phí: lượt dùng mã chuyển "đã dùng" (không trả lại).</summary>
    public async Task MarkUsedAsync(int bookingId)
    {
        var redemption = await _db.VoucherRedemptions
            .FirstOrDefaultAsync(r => r.BookingId == bookingId && r.Status == VoucherRedemptionStatus.Applied);
        if (redemption is null) return;
        redemption.Status = VoucherRedemptionStatus.Used;
        redemption.UsedAt = DateTime.UtcNow;
    }

    /// <summary>Đơn vừa chuyển 5 Paid: chốt lượt dùng mã và xét quà khách thân thiết.</summary>
    public async Task OnBookingPaidAsync(Booking booking)
    {
        await MarkUsedAsync(booking.Id);
        await GrantLoyaltyIfDueAsync(booking.CustomerId, booking.Id);
    }

    // ================= Quà tặng tự động (FR-33) =================

    public static GiftRule DefaultRule(VoucherSource kind) => kind switch
    {
        VoucherSource.Welcome => new GiftRule
        {
            Kind = kind, IsEnabled = true, DiscountType = VoucherDiscountType.Percent, DiscountValue = 20,
            MaxDiscount = 50_000, MinOrderValue = 100_000, ValidDays = 30,
        },
        VoucherSource.Loyalty => new GiftRule
        {
            Kind = kind, IsEnabled = true, DiscountType = VoucherDiscountType.FixedAmount, DiscountValue = 30_000,
            MinOrderValue = 150_000, ValidDays = 30, EveryBookings = 5,
        },
        VoucherSource.Apology => new GiftRule
        {
            Kind = kind, IsEnabled = true, DiscountType = VoucherDiscountType.FixedAmount, DiscountValue = 50_000,
            MinOrderValue = 0, ValidDays = 30,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static readonly VoucherSource[] GiftKinds = { VoucherSource.Welcome, VoucherSource.Loyalty, VoucherSource.Apology };

    public async Task<GiftRule> GetRuleAsync(VoucherSource kind)
        => await _db.GiftRules.AsNoTracking().FirstOrDefaultAsync(g => g.Kind == kind) ?? DefaultRule(kind);

    public static string GiftName(VoucherSource kind) => kind switch
    {
        VoucherSource.Welcome => "Quà chào mừng",
        VoucherSource.Loyalty => "Quà khách thân thiết",
        VoucherSource.Apology => "Quà xin lỗi",
        _ => "Mã giảm giá",
    };

    public static string DescribeDiscount(VoucherDiscountType type, decimal value, decimal? max, decimal minOrder)
    {
        var text = type == VoucherDiscountType.Percent
            ? $"Giảm {value:0.##}%" + (max is > 0 ? $", tối đa {MoneyFormat.Vnd(max.Value)}" : string.Empty)
            : $"Giảm {MoneyFormat.Vnd(value)}";
        return minOrder > 0 ? $"{text} cho đơn từ {MoneyFormat.Vnd(minOrder)}" : text;
    }

    /// <summary>Quà chào mừng khi khách xác thực email lần đầu (mỗi khách 1 lần).</summary>
    public async Task<Voucher?> GrantWelcomeAsync(User customer)
    {
        if (customer.Role != UserRole.Customer) return null;
        if (await _db.Vouchers.AnyAsync(v => v.AssignedCustomerId == customer.Id && v.Source == VoucherSource.Welcome)) return null;
        return await GrantAsync(customer.Id, VoucherSource.Welcome, null,
            "Chào mừng bạn đến với HomeService! Đây là quà cho đơn đầu tiên của bạn.");
    }

    /// <summary>Quà khách thân thiết sau mỗi N đơn hoàn tất (đếm cả đơn vừa hoàn tất).</summary>
    public async Task<Voucher?> GrantLoyaltyIfDueAsync(int customerId, int paidBookingId)
    {
        var rule = await GetRuleAsync(VoucherSource.Loyalty);
        if (!rule.IsEnabled || rule.EveryBookings <= 0) return null;
        var paid = await _db.Bookings.CountAsync(b => b.CustomerId == customerId && b.Status == BookingStatus.Paid && b.Id != paidBookingId) + 1;
        if (paid % rule.EveryBookings != 0) return null;
        if (await _db.Vouchers.AnyAsync(v => v.AssignedCustomerId == customerId && v.Source == VoucherSource.Loyalty
                                             && v.SourceBookingId == paidBookingId)) return null;
        return await GrantAsync(customerId, VoucherSource.Loyalty, paidBookingId,
            $"Cảm ơn bạn đã hoàn tất {paid} đơn cùng HomeService!", rule);
    }

    /// <summary>Quà xin lỗi khi Tasker không đến hoặc khiếu nại được chấp nhận (mỗi đơn tối đa 1 quà).</summary>
    public async Task<Voucher?> GrantApologyAsync(int customerId, int bookingId, string reason)
    {
        if (await _db.Vouchers.AnyAsync(v => v.AssignedCustomerId == customerId && v.Source == VoucherSource.Apology
                                             && v.SourceBookingId == bookingId)) return null;
        return await GrantAsync(customerId, VoucherSource.Apology, bookingId, reason);
    }

    private async Task<Voucher?> GrantAsync(int customerId, VoucherSource kind, int? sourceBookingId, string message,
        GiftRule? rule = null)
    {
        rule ??= await GetRuleAsync(kind);
        if (!rule.IsEnabled || rule.DiscountValue <= 0) return null;

        var now = DateTime.UtcNow;
        var voucher = new Voucher
        {
            Code = await NewGiftCodeAsync(),
            Name = GiftName(kind),
            Description = DescribeDiscount(rule.DiscountType, rule.DiscountValue, rule.MaxDiscount, rule.MinOrderValue),
            DiscountType = rule.DiscountType,
            DiscountValue = rule.DiscountValue,
            MaxDiscount = rule.DiscountType == VoucherDiscountType.Percent ? rule.MaxDiscount : null,
            MinOrderValue = rule.MinOrderValue,
            StartsAt = now,
            EndsAt = now.AddDays(Math.Max(1, rule.ValidDays)),
            TotalLimit = 1,
            PerCustomerLimit = 1,
            Audience = VoucherAudience.AllCustomers,
            IsPublic = false,
            AssignedCustomerId = customerId,
            Source = kind,
            SourceBookingId = sourceBookingId,
            CreatedAt = now,
        };
        _db.Vouchers.Add(voucher);
        _notifications.Add(customerId, $"Bạn nhận được {GiftName(kind).ToLowerInvariant()}",
            $"{message} {voucher.Description} – mã {voucher.Code}, dùng đến {VnTime.Format(voucher.EndsAt)}. " +
            "Xem trong mục Ưu đãi của tôi.");
        return voucher;
    }

    private async Task<string> NewGiftCodeAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            var chars = new char[6];
            for (var j = 0; j < chars.Length; j++) chars[j] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
            var code = "QUA" + new string(chars);
            if (!await _db.Vouchers.AnyAsync(v => v.Code == code) && !_db.Vouchers.Local.Any(v => v.Code == code)) return code;
        }
        throw new InvalidOperationException("Không tạo được mã quà tặng.");
    }
}
