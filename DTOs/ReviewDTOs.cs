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
    string RevieweeName,
    // US27 – thông tin báo cáo / kiểm duyệt: chỉ trả cho người trong đơn và Admin (null với người khác)
    string? ReportReason = null,
    string? ReportedByName = null,
    DateTime? ReportedAt = null,
    string? ModerationNote = null,
    DateTime? ModeratedAt = null
);

// US27 – body của POST /api/reviews/{id}/report: người báo cáo phải nêu lý do (để Admin có căn cứ phân xử)
public record ReportReviewRequest(
    [Required(ErrorMessage = "Vui lòng nêu lý do báo cáo.")]
    [MinLength(10, ErrorMessage = "Lý do báo cáo cần ít nhất 10 ký tự.")]
    [MaxLength(500, ErrorMessage = "Lý do báo cáo tối đa 500 ký tự.")] string? Reason
);

// US27 – body của PUT /api/reviews/{id}/moderation: Admin giữ lại hoặc gỡ đánh giá, bắt buộc ghi lý do
public record ModerateReviewRequest(
    [Required(ErrorMessage = "Vui lòng chọn giữ lại hay gỡ đánh giá.")] bool? Remove,
    [Required(ErrorMessage = "Vui lòng nhập lý do quyết định.")]
    [MinLength(10, ErrorMessage = "Lý do quyết định cần ít nhất 10 ký tự.")]
    [MaxLength(500, ErrorMessage = "Lý do quyết định tối đa 500 ký tự.")] string? Note
);

// US26 – GET /api/reviews/user/{userId}
public record UserReviewSummary(int UserId, int ReviewCount, decimal? RatingAverage, IReadOnlyList<ReviewResponse> Reviews);
