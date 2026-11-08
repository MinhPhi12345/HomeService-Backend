using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US26 – GET /api/public-profiles/{userId}: hồ sơ công khai của Tasker / khách hàng kèm các đánh giá đã nhận.
// Người viết đánh giá chỉ hiện tên gọi; không trả email, số điện thoại, địa chỉ.
public record PublicProfileResponse(
    int UserId,
    string DisplayName,          // họ tên nếu hai bên đã chung một đơn, ngược lại chỉ tên gọi
    UserRole Role,
    string? AvatarUrl,
    DateTime MemberSince,
    int CompletedBookings,       // số đơn đã hoàn tất với vai trò của người đó
    int ReviewCount,
    decimal? RatingAverage,
    bool? IsVerified,            // chỉ Tasker
    string? WorkingArea,         // chỉ Tasker
    string? Skills,              // chỉ Tasker
    IReadOnlyList<ReviewResponse> Reviews,
    IReadOnlyList<AwardBadgeResponse>? Badges = null // FR-31 – huy hiệu Tasker xuất sắc tháng (chỉ Tasker)
);
