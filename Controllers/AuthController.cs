using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Services;
using HomeService.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    /// <summary>Mã lỗi trong body 403 khi đăng nhập tài khoản chưa xác thực email – FE mở màn nhập OTP.</summary>
    public const string EmailNotVerifiedCode = "EMAIL_NOT_VERIFIED";

    /// <summary>Mã lỗi trong body 403 khi tài khoản bị khóa – FE hiện lý do và nút "Gửi yêu cầu mở khóa".</summary>
    public const string AccountLockedCode = "ACCOUNT_LOCKED";

    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // US01 - Đăng ký tài khoản: tạo tài khoản chưa kích hoạt + gửi mã OTP về email
    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // US01 - Nhập mã OTP để kích hoạt tài khoản (đúng mã → trả token, đăng nhập luôn)
    [HttpPost("verify-email")]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(VerifyEmailRequest request) =>
        Ok(await _authService.VerifyEmailAsync(request));

    // Gửi lại mã xác thực đăng ký
    [HttpPost("resend-verification")]
    public async Task<ActionResult<OtpSentResponse>> ResendVerification(EmailRequest request) =>
        Ok(await _authService.ResendVerificationAsync(request.Email));

    // US02 - Đăng nhập
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (AccountLockedException ex)
        {
            var u = ex.User;
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Tài khoản đã bị khóa.",
                code = AccountLockedCode,
                email = u.Email,
                lockReason = u.LockReason,
                lockReasonText = u.LockReason is null ? null : AccountLockReasonText.Of(u.LockReason),
                lockDetail = u.LockDetail,
                lockedAt = u.LockedAt,
            });
        }
        catch (EmailNotVerifiedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = ex.Message,
                code = EmailNotVerifiedCode,
                email = ex.Email,
                maskedEmail = OtpService.MaskEmail(ex.Email),
                expiresAt = ex.Otp?.ExpiresAt,
                resendAfterSeconds = ex.Otp?.ResendAfterSeconds ?? 0,
            });
        }
    }

    // Quên mật khẩu – bước 1: gửi mã OTP về email (luôn trả 200, không tiết lộ email có tồn tại hay không)
    [HttpPost("forgot-password")]
    public async Task<ActionResult<OtpSentResponse>> ForgotPassword(EmailRequest request) =>
        Ok(await _authService.ForgotPasswordAsync(request.Email));

    // Quên mật khẩu – bước 2: nhập mã OTP + mật khẩu mới
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        await _authService.ResetPasswordAsync(request);
        return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới." });
    }

    // US03 - Đăng xuất (client-side: xóa token; nếu cần revoke thì bổ sung bảng RefreshToken)
    [HttpPost("logout")]
    public IActionResult Logout() => Ok(new { message = "Đăng xuất thành công." });
}
