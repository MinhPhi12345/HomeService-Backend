using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US35 – SRS 9.11
public class CreateDisputeRequest
{
    [Required(ErrorMessage = "Vui lòng chọn lý do khiếu nại.")]
    public DisputeReason? Reason { get; set; }

    [Required(ErrorMessage = "Vui lòng mô tả vấn đề (ít nhất 10 ký tự).")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Vui lòng mô tả vấn đề (ít nhất 10 ký tự).")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng đính kèm từ 1 đến 5 ảnh.")]
    [MinLength(1, ErrorMessage = "Vui lòng đính kèm từ 1 đến 5 ảnh.")]
    [MaxLength(5, ErrorMessage = "Vui lòng đính kèm từ 1 đến 5 ảnh.")]
    public List<string> EvidenceUrls { get; set; } = new();
}

public class TaskerDisputeResponseRequest
{
    [Required(ErrorMessage = "Vui lòng nhập phản hồi (ít nhất 10 ký tự).")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Vui lòng nhập phản hồi (ít nhất 10 ký tự).")]
    public string Response { get; set; } = string.Empty;

    [MaxLength(5, ErrorMessage = "Tối đa 5 ảnh.")]
    public List<string>? EvidenceUrls { get; set; }
}

public class ResolveDisputeRequest
{
    [Required(ErrorMessage = "Vui lòng chọn kết quả xử lý.")]
    public DisputeResolution? Resolution { get; set; }

    public decimal? RefundAmount { get; set; }
    public DateTime? RedoScheduledTime { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi lý do quyết định.")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Vui lòng ghi lý do quyết định.")]
    public string AdminNote { get; set; } = string.Empty;
}

public record DisputeResponse(
    int Id,
    int BookingId,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string CustomerName,
    string TaskerName,
    DisputeReason Reason,
    string Description,
    List<string> EvidenceUrls,
    string? CheckInPhotoUrl,
    string? CheckOutPhotoUrl,
    string? TaskerResponse,
    List<string> TaskerEvidenceUrls,
    DisputeStatus Status,
    DisputeResolution? Resolution,
    decimal? RefundAmount,
    string? AdminNote,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    // Bổ sung
    int CustomerId,
    int TaskerId,
    DateTime? RedoScheduledTime,
    DateTime TaskerResponseDueAt
);
