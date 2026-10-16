namespace HomeService.Api.Services.Interfaces;

// EP07 – tạo thông báo trong ứng dụng (bảng Notifications, SRS FR-21)
public interface INotificationService
{
    /// <summary>Thêm thông báo vào DbContext hiện tại; người gọi tự SaveChanges (để chung 1 transaction).</summary>
    void Add(int userId, string title, string message);

    /// <summary>Gửi cho tất cả Admin đang hoạt động.</summary>
    Task AddForAdminsAsync(string title, string message);
}
