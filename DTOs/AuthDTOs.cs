using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US01 – quy tắc kiểm tra dữ liệu theo SRS mục 6.1 (FE và BE dùng chung)
public record RegisterRequest(
    [Required(ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
    string FullName,

    [Required(ErrorMessage = "Email không hợp lệ.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(256, ErrorMessage = "Email không hợp lệ.")]
    string Email,

    [Required(ErrorMessage = "Mật khẩu phải từ 8 ký tự, gồm chữ và số.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,50}$", ErrorMessage = "Mật khẩu phải từ 8 ký tự, gồm chữ và số.")]
    string Password,

    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    string? PhoneNumber,

    UserRole Role
);

// US02
public record LoginRequest(
    [Required(ErrorMessage = "Email không hợp lệ.")] string Email,
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")] string Password
);

public record AuthResponse(int UserId, string FullName, string Email, UserRole Role, string AccessToken, DateTime ExpiresAt);
