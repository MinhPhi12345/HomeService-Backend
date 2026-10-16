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

    public BookingService(AppDbContext db, INotificationService notifications,
        IOptions<BusinessOptions> business, IOptions<SettlementOptions> settlement,
        SettlementService settlementService, WalletService wallet, IOptions<MatchingOptions> matching)
    {
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

        // FR-27 / BR-U01: khách đang nợ không tạo được đơn mới
        if (customer.OutstandingAmount > 0)
            throw ApiException.BadRequest("Vui lòng thanh toán khoản nợ trước khi đặt đơn mới.");

        var quantity = request.Quantity ?? 1;
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

        var booking = new Booking
        {
            CustomerId = customerId,
            ServiceCategoryId = service.Id,
            ScheduledTime = scheduledTime,
            Address = request.Address.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Quantity = quantity,
            TotalPrice = totalPrice,
            PaymentMethod = method,
            CommissionRate = _business.CommissionRate, // BR-P01: chốt tỷ lệ vào đơn
            CustomerPresent = customerPresent,
            CreatedAt = now,
        };

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
                $"Đơn {service.Name} đã được tạo. Vui lòng thanh toán trong {_settlement.PaymentTimeoutMinutes} phút để hệ thống tìm Tasker.");
        }

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();
        return booking;
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
            .FirstOrDefaultAsync(b => b.Id == bookingId)
            ?? throw ApiException.NotFound();
        if (booking.TaskerId != taskerId) throw ApiException.Forbidden();

        var now = DateTime.UtcNow;
        var target = request.Status!.Value;
        var photo = string.IsNullOrWhiteSpace(request.PhotoUrl) ? null : request.PhotoUrl.Trim();

        if (target == BookingStatus.InProgress && booking.Status == BookingStatus.Accepted)
        {
            if (photo is null) throw ApiException.BadRequest("Vui lòng chụp ảnh hiện trạng.");
            booking.Status = BookingStatus.InProgress;
            booking.CheckInPhotoUrl = photo;
            booking.UpdatedAt = now;
            _notifications.Add(booking.CustomerId, "Tasker đã bắt đầu làm việc",
                $"Đơn {booking.ServiceCategory!.Name} đang được thực hiện.");
            await _db.SaveChangesAsync();
            return;
        }

        if (target == BookingStatus.Completed && booking.Status == BookingStatus.InProgress)
        {
            if (photo is null) throw ApiException.BadRequest("Vui lòng chụp ảnh hiện trạng.");

            if (booking.PaymentMethod == PaymentMethod.Cash)
            {
                if (request.CashCollected != true)
                    throw ApiException.BadRequest("Vui lòng xác nhận đã thu đủ tiền mặt hoặc chọn \"Khách chưa thanh toán\".");

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

    // US33 – Khách chọn Thử lại (có thể đổi giờ) khi đơn "Chưa có Tasker nhận" (FR-24)
    public async Task RetryAsync(int bookingId, int customerId, RetryBookingRequest? request)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        if (booking.CustomerId != customerId) throw ApiException.Forbidden();
        if (booking.Status != BookingStatus.NoTaskerFound)
            throw ApiException.BadRequest("Đơn không ở trạng thái chờ thử lại.");
        if (booking.MatchRound >= _matching.MaxRounds)
            throw ApiException.BadRequest("Đã vượt quá số lần thử lại.");

        if (request?.ScheduledTime is not null)
            booking.ScheduledTime = ValidateScheduledTime(request.ScheduledTime.Value);
        else if (booking.ScheduledTime <= DateTime.UtcNow)
            throw ApiException.BadRequest("Thời gian đặt không hợp lệ.");

        // Vòng mới: chạy lại từ đợt 1; Tasker đã được mời ở vòng trước được xét lại
        booking.MatchRound += 1;
        booking.CurrentWave = 0;
        booking.NextMatchAt = null;
        booking.Status = BookingStatus.Pending;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
