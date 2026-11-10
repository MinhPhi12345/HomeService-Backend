using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

// EP03 – Quản lý đặt dịch vụ
public class BookingService : IBookingService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly BusinessOptions _business;
    private readonly SettlementOptions _settlement;
    private readonly SettlementService _settlementService;
    private readonly WalletService _wallet;
    private readonly MatchingOptions _matching;
    private readonly AuditService _audit;
    private readonly VoucherService _vouchers;

    public BookingService(AppDbContext db, INotificationService notifications,
        IOptions<BusinessOptions> business, IOptions<SettlementOptions> settlement,
        SettlementService settlementService, WalletService wallet, IOptions<MatchingOptions> matching,
        AuditService audit, VoucherService vouchers)
    {
        _audit = audit;
        _vouchers = vouchers;
        _matching = matching.Value;
        _db = db;
        _notifications = notifications;
        _business = business.Value;
        _settlement = settlement.Value;
        _settlementService = settlementService;
        _wallet = wallet;
    }

    public async Task<Booking> CreateAsync(int customerId, CreateBookingRequest request)
    {
        var now = DateTime.UtcNow;

        var service = await _db.ServiceCategories.FirstOrDefaultAsync(s => s.Id == request.ServiceCategoryId);
        if (service is null || !service.IsActive)
            throw ApiException.BadRequest("Dịch vụ không tồn tại.");

        var scheduledTime = ValidateScheduledTime(request.ScheduledTime!.Value);

        // FR-09: bắt buộc tọa độ để ghép nối theo khoảng cách
        if (request.Latitude is null || request.Longitude is null
            || request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw ApiException.BadRequest("Thiếu vị trí thực hiện.");

        var method = request.PaymentMethod ?? PaymentMethod.EWallet; // mặc định thanh toán online
        if (!Enum.IsDefined(method))
            throw ApiException.BadRequest("Phương thức thanh toán không hợp lệ.");

        var customer = await _db.Users.FirstAsync(u => u.Id == customerId);

        // US30: tài khoản đang chờ khóa không đặt thêm đơn mới
        if (customer.LockPendingAt is not null)
            throw ApiException.BadRequest("Tài khoản của bạn đang chờ khóa nên không thể đặt đơn mới.");

        // FR-27 / BR-U01: khách đang nợ không tạo được đơn mới
        if (customer.OutstandingAmount > 0)
            throw ApiException.BadRequest("Vui lòng thanh toán khoản nợ trước khi đặt đơn mới.");

        // BR-B01 (SRS v1.8): số lượng trong khoảng min–max riêng của từng dịch vụ
        var quantity = request.Quantity ?? service.MinQuantity;
        if (quantity < service.MinQuantity || quantity > service.MaxQuantity)
            throw ApiException.BadRequest(service.MinQuantity == service.MaxQuantity
                ? $"Dịch vụ này chỉ đặt {service.MinQuantity} {service.Unit} mỗi đơn."
                : $"Số lượng của dịch vụ này từ {service.MinQuantity} đến {service.MaxQuantity} {service.Unit}.");
        var items = ValidateItems(service, quantity, request.Items);
        var totalPrice = service.BasePrice * quantity; // BR-B01 – FE không gửi giá
        var customerPresent = request.CustomerPresent ?? false;

        // BR-P06: tiền mặt chỉ cho khách đủ điều kiện
        if (method == PaymentMethod.Cash)
        {
            var paidOrders = await _db.Bookings.CountAsync(b => b.CustomerId == customerId && b.Status == BookingStatus.Paid);
            var eligible = paidOrders >= _settlement.CashMinCompletedOrders
                           && !customer.IsCashBlocked
                           && totalPrice <= _settlement.CashMaxOrderValue
                           && customerPresent;
            if (!eligible)
                throw ApiException.BadRequest("Đơn này chỉ hỗ trợ thanh toán online.");
        }

        // FR-32: kiểm tra mã giảm giá trước khi tạo đơn (chỉ đơn trả trước – BR-V01)
        (Voucher Voucher, decimal Discount)? voucher = null;
        if (!string.IsNullOrWhiteSpace(request.VoucherCode))
            voucher = await _vouchers.ValidateAsync(customerId, request.VoucherCode, service.Id, totalPrice, method);

        var booking = new Booking
        {
            CustomerId = customerId,
            ServiceCategoryId = service.Id,
            ScheduledTime = scheduledTime,
            Address = request.Address.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone)
                ? customer.PhoneNumber
                : request.ContactPhone.Trim(),
            Quantity = quantity,
            EstimatedMinutes = Math.Max(1, service.MinutesPerUnit) * quantity, // BR-B02 – chốt thời lượng lúc đặt
            TotalPrice = totalPrice,
            PaymentMethod = method,
            CommissionRate = _business.CommissionRate, // BR-P01: tỷ lệ chung; chốt lại theo Tasker lúc nhận đơn (FR-31)
            CustomerPresent = customerPresent,
            CreatedAt = now,
        };
        for (var i = 0; i < items.Count; i++)
            booking.Items.Add(new BookingItem { ItemNo = i + 1, Description = items[i] });

        if (method == PaymentMethod.Cash)
        {
            booking.Status = BookingStatus.Pending; // ghép nối ngay (FR-12)
        }
        else
        {
            // BR-P08: đơn online phải thanh toán trước trong 15 phút thì mới tìm Tasker
            booking.Status = BookingStatus.AwaitingPayment;
            booking.PaymentDueAt = now.AddMinutes(_settlement.PaymentTimeoutMinutes);
            _notifications.Add(customerId, "Vui lòng thanh toán đơn hàng",
                $"Đơn {service.Name} đã được tạo. Vui lòng thanh toán {MoneyFormat.Vnd(totalPrice - (voucher?.Discount ?? 0))} " +
                $"trong {_settlement.PaymentTimeoutMinutes} phút để hệ thống tìm Tasker.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        if (voucher is not null)
        {
            await _vouchers.ApplyAsync(booking, voucher.Value.Voucher, voucher.Value.Discount);
            await _db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return booking;
    }

    /// <summary>BR-B05: dịch vụ tính theo hạng mục → khách ghi đủ từng hạng mục (mỗi hạng mục 3–200 ký tự).</summary>
    private static List<string> ValidateItems(ServiceCategory service, int quantity, List<string>? items)
    {
        if (!service.RequiresItemList) return new List<string>();
        var list = (items ?? new List<string>()).Select(x => (x ?? string.Empty).Trim()).ToList();
        if (list.Count != quantity || list.Any(x => x.Length == 0))
            throw ApiException.BadRequest($"Vui lòng ghi rõ {quantity} hạng mục cần làm (mỗi dòng một hạng mục).");
        if (list.Any(x => x.Length < 3 || x.Length > 200))
            throw ApiException.BadRequest("Mỗi hạng mục cần mô tả từ 3 đến 200 ký tự.");
        return list;
    }

    public DateTime ValidateScheduledTime(DateTime scheduledTime)
    {
        var utc = DateTimeHelper.ToUtc(scheduledTime);
        var now = DateTime.UtcNow;
        if (utc < now.AddHours(1) || utc > now.AddDays(30))
            throw ApiException.BadRequest("Thời gian đặt không hợp lệ.");
        return utc;
    }

    // US18 – Bắt đầu (2 → 3, ảnh check-in) → Hoàn thành (3 → 4 online, ảnh check-out) / (3 → 5 tiền mặt đã thu)
    public async Task UpdateStatusAsync(int bookingId, int taskerId, UpdateBookingStatusRequest request)
    {
        var booking = await _db.Bookings
            .Include(b => b.Payment)
            .Include(b => b.ServiceCategory)
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw ApiException.NotFound();
        if (booking.TaskerId != taskerId) throw ApiException.Forbidden();
        if (booking.IsTaskerNoShow) throw ApiException.Conflict("Đơn đã quá giờ hẹn, không thể bắt đầu."); // FR-28

        var now = DateTime.UtcNow;
        var target = request.Status!.Value;

        if (target == BookingStatus.InProgress && booking.Status == BookingStatus.Accepted)
        {
            var photo = AddPhotos(booking, request, BookingPhotoStage.CheckIn, now);
            booking.Status = BookingStatus.InProgress;
            booking.CheckInPhotoUrl = photo;
            booking.StartedAt = now;
            booking.UpdatedAt = now;
            _notifications.Add(booking.CustomerId, "Tasker đã bắt đầu làm việc",
                $"Đơn {booking.ServiceCategory!.Name} đang được thực hiện.");
            await _db.SaveChangesAsync();
            return;
        }

        if (target == BookingStatus.Completed && booking.Status == BookingStatus.InProgress)
        {
            if (booking.PaymentMethod == PaymentMethod.Cash && request.CashCollected != true)
                throw ApiException.BadRequest("Vui lòng xác nhận đã thu đủ tiền mặt hoặc chọn \"Khách chưa thanh toán\".");
            var photo = AddPhotos(booking, request, BookingPhotoStage.CheckOut, now);

            if (booking.PaymentMethod == PaymentMethod.Cash)
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                booking.CheckOutPhotoUrl = photo;
                booking.CompletedAt = now; // mốc tính hạn khiếu nại 24 giờ
                if (booking.Payment is null)
                    await _settlementService.SettleCashAsync(booking); // trừ chiết khấu từ ví Tasker → 5 Paid
                else
                {
                    booking.Status = BookingStatus.Paid; // làm lại sau khiếu nại: tiền mặt đã quyết toán trước đó
                    booking.UpdatedAt = now;
                }
                _notifications.Add(booking.CustomerId, "Đơn đã hoàn thành",
                    $"Đơn {booking.ServiceCategory!.Name} đã hoàn thành và thanh toán tiền mặt. Hãy đánh giá Tasker nhé!");
                await _db.SaveChangesAsync();
                await _wallet.RecalculateReservedAsync(taskerId); // giải phóng phần tạm giữ
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return;
            }

            // Đơn online → chờ khách xác nhận (tự xác nhận sau 24 giờ – FR-25)
            booking.Status = BookingStatus.Completed;
            booking.CheckOutPhotoUrl = photo;
            booking.CompletedAt = now;
            booking.AutoConfirmAt = now.AddHours(_settlement.AutoConfirmHours);
            booking.AutoConfirmReminderSentAt = null;
            booking.UpdatedAt = now;
            _notifications.Add(booking.CustomerId, "Tasker đã hoàn thành công việc",
                $"Tasker đã hoàn thành công việc. Vui lòng kiểm tra và xác nhận trước {VnTime.Format(booking.AutoConfirmAt.Value)}.");
            await _db.SaveChangesAsync();
            return;
        }

        throw ApiException.BadRequest("Chuyển trạng thái không hợp lệ.");
    }

    public const int MaxPhotosPerItem = 3;
    public const int MaxPhotosGeneral = 5;

    /// <summary>
    /// US18 / BR-B05 – ảnh lúc Bắt đầu / Hoàn thành. Dịch vụ tính theo hạng mục: mỗi hạng mục 1–3 ảnh;
    /// dịch vụ khác: 1–5 ảnh. Vẫn nhận photoUrl (1 ảnh) của app cũ. Trả về ảnh đầu tiên (lưu vào CheckIn/CheckOutPhotoUrl).
    /// </summary>
    private string AddPhotos(Booking booking, UpdateBookingStatusRequest request, BookingPhotoStage stage, DateTime now)
    {
        var photos = (request.Photos ?? new List<BookingPhotoInput>())
            .Select(p => (Url: UploadUrl.Normalize(p.Url), p.ItemNo, Note: string.IsNullOrWhiteSpace(p.Note) ? null : p.Note.Trim()))
            .Where(p => p.Url is not null)
            .ToList();
        var legacy = UploadUrl.Normalize(request.PhotoUrl);
        if (photos.Count == 0 && legacy is not null) photos.Add((legacy, null, null));
        if (photos.Count == 0) throw ApiException.BadRequest("Vui lòng chụp ảnh hiện trạng.");
        if (photos.Any(p => p.Note is { Length: > 200 })) throw ApiException.BadRequest("Ghi chú ảnh tối đa 200 ký tự.");

        var byItem = booking.ServiceCategory?.RequiresItemList == true && booking.Items.Count > 0;
        if (byItem)
        {
            var itemNos = booking.Items.Select(i => i.ItemNo).ToHashSet();
            if (photos.Any(p => p.ItemNo is null || !itemNos.Contains(p.ItemNo.Value)))
                throw ApiException.BadRequest("Vui lòng chụp ảnh theo từng hạng mục của đơn.");
            foreach (var item in booking.Items.OrderBy(i => i.ItemNo))
            {
                var count = photos.Count(p => p.ItemNo == item.ItemNo);
                if (count == 0)
                    throw ApiException.BadRequest($"Vui lòng chụp ít nhất 1 ảnh cho hạng mục {item.ItemNo}: {item.Description}.");
                if (count > MaxPhotosPerItem)
                    throw ApiException.BadRequest($"Mỗi hạng mục tối đa {MaxPhotosPerItem} ảnh.");
            }
        }
        else if (photos.Count > MaxPhotosGeneral)
        {
            throw ApiException.BadRequest($"Tối đa {MaxPhotosGeneral} ảnh mỗi lần.");
        }

        foreach (var p in photos)
            _db.BookingPhotos.Add(new BookingPhoto
            {
                BookingId = booking.Id,
                Stage = stage,
                ItemNo = byItem ? p.ItemNo : null,
                Url = p.Url!,
                Note = p.Note,
                CreatedAt = now,
            });
        return photos[0].Url!;
    }

    // US34 – Khách xem ảnh và xác nhận hoàn thành → giải ngân cho Tasker (FR-25)
    public async Task ConfirmAsync(int bookingId, int customerId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Payment)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw ApiException.NotFound();
        if (booking.CustomerId != customerId) throw ApiException.Forbidden();
        if (booking.Status != BookingStatus.Completed)
            throw ApiException.BadRequest("Đơn không ở trạng thái chờ xác nhận.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        await _settlementService.ReleaseOnlineAsync(booking, isAuto: false);
        booking.Status = BookingStatus.Paid;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // US33 / BR-M07 – khách bấm "Tìm lại ngay" khi đơn đã quét hết mà chưa ai nhận và đang chờ lượt quét lại.
    // Quét lại ngay từ đợt 3 km trong CÙNG vòng: chỉ mời Tasker chưa được mời (Tasker đã từ chối không bị làm phiền lại).
    public async Task SearchNowAsync(int bookingId, int customerId)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        if (booking.CustomerId != customerId) throw ApiException.Forbidden();
        var now = DateTime.UtcNow;
        if (booking.Status == BookingStatus.Matched)
            throw ApiException.BadRequest("Hệ thống đang gửi lời mời cho Tasker gần bạn, vui lòng chờ kết quả.");
        if (booking.Status != BookingStatus.Pending || booking.TaskerId != null || booking.SearchExhaustedAt is null)
            throw ApiException.BadRequest("Đơn không ở trạng thái chờ tìm lại Tasker.");
        if (now >= booking.ScheduledTime.AddMinutes(-_matching.StopSearchMinutesBefore))
            throw ApiException.BadRequest("Đã gần giờ hẹn nên hệ thống đã dừng tìm Tasker cho đơn này.");
        if (booking.ManualSearchAvailableAt is not null && booking.ManualSearchAvailableAt > now)
        {
            var wait = (int)Math.Ceiling((booking.ManualSearchAvailableAt.Value - now).TotalMinutes);
            throw ApiException.BadRequest($"Bạn vừa tìm lại. Vui lòng thử lại sau {Math.Max(1, wait)} phút.");
        }

        var rows = await _db.Bookings
            .Where(b => b.Id == bookingId && b.Status == BookingStatus.Pending && b.TaskerId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.CurrentWave, 0)
                .SetProperty(b => b.NextMatchAt, now)
                .SetProperty(b => b.ManualSearchAvailableAt, now.AddMinutes(_matching.ManualSearchCooldownMinutes))
                .SetProperty(b => b.UpdatedAt, now));
        if (rows == 0) throw ApiException.BadRequest("Đơn không ở trạng thái chờ tìm lại Tasker.");
    }

    // US33 – Khách chọn Thử lại (có thể đổi giờ) khi đơn "Chưa có Tasker nhận" (FR-24)
    public async Task RetryAsync(int bookingId, int customerId, RetryBookingRequest? request)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        if (booking.CustomerId != customerId) throw ApiException.Forbidden();
        // FR-28: cũng cho phép "Tìm Tasker khác" khi Tasker không đến
        var noShowRetry = booking.Status == BookingStatus.Accepted && booking.IsTaskerNoShow;
        if (booking.Status != BookingStatus.NoTaskerFound && !noShowRetry)
            throw ApiException.BadRequest("Đơn không ở trạng thái chờ thử lại.");
        if (booking.MatchRound >= _matching.MaxRounds)
            throw ApiException.BadRequest("Đã vượt quá số lần thử lại.");

        if (request?.ScheduledTime is not null)
            booking.ScheduledTime = ValidateScheduledTime(request.ScheduledTime.Value);
        else if (noShowRetry)
            booking.ScheduledTime = RoundUpToFiveMinutes(DateTime.UtcNow.AddHours(1)); // giờ hẹn cũ đã qua → sớm nhất có thể
        else if (booking.ScheduledTime <= DateTime.UtcNow)
            throw ApiException.BadRequest("Thời gian đặt không hợp lệ.");

        int? oldTasker = null;
        if (noShowRetry)
        {
            oldTasker = booking.TaskerId;
            booking.TaskerId = null;
            booking.IsTaskerNoShow = false;
            booking.NoShowAt = null;
        }

        // Vòng mới: chạy lại từ đợt 1; Tasker đã được mời ở vòng trước được xét lại
        booking.MatchRound += 1;
        booking.CurrentWave = 0;
        booking.NextMatchAt = null;
        booking.SearchExhaustedAt = null;
        booking.ManualSearchAvailableAt = null;
        booking.Status = BookingStatus.Pending;
        booking.UpdatedAt = DateTime.UtcNow;
        if (oldTasker is not null)
            _notifications.Add(oldTasker.Value, "Đơn đã được chuyển cho Tasker khác",
                $"Đơn #{booking.Id} đã được chuyển cho Tasker khác vì bạn không đến đúng hẹn.");
        await _db.SaveChangesAsync();
    }

    // US36 – Tasker làm xong nhưng khách không trả tiền mặt (FR-27): đơn → 13, khách bị ghi nợ, Tasker không bị trừ chiết khấu
    public async Task ReportUnpaidAsync(int bookingId, int taskerId, ReportUnpaidRequest request)
    {
        var booking = await _db.Bookings.Include(b => b.Payment).FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        if (booking.TaskerId != taskerId) throw ApiException.Forbidden();
        if (booking.PaymentMethod != PaymentMethod.Cash || booking.Status != BookingStatus.InProgress || booking.Payment is not null)
            throw ApiException.BadRequest("Chỉ báo được với đơn tiền mặt đang thực hiện.");

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();
        booking.Status = BookingStatus.PaymentOverdue;
        booking.CheckOutPhotoUrl = UploadUrl.Normalize(request.PhotoUrl);
        if (booking.CheckOutPhotoUrl is not null)
            _db.BookingPhotos.Add(new BookingPhoto
            {
                BookingId = booking.Id, Stage = BookingPhotoStage.CheckOut, Url = booking.CheckOutPhotoUrl,
                Note = "Ảnh khi báo khách chưa thanh toán", CreatedAt = now,
            });
        booking.CompletedAt = now; // mốc 24 giờ để khách khiếu nại nếu cho rằng đã trả
        booking.UpdatedAt = now;

        var customer = await _db.Users.FirstAsync(u => u.Id == booking.CustomerId);
        customer.OutstandingAmount += booking.TotalPrice;
        customer.UnpaidReportCount += 1;
        if (customer.UnpaidReportCount >= 2) customer.IsCashBlocked = true; // BR-U01: từ lần thứ 2 chỉ được trả online

        _audit.Add(AuditService.Debt, booking.TotalPrice, booking.Id, null,
            $"Tasker #{taskerId} báo khách chưa thanh toán tiền mặt: {request.Note.Trim()}");
        _notifications.Add(booking.CustomerId, "Bạn có khoản cần thanh toán",
            $"Tasker báo đơn #{booking.Id} chưa được thanh toán {MoneyFormat.Vnd(booking.TotalPrice)}. " +
            "Vui lòng thanh toán online để tiếp tục đặt dịch vụ. Nếu bạn đã trả, hãy khiếu nại trong 24 giờ.");

        await _db.SaveChangesAsync();
        await _wallet.RecalculateReservedAsync(taskerId); // giải phóng phần tạm giữ, không trừ chiết khấu
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static DateTime RoundUpToFiveMinutes(DateTime utc)
    {
        var ticks = TimeSpan.FromMinutes(5).Ticks;
        return new DateTime((utc.Ticks + ticks - 1) / ticks * ticks, DateTimeKind.Utc);
    }
}
