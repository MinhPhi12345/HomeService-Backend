using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP01 - Quản lý tài khoản (US01, US02, US05, US06)
public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsActive { get; set; } = true; // US30: khóa/mở khóa tài khoản
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Chỉ có giá trị khi Role = Tasker
    public TaskerProfile? TaskerProfile { get; set; }
}
