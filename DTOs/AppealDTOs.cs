using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US48 – yêu cầu mở khóa tài khoản (SRS v1.8)

/// <summary>Bước 2: nhập mã OTP đã gửi về email của tài khoản bị khóa.</summary>
public class AppealVerifyRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP gồm 6 chữ số.")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Bước 3: gửi giải trình (kèm token phiên nhận được ở bước 2).</summary>
public class SubmitAppealRequest
{
    [Required(ErrorMessage = "Phiên gửi yêu cầu đã hết hạn, vui lòng xác thực email lại.")]
    [MaxLength(200)]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng viết giải trình (20–2000 ký tự).")]
    [StringLength(2000, MinimumLength = 20, ErrorMessage = "Vui lòng viết giải trình (20–2000 ký tự).")]
    public string Content { get; set; } = string.Empty;

    [MaxLength(3, ErrorMessage = "Tối đa 3 ảnh đính kèm.")]
    public List<string>? AttachmentUrls { get; set; }
}

public class ResolveAppealRequest
{
    [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    public string? Note { get; set; }
}

public class RejectAppealRequest
{
    [Required(ErrorMessage = "Vui lòng ghi lý do từ chối (10–1000 ký tự).")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Vui lòng ghi lý do từ chối (10–1000 ký tự).")]
    public string Note { get; set; } = string.Empty;
}

public record AppealHistoryItem(int Id, AppealStatus Status, DateTime CreatedAt, DateTime? ResolvedAt, string? AdminNote);

/// <summary>Thông tin hiện cho người bị khóa sau khi xác thực email.</summary>
public record AppealContextResponse(
    string FullName,
    string MaskedEmail,
    AccountLockReason? LockReason,
    string LockReasonText,
    string? LockDetail,
    DateTime? LockedAt,
    bool CanSubmit,
    string? BlockedMessage,
    int RemainingAppeals,
    DateTime? NextAppealAt,
    IReadOnlyList<AppealHistoryItem> History);

public record AppealSessionResponse(string Token, DateTime ExpiresAt, AppealContextResponse Context);

// Admin – danh sách & chi tiết
public record AppealListItem(
    int Id, int UserId, string FullName, string Email, UserRole Role,
    AccountLockReason? LockReason, DateTime LockedAt, int AttemptNo,
    AppealStatus Status, string ContentPreview, DateTime CreatedAt, DateTime? ResolvedAt);

public record AppealUserSummary(
    int Id, string FullName, string Email, string? PhoneNumber, UserRole Role, bool IsActive, DateTime CreatedAt,
    AccountLockReason? LockReason, string? LockDetail, DateTime? LockedAt, string? LockedByName,
    decimal OutstandingAmount, bool IsCashBlocked, int UnpaidReportCount,
    int? ViolationPoints, decimal? RatingAverage,
    int TotalBookings, int CompletedBookings, int CancelledBookings, int DisputesAgainst, int DisputesFiled);

public record AppealDetailResponse(
    int Id, AppealStatus Status, string Content, List<string> AttachmentUrls,
    DateTime CreatedAt, DateTime? ResolvedAt, string? AdminNote, string? ResolvedByName,
    int AttemptNo, int MaxAttempts, AppealUserSummary User, IReadOnlyList<AppealHistoryItem> PreviousAppeals);
