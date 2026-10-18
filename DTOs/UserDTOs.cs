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
    DateTime? SuspendedUntil
);
