using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
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
        var exists = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (exists)
            throw new InvalidOperationException("Email đã được sử dụng.");

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            PhoneNumber = request.PhoneNumber,
            Role = request.Role,
        };

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
