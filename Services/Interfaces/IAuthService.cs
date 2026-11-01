using HomeService.Api.DTOs;

namespace HomeService.Api.Services.Interfaces;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request);
    Task<OtpSentResponse> ResendVerificationAsync(string email);
    Task<OtpSentResponse> ForgotPasswordAsync(string email);
    Task ResetPasswordAsync(ResetPasswordRequest request);
}
