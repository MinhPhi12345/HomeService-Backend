using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// EP01 - US04: Xác thực hồ sơ Tasker (SRS 7.2)
public class TaskerProfile
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string IdCardNumber { get; set; } = string.Empty; // CCCD 12 chữ số; rỗng = Tasker chưa gửi hồ sơ
    public string? IdCardImageUrl { get; set; }
    public string? Skills { get; set; } // vd: "Dọn dẹp, Sửa điện nước"
    public string? WorkingArea { get; set; } // khu vực (SRS 7.9) – hiển thị & gợi ý Tasker

    public TaskerVerificationStatus VerificationStatus { get; set; } = TaskerVerificationStatus.Pending;
    public decimal RatingAverage { get; set; } = 5.0m;
    public decimal WalletBalance { get; set; } = 0m; // US23: ví trả trước, luôn ≥ 0 (BR-P04)
    public decimal ReservedAmount { get; set; } = 0m; // BR-P05: hoa hồng tạm giữ cho đơn tiền mặt đang thực hiện

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // US04 (SRS v1.1): điểm làm việc + bán kính phục vụ – dùng cho ghép nối (BR-M06)
    public string BaseAddress { get; set; } = string.Empty;
    public double BaseLatitude { get; set; }
    public double BaseLongitude { get; set; }
    public int ServiceRadiusKm { get; set; } = 5; // 3, 5 hoặc 8

    public bool IsAvailable { get; set; } = true; // Tasker đang bật nhận đơn

    // US16 – vị trí hiện tại app gửi khi bật nhận đơn; quá 60 phút thì dùng điểm làm việc (BR-M06)
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public DateTime? LastLocationAt { get; set; }
    public decimal AvailableBalance => WalletBalance - ReservedAmount; // không lưu DB

    public DateTime? SubmittedAt { get; set; } // lần gửi hồ sơ gần nhất (để Admin sắp xếp danh sách chờ duyệt)
}
