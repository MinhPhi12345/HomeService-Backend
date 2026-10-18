using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>
/// Dòng tiền của đơn (FR-16, FR-25): giải ngân đơn online đang tạm giữ, quyết toán đơn tiền mặt.
/// Mọi hàm chạy trong transaction của người gọi; Payment.Status là concurrency token nên
/// không bao giờ giải ngân 2 lần (NFR-17).
/// </summary>
public class SettlementService
{
    private readonly AppDbContext _db;
    private readonly WalletService _wallet;
    private readonly INotificationService _notifications;

    public SettlementService(AppDbContext db, WalletService wallet, INotificationService notifications)
    {
        _db = db;
        _wallet = wallet;
        _notifications = notifications;
    }

    /// <summary>
    /// Giải ngân khoản tạm giữ của đơn online: Tasker nhận amount − refundedAmount − chiết khấu, nền tảng giữ chiết khấu.
    /// Trả false nếu đã giải ngân trước đó (idempotent).
    /// </summary>
    public async Task<bool> ReleaseOnlineAsync(Booking booking, bool isAuto)
    {
        var payment = booking.Payment ?? await _db.Payments.FirstOrDefaultAsync(p => p.BookingId == booking.Id);
        if (payment is null || payment.Status != PaymentStatus.Held) return false;

        var now = DateTime.UtcNow;
        var payable = payment.Amount - payment.RefundedAmount;
        var commission = Money.Commission(payable, booking.CommissionRate);
        var taskerAmount = payable - commission;

        payment.CommissionAmount = commission;
        payment.TaskerAmount = taskerAmount;
        payment.ReleasedAt = now;
        payment.Status = payment.RefundedAmount > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Released;

        if (taskerAmount > 0 && booking.TaskerId is not null)
        {
            await _wallet.ApplyAsync(booking.TaskerId.Value, taskerAmount, WalletTransactionType.OnlineEarning,
                booking.Id, $"Thu nhập đơn #{booking.Id} (đã trừ chiết khấu {MoneyFormat.Vnd(commission)})");
            _notifications.Add(booking.TaskerId.Value, "Tiền đã được giải ngân",
                $"Đơn #{booking.Id}: {MoneyFormat.Vnd(taskerAmount)} đã được cộng vào ví của bạn.");
        }

        booking.Status = BookingStatus.Paid;
        booking.IsAutoConfirmed = isAuto;
        booking.UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Đơn tiền mặt hoàn thành, Tasker xác nhận đã thu đủ: ghi Payment (Success), trừ chiết khấu từ ví Tasker
    /// và giải phóng phần tạm giữ (FR-16 Luồng 1).
    /// </summary>
    public async Task SettleCashAsync(Booking booking)
    {
        var now = DateTime.UtcNow;
        var commission = Money.Commission(booking.TotalPrice, booking.CommissionRate);
        var payment = new Payment
        {
            BookingId = booking.Id,
            Amount = booking.TotalPrice,
            CommissionAmount = commission,
            TaskerAmount = booking.TotalPrice - commission, // Tasker đã giữ tiền mặt
            Method = PaymentMethod.Cash,
            Status = PaymentStatus.Success,
            PaidAt = now,
        };
        _db.Payments.Add(payment);
        booking.Payment = payment;

        var taskerId = booking.TaskerId!.Value;
        if (commission > 0)
        {
            await _wallet.ApplyAsync(taskerId, -commission, WalletTransactionType.Commission, booking.Id,
                $"Chiết khấu đơn tiền mặt #{booking.Id}");
            _notifications.Add(taskerId, "Ví bị trừ chiết khấu",
                $"Đơn #{booking.Id}: trừ {MoneyFormat.Vnd(commission)} chiết khấu nền tảng từ ví trả trước.");
        }
        booking.Status = BookingStatus.Paid;
        booking.UpdatedAt = now;
    }
}
