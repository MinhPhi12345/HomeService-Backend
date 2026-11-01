namespace HomeService.Api.Options;

/// <summary>
/// appsettings → "Email": máy chủ SMTP gửi mã OTP (mặc định Gmail SMTP).
/// Username/Password (mật khẩu ứng dụng Gmail) là BÍ MẬT – chỉ đặt trong appsettings.Secrets.json hoặc biến môi trường.
/// Enabled = false: không gửi thật, nội dung email (kèm mã) chỉ được ghi ra log – dùng khi phát triển.
/// </summary>
public class EmailOptions
{
    public const string Section = "Email";

    public bool Enabled { get; set; }
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Địa chỉ người gửi; bỏ trống thì dùng Username.</summary>
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "HomeService";
    public int TimeoutSeconds { get; set; } = 20;
}
