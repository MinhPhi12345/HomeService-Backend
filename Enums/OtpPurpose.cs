namespace HomeService.Api.Enums;

/// <summary>Mục đích của mã OTP gửi qua email – mỗi mục đích có mã riêng, không dùng chéo.</summary>
public enum OtpPurpose
{
    VerifyEmail = 0,     // Xác thực email khi đăng ký tài khoản
    ResetPassword = 1,   // Quên mật khẩu
    ChangePassword = 2,  // Đổi mật khẩu khi đã đăng nhập
    Withdraw = 3,        // Tasker xác nhận rút tiền ví
    AccountAppeal = 4,   // Tài khoản bị khóa xác thực email để gửi yêu cầu mở khóa
    AppealSession = 5,   // Phiên gửi yêu cầu mở khóa (mã ngẫu nhiên dài, cấp sau khi nhập đúng OTP)
}
