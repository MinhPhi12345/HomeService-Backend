namespace HomeService.Api.Options;

// appsettings.json → "Account" – khóa tài khoản & yêu cầu mở khóa (US30, US48)
public class AccountOptions
{
    public const string Section = "Account";

    /// <summary>Số lần gửi yêu cầu mở khóa tối đa cho mỗi lần bị khóa.</summary>
    public int AppealMaxPerLock { get; set; } = 3;

    /// <summary>Bị từ chối thì phải chờ số ngày này mới gửi lại được.</summary>
    public int AppealCooldownDays { get; set; } = 7;

    /// <summary>Sau khi nhập đúng OTP, phiên gửi yêu cầu (tải ảnh + gửi) có hiệu lực trong số phút này.</summary>
    public int AppealSessionMinutes { get; set; } = 30;

    /// <summary>Số ảnh đính kèm tối đa.</summary>
    public int AppealMaxAttachments { get; set; } = 3;

    /// <summary>Địa chỉ website khách hàng – dùng tạo đường dẫn "Gửi yêu cầu mở khóa" trong email.</summary>
    public string WebBaseUrl { get; set; } = "https://homeservice.id.vn";
}
