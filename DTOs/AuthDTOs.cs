using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US01
public record RegisterRequest(string FullName, string Email, string Password, string? PhoneNumber, UserRole Role);

// US02
public record LoginRequest(
    [Required(ErrorMessage = "Email không hợp lệ.")] string Email,
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")] string Password
);

public record AuthResponse(int UserId, string FullName, string Email, UserRole Role, string AccessToken, DateTime ExpiresAt);
