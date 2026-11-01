using HomeService.Api.Enums;

namespace HomeService.Api.Models;

/// <summary>
/// Mã OTP 6 số gửi qua email. Chỉ lưu bản băm (không lưu mã gốc), hết hạn sau vài phút,
/// giới hạn số lần nhập sai; mã mới gửi thì các mã cũ cùng mục đích bị vô hiệu.
/// </summary>
public class OtpCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public OtpPurpose Purpose { get; set; }
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Rút tiền: mã chỉ dùng được cho đúng số tiền đã yêu cầu.</summary>
    public decimal? Amount { get; set; }

    public int Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
