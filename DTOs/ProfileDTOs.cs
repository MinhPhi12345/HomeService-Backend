using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US05 – SRS 9.6. PUT gửi đầy đủ field; không sửa được email & role qua API này.
public record UpdateProfileRequest(
    [Required(ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 100 ký tự.")]
    string FullName,

    [RegularExpression(@"^0\d{9}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    string? PhoneNumber,

    [MaxLength(255, ErrorMessage = "Địa chỉ tối đa 255 ký tự.")]
    string? Address,

    [MaxLength(500, ErrorMessage = "Link ảnh tối đa 500 ký tự.")]
    string? AvatarUrl
);

public record ProfileResponse(
    int Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    string? Address,
    string? AvatarUrl,
    UserRole Role,
    // Bổ sung: khoản đang nợ & quyền thanh toán tiền mặt (FR-27) để FE hiện nhắc trả nợ
    decimal OutstandingAmount,
    bool IsCashBlocked,
    DateTime CreatedAt
);
