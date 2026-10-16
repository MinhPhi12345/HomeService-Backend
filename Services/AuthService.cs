using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

// EP01 - US01 (đăng ký), US02 (đăng nhập)
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;

    public AuthService(AppDbContext db, ITokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // FR-01: không cho đăng ký Admin; chỉ nhận 0 (Customer) hoặc 1 (Tasker)
        if (request.Role != UserRole.Customer && request.Role != UserRole.Tasker)
            throw ApiException.BadRequest("Vai trò không hợp lệ.");

        var email = NormalizeEmail(request.Email);   // BR-A01: email duy nhất, không phân biệt hoa thường
        var exists = await _db.Users.AnyAsync(u => u.Email == email);
        if (exists)
            throw new InvalidOperationException("Email đã được sử dụng.");

        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), // BR-A02
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Role = request.Role,
        };

        // FR-01 bước 5: Tasker được tạo sẵn hồ sơ ở trạng thái Pending (chờ gửi hồ sơ & duyệt – US04)
        if (user.Role == UserRole.Tasker)
            user.TaskerProfile = new TaskerProfile { VerificationStatus = TaskerVerificationStatus.Pending };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (token, expiresAt) = _tokenService.GenerateAccessToken(user);
        return new AuthResponse(user.Id, user.FullName, user.Email, user.Role, token, expiresAt);
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

        var (token, expiresAt) = _tokenService.GenerateAccessToken(user);
        return new AuthResponse(user.Id, user.FullName, user.Email, user.Role, token, expiresAt);
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
