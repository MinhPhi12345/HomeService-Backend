namespace HomeService.Api.DTOs;

// SRS 9.9
public record NotificationResponse(int Id, string Title, string Message, bool IsRead, DateTime CreatedAt);

// Bổ sung: kèm số thông báo chưa đọc để hiện badge trên chuông thông báo
public record NotificationPage(IReadOnlyList<NotificationResponse> Items, int Page, int PageSize, int TotalCount, int UnreadCount);
