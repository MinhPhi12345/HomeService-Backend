using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>Đăng nhập đúng mật khẩu nhưng email chưa xác thực OTP → controller trả 403 kèm mã lỗi để FE mở màn nhập mã.</summary>
public class EmailNotVerifiedException : Exception
{
    public string Email { get; }
    public OtpSentResponse? Otp { get; }

    public EmailNotVerifiedException(string email, OtpSentResponse? otp)
        : base($"Tài khoản chưa xác thực email. Vui lòng nhập mã OTP đã gửi tới {OtpService.MaskEmail(email)}.")
    {
        Email = email;
        Otp = otp;
    }
}

// EP01 - US01 (đăng ký + xác thực email bằng OTP), US02 (đăng nhập), quên mật khẩu bằng OTP
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly OtpService _otp;

    public AuthService(AppDbContext db, ITokenService tokenService, OtpService otp)
    {
        _db = db;
        _tokenService = tokenService;
        _otp = otp;
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        // FR-01: không cho đăng ký Admin; chỉ nhận 0 (Customer) hoặc 1 (Tasker)
        if (request.Role != UserRole.Customer && request.Role != UserRole.Tasker)
            throw ApiException.BadRequest("Vai trò không hợp lệ.");

        var email = NormalizeEmail(request.Email);   // BR-A01: email duy nhất, không phân biệt hoa thường
        var user = await _db.Users.Include(u => u.TaskerProfile).FirstOrDefaultAsync(u => u.Email == email);
        if (user is { EmailVerified: true })
            throw new InvalidOperationException("Email đã được sử dụng.");

        if (user is null)
        {
            user = new User { Email = email };
            _db.Users.Add(user);
        }
        // Email đã đăng ký nhưng chưa xác thực (mất mã, gõ nhầm thông tin…): cho đăng ký lại, ghi đè thông tin cũ.
        // Người khác không chiếm được vì vẫn phải nhập mã OTP gửi tới đúng hộp thư này.
        user.FullName = request.FullName.Trim();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password); // BR-A02
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.Role = request.Role;
        user.EmailVerified = false;

        // FR-01 bước 5: Tasker được tạo sẵn hồ sơ ở trạng thái Pending (chờ gửi hồ sơ & duyệt – US04)
        if (user.Role == UserRole.Tasker && user.TaskerProfile is null)
            user.TaskerProfile = new TaskerProfile { VerificationStatus = TaskerVerificationStatus.Pending };
        else if (user.Role != UserRole.Tasker && user.TaskerProfile is not null)
            _db.TaskerProfiles.Remove(user.TaskerProfile);

        await _db.SaveChangesAsync();

        var sent = await _otp.SendAsync(user, OtpPurpose.VerifyEmail);
        return new RegisterResponse(user.Email, sent.MaskedEmail, sent.ExpiresAt, sent.ResendAfterSeconds,
            $"Đã gửi mã OTP tới {sent.MaskedEmail}. Nhập mã để kích hoạt tài khoản.");
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email)
            ?? throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");

        // Không nói rõ sai email hay sai mật khẩu để tránh dò tài khoản (FR-02)
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("Tài khoản đã bị khóa.");

        if (!user.EmailVerified)
        {
            // Tự gửi mã mới (nếu đã qua thời gian chờ) để người dùng nhập luôn
            OtpSentResponse? sent = null;
            try { sent = await _otp.SendAsync(user, OtpPurpose.VerifyEmail); }
            catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status429TooManyRequests) { }
            throw new EmailNotVerifiedException(user.Email, sent);
        }

        var (token, expiresAt) = _tokenService.GenerateAccessToken(user);
        return new AuthResponse(user.Id, user.FullName, user.Email, user.Role, token, expiresAt);
    }

    /// <summary>Nhập đúng mã OTP → kích hoạt tài khoản và đăng nhập luôn.</summary>
    public async Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email)
            ?? throw ApiException.BadRequest("Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng bấm \"Gửi lại mã\".");
        if (user.EmailVerified)
            throw ApiException.BadRequest("Email đã được xác thực. Vui lòng đăng nhập.");
        if (!user.IsActive)
            throw ApiException.BadRequest("Tài khoản đã bị khóa.");

        await _otp.VerifyAsync(user.Id, OtpPurpose.VerifyEmail, request.Code);
        user.EmailVerified = true;
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _tokenService.GenerateAccessToken(user);
        return new AuthResponse(user.Id, user.FullName, user.Email, user.Role, token, expiresAt);
    }

    public async Task<OtpSentResponse> ResendVerificationAsync(string email)
    {
        email = NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive) return Generic(email);
        if (user.EmailVerified)
            throw ApiException.BadRequest("Email đã được xác thực. Vui lòng đăng nhập.");
        return await _otp.SendAsync(user, OtpPurpose.VerifyEmail);
    }

    /// <summary>
    /// Quên mật khẩu: luôn trả cùng một kiểu kết quả dù email có tồn tại hay không (tránh dò tài khoản);
    /// chỉ tài khoản đang hoạt động mới thật sự nhận được mã.
    /// </summary>
    public async Task<OtpSentResponse> ForgotPasswordAsync(string email)
    {
        email = NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !user.IsActive) return Generic(email);
        return await _otp.SendAsync(user, OtpPurpose.ResetPassword);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive)
            ?? throw ApiException.BadRequest("Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng bấm \"Gửi lại mã\".");

        await _otp.VerifyAsync(user.Id, OtpPurpose.ResetPassword, request.Code);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.EmailVerified = true;   // nhận được mã qua email = đã chứng minh sở hữu email
        await _db.SaveChangesAsync();
    }

    private OtpSentResponse Generic(string email) =>
        new(OtpService.MaskEmail(email), DateTime.UtcNow.AddMinutes(_otp.ExpiryMinutes), _otp.ResendSeconds);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
