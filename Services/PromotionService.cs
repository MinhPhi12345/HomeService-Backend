using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>
/// EP10 – phía khách (kiểm tra mã, "Ưu đãi của tôi") và phía Admin (quản lý chương trình, thống kê, cài đặt quà tặng).
/// Logic kiểm tra / áp mã nằm ở VoucherService.
/// </summary>
public class PromotionService
{
    private readonly AppDbContext _db;
    private readonly VoucherService _vouchers;
    private readonly AuditService _audit;

    public PromotionService(AppDbContext db, VoucherService vouchers, AuditService audit)
    {
        _db = db;
        _vouchers = vouchers;
        _audit = audit;
    }

    // ================= Khách hàng =================

    public async Task<VoucherCheckResponse> CheckAsync(int customerId, CheckVoucherRequest request)
    {
        var service = await _db.ServiceCategories.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ServiceCategoryId);
        if (service is null || !service.IsActive) throw ApiException.BadRequest("Dịch vụ không tồn tại.");
        var orderValue = service.BasePrice * (request.Quantity ?? 1);
        var (voucher, discount) = await _vouchers.ValidateAsync(customerId, request.Code, service.Id, orderValue,
            request.PaymentMethod ?? PaymentMethod.EWallet);
        return new VoucherCheckResponse(voucher.Id, voucher.Code, voucher.Name, voucher.Description, orderValue, discount,
            orderValue - discount, $"Áp dụng mã {voucher.Code}: giảm {MoneyFormat.Vnd(discount)}.");
    }

    /// <summary>Quà riêng còn hạn + chương trình công khai đang chạy mà khách còn lượt dùng.</summary>
    public async Task<List<MyVoucherResponse>> GetMineAsync(int customerId)
    {
        var now = DateTime.UtcNow;
        var hasPaid = await _db.Bookings.AnyAsync(b => b.CustomerId == customerId && b.Status == BookingStatus.Paid);
        var vouchers = await _db.Vouchers.AsNoTracking()
            .Where(v => v.IsActive && v.StartsAt <= now && v.EndsAt > now
                        && (v.AssignedCustomerId == customerId || (v.AssignedCustomerId == null && v.IsPublic))
                        && (v.TotalLimit == null || v.UsedCount < v.TotalLimit))
            .ToListAsync();
        if (hasPaid) vouchers = vouchers.Where(v => v.Audience != VoucherAudience.NewCustomers).ToList();

        var ids = vouchers.Select(v => v.Id).ToList();
        var used = await _db.VoucherRedemptions.AsNoTracking()
            .Where(r => ids.Contains(r.VoucherId) && r.CustomerId == customerId && r.Status != VoucherRedemptionStatus.Released)
            .GroupBy(r => r.VoucherId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var services = await ServiceNamesAsync();

        return vouchers
            .Select(v => (v, left: v.PerCustomerLimit - used.GetValueOrDefault(v.Id)))
            .Where(x => x.left > 0)
            .OrderBy(x => x.v.AssignedCustomerId == null).ThenBy(x => x.v.EndsAt)
            .Select(x => new MyVoucherResponse(x.v.Id, x.v.Code, x.v.Name, x.v.Description, x.v.DiscountType, x.v.DiscountValue,
                x.v.MaxDiscount, x.v.MinOrderValue, NamesOf(x.v.ServiceCategoryIds, services), x.v.EndsAt, x.v.Source,
                x.v.AssignedCustomerId != null, x.left))
            .ToList();
    }

    // ================= Admin – chương trình khuyến mãi =================

    public static string StatusOf(Voucher v, DateTime now) =>
        v.EndsAt <= now ? "Ended"
        : !v.IsActive ? "Paused"
        : v.TotalLimit is not null && v.UsedCount >= v.TotalLimit ? "SoldOut"
        : v.StartsAt > now ? "Scheduled"
        : "Active";

    public async Task<List<VoucherAdminResponse>> GetCampaignsAsync()
    {
        var now = DateTime.UtcNow;
        var list = await _db.Vouchers.AsNoTracking().Where(v => v.Source == VoucherSource.Campaign)
            .OrderByDescending(v => v.CreatedAt).ToListAsync();
        var ids = list.Select(v => v.Id).ToList();
        var redemptions = await _db.VoucherRedemptions.AsNoTracking()
            .Where(r => ids.Contains(r.VoucherId) && r.Status != VoucherRedemptionStatus.Released)
            .Select(r => new { r.VoucherId, r.Status, r.DiscountAmount })
            .ToListAsync();
        var services = await ServiceNamesAsync();
        return list.Select(v =>
        {
            var mine = redemptions.Where(r => r.VoucherId == v.Id).ToList();
            var completed = mine.Where(r => r.Status == VoucherRedemptionStatus.Used).ToList();
            return new VoucherAdminResponse(v.Id, v.Code, v.Name, v.Description, v.DiscountType, v.DiscountValue, v.MaxDiscount,
                v.MinOrderValue, VoucherService.ParseServiceIds(v.ServiceCategoryIds), NamesOf(v.ServiceCategoryIds, services),
                v.StartsAt, v.EndsAt, v.TotalLimit, v.PerCustomerLimit, v.UsedCount, completed.Count,
                completed.Sum(r => r.DiscountAmount), v.Audience, v.IsPublic, v.IsActive, StatusOf(v, now), v.CreatedAt);
        }).ToList();
    }

    public async Task<int> CreateCampaignAsync(SaveVoucherRequest request, int adminId)
    {
        var code = VoucherService.NormalizeCode(request.Code);
        if (code.StartsWith("QUA", StringComparison.Ordinal))
            throw ApiException.BadRequest("Mã bắt đầu bằng \"QUA\" được dành cho quà tặng tự động.");
        if (await _db.Vouchers.AnyAsync(v => v.Code == code)) throw ApiException.Conflict("Mã giảm giá đã tồn tại.");

        var voucher = new Voucher { Code = code, Source = VoucherSource.Campaign, CreatedById = adminId, CreatedAt = DateTime.UtcNow };
        await ApplyAsync(voucher, request, isNew: true);
        _db.Vouchers.Add(voucher);
        _audit.Add(AuditService.VoucherCreated, 0, detail: $"Tạo mã {voucher.Code} – {voucher.Name}: " +
            VoucherService.DescribeDiscount(voucher.DiscountType, voucher.DiscountValue, voucher.MaxDiscount, voucher.MinOrderValue));
        await _db.SaveChangesAsync();
        return voucher.Id;
    }

    public async Task UpdateCampaignAsync(int id, SaveVoucherRequest request)
    {
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id && v.Source == VoucherSource.Campaign)
                      ?? throw ApiException.NotFound();
        var code = VoucherService.NormalizeCode(request.Code);
        if (code != voucher.Code)
        {
            if (voucher.UsedCount > 0 || await _db.VoucherRedemptions.AnyAsync(r => r.VoucherId == id))
                throw ApiException.BadRequest("Mã đã có khách dùng, không thể đổi mã.");
            if (code.StartsWith("QUA", StringComparison.Ordinal))
                throw ApiException.BadRequest("Mã bắt đầu bằng \"QUA\" được dành cho quà tặng tự động.");
            if (await _db.Vouchers.AnyAsync(v => v.Code == code)) throw ApiException.Conflict("Mã giảm giá đã tồn tại.");
            voucher.Code = code;
        }
        await ApplyAsync(voucher, request, isNew: false);
        voucher.UpdatedAt = DateTime.UtcNow;
        _audit.Add(AuditService.VoucherUpdated, 0, detail: $"Sửa mã {voucher.Code} – {voucher.Name}");
        await _db.SaveChangesAsync();
    }

    private async Task ApplyAsync(Voucher v, SaveVoucherRequest r, bool isNew)
    {
        var type = r.DiscountType!.Value;
        if (!Enum.IsDefined(type)) throw ApiException.BadRequest("Loại giảm giá không hợp lệ.");
        if (decimal.Truncate(r.DiscountValue) != r.DiscountValue) throw ApiException.BadRequest("Giá trị giảm không hợp lệ.");
        if (type == VoucherDiscountType.Percent && r.DiscountValue > 100) throw ApiException.BadRequest("Giảm theo % chỉ từ 1 đến 100.");
        if (type == VoucherDiscountType.Percent && r.MaxDiscount is null or <= 0)
            throw ApiException.BadRequest("Vui lòng nhập mức giảm tối đa cho mã giảm theo %.");
        var starts = DateTimeHelper.ToUtc(r.StartsAt!.Value);
        var ends = DateTimeHelper.ToUtc(r.EndsAt!.Value);
        if (ends <= starts) throw ApiException.BadRequest("Thời gian kết thúc phải sau thời gian bắt đầu.");
        if (isNew && ends <= DateTime.UtcNow) throw ApiException.BadRequest("Thời gian kết thúc phải ở tương lai.");
        if (r.TotalLimit is not null && r.TotalLimit < v.UsedCount)
            throw ApiException.BadRequest($"Tổng lượt dùng không được nhỏ hơn số lượt đã dùng ({v.UsedCount}).");
        if (!Enum.IsDefined(r.Audience)) throw ApiException.BadRequest("Đối tượng áp dụng không hợp lệ.");

        var serviceIds = (r.ServiceCategoryIds ?? new List<int>()).Distinct().ToList();
        if (serviceIds.Count > 0 && await _db.ServiceCategories.CountAsync(s => serviceIds.Contains(s.Id)) != serviceIds.Count)
            throw ApiException.BadRequest("Dịch vụ áp dụng không hợp lệ.");

        v.Name = r.Name.Trim();
        v.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        v.DiscountType = type;
        v.DiscountValue = r.DiscountValue;
        v.MaxDiscount = type == VoucherDiscountType.Percent ? r.MaxDiscount : null;
        v.MinOrderValue = r.MinOrderValue;
        v.ServiceCategoryIds = serviceIds.Count == 0 ? null : string.Join(",", serviceIds.OrderBy(x => x));
        v.StartsAt = starts;
        v.EndsAt = ends;
        v.TotalLimit = r.TotalLimit;
        v.PerCustomerLimit = r.PerCustomerLimit;
        v.Audience = r.Audience;
        v.IsPublic = r.IsPublic;
    }

    public async Task ChangeStatusAsync(int id, string action)
    {
        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id && v.Source == VoucherSource.Campaign)
                      ?? throw ApiException.NotFound();
        var now = DateTime.UtcNow;
        if (voucher.EndsAt <= now) throw ApiException.BadRequest("Chương trình đã kết thúc.");
        string text;
        switch (action)
        {
            case "pause": voucher.IsActive = false; text = "Tạm dừng"; break;
            case "resume": voucher.IsActive = true; text = "Tiếp tục"; break;
            case "end": voucher.EndsAt = now; text = "Kết thúc"; break;
            default: throw ApiException.BadRequest("Thao tác không hợp lệ.");
        }
        voucher.UpdatedAt = now;
        _audit.Add(AuditService.VoucherUpdated, 0, detail: $"{text} chương trình {voucher.Code} – {voucher.Name}");
        await _db.SaveChangesAsync();
    }

    public async Task<PromotionSummaryResponse> GetSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var vouchers = await _db.Vouchers.AsNoTracking().Select(v => new
        {
            v.Id, v.Source, v.IsActive, v.StartsAt, v.EndsAt, v.TotalLimit, v.UsedCount,
        }).ToListAsync();
        var used = await _db.VoucherRedemptions.AsNoTracking().Where(r => r.Status == VoucherRedemptionStatus.Used)
            .Select(r => new { r.VoucherId, r.DiscountAmount }).ToListAsync();
        var sourceOf = vouchers.ToDictionary(v => v.Id, v => v.Source);
        decimal CostOf(Func<VoucherSource, bool> pick) => used.Where(r => pick(sourceOf[r.VoucherId])).Sum(r => r.DiscountAmount);

        var campaigns = vouchers.Where(v => v.Source == VoucherSource.Campaign).ToList();
        var gifts = VoucherService.GiftKinds.Select(k => new GiftStatResponse(k, VoucherService.GiftName(k),
            vouchers.Count(v => v.Source == k), used.Count(r => sourceOf[r.VoucherId] == k), CostOf(s => s == k))).ToList();
        var campaignCost = CostOf(s => s == VoucherSource.Campaign);
        var giftCost = gifts.Sum(g => g.TotalDiscount);
        return new PromotionSummaryResponse(
            campaigns.Count(v => v.IsActive && v.StartsAt <= now && v.EndsAt > now && (v.TotalLimit == null || v.UsedCount < v.TotalLimit)),
            campaigns.Count, used.Count(r => sourceOf[r.VoucherId] == VoucherSource.Campaign), campaignCost, giftCost,
            campaignCost + giftCost, gifts);
    }

    // ================= Admin – cài đặt quà tặng tự động =================

    public async Task<List<GiftRuleResponse>> GetGiftRulesAsync()
    {
        var result = new List<GiftRuleResponse>();
        foreach (var kind in VoucherService.GiftKinds) result.Add(ToResponse(await _vouchers.GetRuleAsync(kind)));
        return result;
    }

    public static GiftRuleResponse ToResponse(GiftRule g) => new(g.Kind, VoucherService.GiftName(g.Kind), g.IsEnabled,
        g.DiscountType, g.DiscountValue, g.MaxDiscount, g.MinOrderValue, g.ValidDays, g.EveryBookings,
        VoucherService.DescribeDiscount(g.DiscountType, g.DiscountValue, g.MaxDiscount, g.MinOrderValue), g.UpdatedAt);

    public async Task<GiftRuleResponse> SaveGiftRuleAsync(VoucherSource kind, SaveGiftRuleRequest r, int adminId)
    {
        if (!VoucherService.GiftKinds.Contains(kind)) throw ApiException.NotFound();
        var type = r.DiscountType!.Value;
        if (!Enum.IsDefined(type)) throw ApiException.BadRequest("Loại giảm giá không hợp lệ.");
        if (decimal.Truncate(r.DiscountValue) != r.DiscountValue) throw ApiException.BadRequest("Giá trị giảm không hợp lệ.");
        if (type == VoucherDiscountType.Percent && r.DiscountValue > 100) throw ApiException.BadRequest("Giảm theo % chỉ từ 1 đến 100.");
        if (type == VoucherDiscountType.Percent && r.MaxDiscount is null or <= 0)
            throw ApiException.BadRequest("Vui lòng nhập mức giảm tối đa cho quà giảm theo %.");
        if (kind == VoucherSource.Loyalty && r.EveryBookings < 1)
            throw ApiException.BadRequest("Số đơn để nhận quà từ 1 đến 100.");

        var rule = await _db.GiftRules.FirstOrDefaultAsync(g => g.Kind == kind);
        if (rule is null)
        {
            rule = new GiftRule { Kind = kind };
            _db.GiftRules.Add(rule);
        }
        rule.IsEnabled = r.IsEnabled;
        rule.DiscountType = type;
        rule.DiscountValue = r.DiscountValue;
        rule.MaxDiscount = type == VoucherDiscountType.Percent ? r.MaxDiscount : null;
        rule.MinOrderValue = r.MinOrderValue;
        rule.ValidDays = r.ValidDays;
        rule.EveryBookings = kind == VoucherSource.Loyalty ? r.EveryBookings : 0;
        rule.UpdatedAt = DateTime.UtcNow;
        rule.UpdatedById = adminId;
        _audit.Add(AuditService.GiftRuleUpdated, 0, detail: $"{VoucherService.GiftName(kind)}: {(rule.IsEnabled ? "bật" : "tắt")}, " +
            VoucherService.DescribeDiscount(rule.DiscountType, rule.DiscountValue, rule.MaxDiscount, rule.MinOrderValue) +
            $", hạn {rule.ValidDays} ngày" + (kind == VoucherSource.Loyalty ? $", mỗi {rule.EveryBookings} đơn" : string.Empty));
        await _db.SaveChangesAsync();
        return ToResponse(rule);
    }

    // ================= Tiện ích =================

    private async Task<Dictionary<int, string>> ServiceNamesAsync() =>
        await _db.ServiceCategories.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);

    private static IReadOnlyList<string> NamesOf(string? csv, Dictionary<int, string> names) =>
        VoucherService.ParseServiceIds(csv).Select(id => names.GetValueOrDefault(id)).Where(n => n is not null).Select(n => n!).ToList();
}
