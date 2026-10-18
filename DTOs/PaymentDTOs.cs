using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US21 – body của POST /api/payments (SRS 9.7)
public class CreatePaymentRequest
{
    [Required(ErrorMessage = "Thiếu mã đơn.")]
    public int? BookingId { get; set; }

    [Required(ErrorMessage = "Phương thức thanh toán không khớp đơn.")]
    public PaymentMethod? Method { get; set; }
}

public record PaymentResponse(
    int Id,
    int BookingId,
    string ServiceCategoryName,
    decimal Amount,
    decimal CommissionAmount,
    PaymentMethod Method,
    PaymentStatus Status,
    DateTime? PaidAt,
    DateTime CreatedAt,
    decimal TaskerAmount,
    decimal RefundedAmount,
    DateTime? ReleasedAt
);
