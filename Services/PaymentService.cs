using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>US21 – Thanh toán trước & tạm giữ tiền (FR-16 Luồng 2). Cổng thanh toán được MÔ PHỎNG (thành công ngay).</summary>
public class PaymentService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly AuditService _audit;

    public PaymentService(AppDbContext db, INotificationService notifications, AuditService audit)
    {
        _db = db;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<Payment> CreateAsync(int customerId, CreatePaymentRequest request)
    {
        var now = DateTime.UtcNow;
        var booking = await _db.Bookings
            .Include(b => b.Payment)
            .Include(b => b.ServiceCategory)
            .FirstOrDefaultAsync(b => b.Id == request.BookingId)
            ?? throw ApiException.NotFound();
        if (booking.CustomerId != customerId) throw ApiException.Forbidden();

        var method = request.Method!.Value;

        if (booking.Status != BookingStatus.AwaitingPayment)
            throw ApiException.BadRequest(booking.Payment is not null
                ? "Đơn đã được thanh toán."
                : "Đơn không ở trạng thái chờ thanh toán.");
        if (booking.Payment is not null)
            throw ApiException.BadRequest("Đơn đã được thanh toán.");

        // Chỉ ví điện tử / thẻ, và phải khớp phương thức đã chọn khi đặt đơn
        if (method == PaymentMethod.Cash || method != booking.PaymentMethod || !Enum.IsDefined(method))
            throw ApiException.BadRequest("Phương thức thanh toán không khớp đơn.");
        if (booking.PaymentDueAt is not null && booking.PaymentDueAt < now)
            throw ApiException.BadRequest("Đơn đã quá hạn thanh toán.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Đổi trạng thái có điều kiện → 2 request thanh toán cùng lúc chỉ 1 cái thành công
        var rows = await _db.Bookings
            .Where(b => b.Id == booking.Id && b.Status == BookingStatus.AwaitingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.Pending)   // bắt đầu ghép nối (FR-12)
                .SetProperty(b => b.CurrentWave, 0)
                .SetProperty(b => b.NextMatchAt, (DateTime?)null)
                .SetProperty(b => b.UpdatedAt, now));
        if (rows == 0) throw ApiException.BadRequest("Đơn đã được thanh toán.");

        var payment = new Payment
        {
            BookingId = booking.Id,
            Amount = booking.TotalPrice,
            Method = method,
            Status = PaymentStatus.Held, // tiền được hệ thống tạm giữ
            PaidAt = now,
            CreatedAt = now,
            GatewayTransactionId = $"SIM{now:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _audit.Add(AuditService.PaymentHeld, payment.Amount, booking.Id, payment.Id,
            $"Khách trả trước {payment.Amount} qua {method} ({payment.GatewayTransactionId}) – hệ thống tạm giữ");
        _notifications.Add(customerId, "Thanh toán thành công",
            $"Đã thanh toán {MoneyFormat.Vnd(payment.Amount)} cho đơn {booking.ServiceCategory!.Name}. " +
            "Hệ thống đang tạm giữ tiền và tìm Tasker cho bạn.");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return payment;
    }

    public static PaymentResponse ToResponse(Payment p, string serviceName) => new(
        p.Id, p.BookingId, serviceName, p.Amount, p.CommissionAmount, p.Method, p.Status,
        p.PaidAt, p.CreatedAt, p.TaskerAmount, p.RefundedAmount, p.ReleasedAt);
}
