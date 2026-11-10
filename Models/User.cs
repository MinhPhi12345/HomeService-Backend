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

    /// <summary>
    /// Email đã xác thực bằng OTP. Tài khoản tự đăng ký phải nhập mã OTP mới đăng nhập được;
    /// tài khoản cũ / Admin tạo sẵn mặc định đã xác thực.
    /// </summary>
    public bool EmailVerified { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // US12/US36 – điều kiện thanh toán tiền mặt (BR-P06, BR-U01)
    public decimal OutstandingAmount { get; set; } = 0m; // khoản đang nợ; > 0 thì không đặt được đơn mới
    public bool IsCashBlocked { get; set; } = false;      // true → chỉ được thanh toán online
    public int UnpaidReportCount { get; set; } = 0;        // số lần bị báo không thanh toán

    // US30 (SRS v1.8) – khóa tài khoản có lý do; "khóa sau khi hoàn tất đơn" dùng LockPendingAt
    public AccountLockReason? LockReason { get; set; }
    public string? LockDetail { get; set; }      // chi tiết lý do Admin ghi (gửi trong email cho người bị khóa)
    public DateTime? LockedAt { get; set; }      // thời điểm khóa có hiệu lực (IsActive = false)
    public DateTime? LockPendingAt { get; set; } // chờ khóa: không nhận đơn / đặt đơn mới, tự khóa khi các đơn đang dở kết thúc
    public int? LockedById { get; set; }         // Admin thực hiện

    // Chỉ có giá trị khi Role = Tasker
    public TaskerProfile? TaskerProfile { get; set; }
}
