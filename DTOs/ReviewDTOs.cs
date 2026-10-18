using System.ComponentModel.DataAnnotations;

namespace HomeService.Api.DTOs;

// US25/US26 – body của POST /api/reviews (SRS 9.8)
public record CreateReviewRequest(
    [Required(ErrorMessage = "Thiếu mã đơn.")] int? BookingId,
    [Required(ErrorMessage = "Thiếu người được đánh giá.")] int? RevieweeId,
    [Required(ErrorMessage = "Vui lòng chọn số sao.")]
    [Range(1, 5, ErrorMessage = "Vui lòng chọn số sao.")] int? Rating,
    [MaxLength(500, ErrorMessage = "Nhận xét tối đa 500 ký tự.")] string? Comment
);

public record ReviewResponse(
    int Id,
    int BookingId,
    int ReviewerId,
    string ReviewerName,
    int RevieweeId,
    int Rating,
    string? Comment,
    bool IsFlagged,
    DateTime CreatedAt,
    // Bổ sung
    string RevieweeName
);
