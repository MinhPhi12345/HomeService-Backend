using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// Phần tử của GET /api/users (SRS 9.6). Field bổ sung thêm vào cuối, null khi không áp dụng.
public record AdminUserItem(
    int Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt,
    // Bổ sung cho trang quản lý người dùng
    decimal OutstandingAmount,
    bool IsCashBlocked,
    int UnpaidReportCount,
    TaskerVerificationStatus? VerificationStatus,
    decimal? RatingAverage,
    int? ViolationPoints,
    DateTime? SuspendedUntil,
    // US30 (SRS v1.8) – lý do khóa, chờ khóa, yêu cầu mở khóa đang chờ
    AccountLockReason? LockReason = null,
    string? LockDetail = null,
    DateTime? LockedAt = null,
    DateTime? LockPendingAt = null,
    int PendingAppeals = 0
);

// US30 – POST /api/users/{id}/lock và /lock-preview
public class LockAccountRequest
{
    [Required(ErrorMessage = "Vui lòng chọn lý do khóa.")]
    public AccountLockReason? Reason { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi chi tiết lý do (10–1000 ký tự).")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Vui lòng ghi chi tiết lý do (10–1000 ký tự).")]
    public string Detail { get; set; } = string.Empty;

    /// <summary>0 = khóa sau khi hoàn tất các đơn đang dở (mặc định), 1 = khóa ngay.</summary>
    public AccountLockMode Mode { get; set; } = AccountLockMode.AfterOrders;
}

// US30 – POST /api/users/{id}/unlock
public class UnlockAccountRequest
{
    [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự.")]
    public string? Note { get; set; }
}

/// <summary>Đơn đang dở của người bị khóa và cách hệ thống sẽ xử lý nếu khóa ngay.</summary>
public record ActiveOrderItem(int BookingId, string ServiceName, BookingStatus Status, DateTime ScheduledTime,
    PaymentMethod PaymentMethod, string ImmediateAction);

public record LockPreviewResponse(IReadOnlyList<ActiveOrderItem> ActiveOrders, string EmailSubject, string EmailText);

/// <summary>Status: "Locked" (đã khóa) hoặc "Pending" (chờ khóa sau khi hoàn tất đơn).</summary>
public record LockResultResponse(string Status, int ActiveOrders, IReadOnlyList<string> Actions);
