using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP05 - Thanh toán (US21-US24)
public class Payment
{
    public int Id { get; set; }

    public int BookingId { get; set; }
    public Booking? Booking { get; set; }

    public decimal Amount { get; set; }
    public decimal CommissionAmount { get; set; } // US22: hoa hồng nền tảng
    public PaymentMethod Method { get; set; } = PaymentMethod.EWallet;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
