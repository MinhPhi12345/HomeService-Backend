using HomeService.Api.Enums;

namespace HomeService.Api.Models;

/// <summary>US48 – yêu cầu mở khóa tài khoản (người bị khóa gửi sau khi xác thực email bằng OTP).</summary>
public class AccountAppeal
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Lần khóa mà yêu cầu này kháng cáo (= User.LockedAt lúc gửi) – để đếm số lần kháng cáo mỗi lần khóa.</summary>
    public DateTime LockedAt { get; set; }

    public string Content { get; set; } = string.Empty;
    public string AttachmentUrls { get; set; } = string.Empty; // URL cách nhau dấu phẩy (tối đa 3 ảnh)

    public AppealStatus Status { get; set; } = AppealStatus.Pending;
    public string? AdminNote { get; set; }
    public int? ResolvedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
