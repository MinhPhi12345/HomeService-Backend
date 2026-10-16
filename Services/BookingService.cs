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

    public BookingService(AppDbContext db, INotificationService notifications,
        IOptions<BusinessOptions> business, IOptions<SettlementOptions> settlement)
    {
        _db = db;
        _notifications = notifications;
        _business = business.Value;
        _settlement = settlement.Value;
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
}
