namespace HomeService.Api.Options;

// appsettings.json → "Otp": mã OTP 6 số gửi qua email
public class OtpOptions
{
    public const string Section = "Otp";

    /// <summary>Thời gian hiệu lực của mã (phút).</summary>
    public int ExpiryMinutes { get; set; } = 5;

    /// <summary>Số lần nhập sai tối đa cho 1 mã; vượt quá phải gửi mã mới.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Thời gian chờ giữa 2 lần gửi mã cùng mục đích (giây).</summary>
    public int ResendSeconds { get; set; } = 60;

    /// <summary>Số mã tối đa cho 1 tài khoản / 1 mục đích trong 1 giờ (chống spam email).</summary>
    public int MaxPerHour { get; set; } = 5;
}
