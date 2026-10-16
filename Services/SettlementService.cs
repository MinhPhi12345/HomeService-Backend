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
/// Dòng tiền của đơn (FR-16, FR-25): giải ngân đơn online đang tạm giữ, quyết toán đơn tiền mặt.
/// Mọi hàm chạy trong transaction của người gọi; Payment.Status là concurrency token nên
/// không bao giờ giải ngân 2 lần (NFR-17).
/// </summary>
public class SettlementService
{
    private readonly AppDbContext _db;
    private readonly WalletService _wallet;
    private readonly INotificationService _notifications;
    private readonly AuditService _audit;
    private readonly ILogger<SettlementService> _logger;
    private readonly SettlementOptions _opt;

    public SettlementService(AppDbContext db, WalletService wallet, INotificationService notifications,
        AuditService audit, ILogger<SettlementService> logger, IOptions<SettlementOptions> options)
    {
        _db = db;
        _wallet = wallet;
        _notifications = notifications;
        _audit = audit;
        _logger = logger;
        _opt = options.Value;
    }

    /// <summary>
    /// US22 – Tính chiết khấu nền tảng khi giải ngân (BR-P01):
    /// commission = làm tròn((amount − refundedAmount) × commissionRate); Tasker nhận phần còn lại.
    /// Tỷ lệ lấy từ Booking.CommissionRate (đã chốt lúc tạo đơn) chứ không lấy cấu hình hiện tại.
    /// </summary>
    public static (decimal Commission, decimal TaskerAmount) SplitOnline(decimal amount, decimal refunded, decimal rate)
    {
        var payable = amount - refunded;
        var commission = Money.Commission(payable, rate);
        return (commission, payable - commission);
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
        var (commission, taskerAmount) = SplitOnline(payment.Amount, payment.RefundedAmount, booking.CommissionRate);

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

        AssertInvariant(payment);
        _audit.Add(AuditService.Released, taskerAmount, booking.Id, payment.Id,
            $"Giải ngân {(isAuto ? "tự động sau hạn xác nhận" : "khách xác nhận")}: Tasker {taskerAmount}, chiết khấu {commission}, đã hoàn {payment.RefundedAmount}");

        booking.Status = BookingStatus.Paid;
        booking.IsAutoConfirmed = isAuto;
        booking.UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Hoàn tiền khoản đang tạm giữ và kết thúc Payment: khách nhận refundAmount, Tasker nhận feeToTasker (phí hủy,
    /// không thu chiết khấu – BR-C01). Dùng khi hủy đơn, không có Tasker nhận, Tasker không đến, khiếu nại hoàn toàn bộ.
    /// </summary>
    public async Task<bool> RefundHeldAsync(Booking booking, decimal feeToTasker, string reason)
    {
        var payment = booking.Payment ?? await _db.Payments.FirstOrDefaultAsync(p => p.BookingId == booking.Id);
        if (payment is null || payment.Status != PaymentStatus.Held) return false;

        var now = DateTime.UtcNow;
        feeToTasker = Math.Clamp(Money.Round(feeToTasker), 0, payment.Amount);
        var refund = payment.Amount - feeToTasker;

        payment.RefundedAmount = refund;
        payment.TaskerAmount = feeToTasker;
        payment.CommissionAmount = 0;
        payment.RefundedAt = now;
        payment.Status = feeToTasker > 0 ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
        AssertInvariant(payment);

        _audit.Add(AuditService.Refund, refund, booking.Id, payment.Id, reason);
        _notifications.Add(booking.CustomerId, "Đã hoàn tiền",
            $"Đơn #{booking.Id}: hoàn {MoneyFormat.Vnd(refund)} về phương thức bạn đã thanh toán. {reason}");

        if (feeToTasker > 0 && booking.TaskerId is not null)
        {
            await _wallet.ApplyAsync(booking.TaskerId.Value, feeToTasker, WalletTransactionType.CancellationFee,
                booking.Id, $"Phí hủy đơn #{booking.Id}");
            _audit.Add(AuditService.CancellationFee, feeToTasker, booking.Id, payment.Id, "Phí hủy chuyển cho Tasker");
            _notifications.Add(booking.TaskerId.Value, "Nhận phí hủy đơn",
                $"Đơn #{booking.Id} bị khách hủy, bạn nhận {MoneyFormat.Vnd(feeToTasker)} phí hủy.");
        }
        return true;
    }

    /// <summary>BR-P07 / NFR-16: refundedAmount + taskerAmount + commissionAmount = amount cho mọi Payment đã kết thúc.</summary>
    private void AssertInvariant(Payment p)
    {
        if (p.RefundedAmount + p.TaskerAmount + p.CommissionAmount != p.Amount)
        {
            _logger.LogError("Sai bất biến tiền ở Payment {Id}: {R} + {T} + {C} != {A}",
                p.Id, p.RefundedAmount, p.TaskerAmount, p.CommissionAmount, p.Amount);
            throw new InvalidOperationException("Sai lệch số tiền khi quyết toán.");
        }
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
            var tx = await _wallet.ApplyAsync(taskerId, -commission, WalletTransactionType.Commission, booking.Id,
                $"Chiết khấu đơn tiền mặt #{booking.Id}");
            _notifications.Add(taskerId, "Ví bị trừ chiết khấu",
                $"Đơn #{booking.Id}: trừ {MoneyFormat.Vnd(commission)} chiết khấu nền tảng từ ví trả trước.");
            if (tx.BalanceAfter < _opt.LowBalanceThreshold)
                _notifications.Add(taskerId, "Số dư ví thấp",
                    $"Số dư ví còn {MoneyFormat.Vnd(tx.BalanceAfter)}. Hãy nạp thêm để tiếp tục nhận đơn tiền mặt.");
        }
        _audit.Add(AuditService.CashCommission, commission, booking.Id, null,
            $"Đơn tiền mặt {booking.TotalPrice}: trừ chiết khấu {commission} từ ví Tasker #{taskerId}");
        booking.Status = BookingStatus.Paid;
        booking.UpdatedAt = now;
    }
}
