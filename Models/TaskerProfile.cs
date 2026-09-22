using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP01 - US04: Xác thực hồ sơ Tasker
public class TaskerProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string IdCardNumber { get; set; } = string.Empty;
    public string? IdCardImageUrl { get; set; }
    public string? Skills { get; set; } // vd: "Dọn dẹp, Sửa điện nước"
    public string? WorkingArea { get; set; } // khu vực hoạt động, dùng cho matching (US16)

    public TaskerVerificationStatus VerificationStatus { get; set; } = TaskerVerificationStatus.Pending;
    public decimal RatingAverage { get; set; } = 5.0m;
    public decimal WalletBalance { get; set; } = 0m; // US23: rút tiền từ ví

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
