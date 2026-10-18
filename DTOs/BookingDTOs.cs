using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US12/US13 – body của POST /api/bookings (SRS 9.4)
public class CreateBookingRequest
{
    [Required(ErrorMessage = "Dịch vụ không tồn tại.")]
    public int? ServiceCategoryId { get; set; }

    [Required(ErrorMessage = "Thời gian đặt không hợp lệ.")]
    public DateTime? ScheduledTime { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ.")]
    [StringLength(255, MinimumLength = 5, ErrorMessage = "Vui lòng nhập địa chỉ.")]
    public string Address { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [MaxLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    public string? Note { get; set; }

    [Range(1, 12, ErrorMessage = "Số lượng từ 1 đến 12.")]
    public int? Quantity { get; set; }

    /// <summary>0 Tiền mặt, 1 Ví điện tử (mặc định), 2 Thẻ.</summary>
    public PaymentMethod? PaymentMethod { get; set; }

    /// <summary>Bắt buộc true khi chọn tiền mặt (khách cam kết có mặt lúc hoàn thành).</summary>
    public bool? CustomerPresent { get; set; }
}

// US18 – body của PUT /api/bookings/{id}/status
public class UpdateBookingStatusRequest
{
    /// <summary>Chỉ 3 (InProgress – Bắt đầu) hoặc 4 (Completed – Hoàn thành).</summary>
    [Required(ErrorMessage = "Chuyển trạng thái không hợp lệ.")]
    public BookingStatus? Status { get; set; }

    /// <summary>Bắt buộc: URL ảnh check-in (status 3) / check-out (status 4) lấy từ POST /api/uploads.</summary>
    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    /// <summary>Đơn tiền mặt, status 4: phải là true (đã thu đủ). Chưa thu → dùng report-unpaid.</summary>
    public bool? CashCollected { get; set; }
}

// SRS 9.4 – BookingResponse. Field mới luôn thêm vào CUỐI để không phá FE cũ.
public class BookingResponse
{
    public int Id { get; init; }
    public string ServiceCategoryName { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public int? TaskerId { get; init; }
    public string? TaskerName { get; init; }
    public DateTime ScheduledTime { get; init; }
    public string Address { get; init; } = string.Empty;
    public decimal TotalPrice { get; init; }
    public BookingStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }

    public decimal CommissionRate { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public bool CustomerPresent { get; init; }
    public PaymentStatus? PaymentStatus { get; init; }
    public DateTime? PaymentDueAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? AutoConfirmAt { get; init; }
    public string? CheckInPhotoUrl { get; init; }
    public string? CheckOutPhotoUrl { get; init; }
    public bool IsTaskerNoShow { get; init; }
    public bool CanDispute { get; init; }

    // Bổ sung (SRS 9.4: "Field có thể bổ sung sau")
    public int ServiceCategoryId { get; init; }
    public string? Note { get; init; }
    public int Quantity { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int MatchRound { get; init; }
    public int CurrentWave { get; init; }
}
