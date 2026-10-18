using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP05 - Thanh toán (SRS 7.5). Bất biến: RefundedAmount + TaskerAmount + CommissionAmount = Amount khi đã kết thúc.
public class Payment
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public decimal Amount { get; set; }
    public decimal CommissionAmount { get; set; } // US22: tính khi giải ngân
    public PaymentMethod Method { get; set; } = PaymentMethod.EWallet;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public decimal RefundedAmount { get; set; } // tổng tiền đã hoàn cho khách
    public decimal TaskerAmount { get; set; }   // số tiền đã giải ngân vào ví Tasker
    public DateTime? ReleasedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? GatewayTransactionId { get; set; } // mô phỏng cổng thanh toán
}
