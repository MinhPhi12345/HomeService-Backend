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
    DateTime CreatedAt,
    // US30 – tài khoản đang chờ khóa (chỉ hoàn thành đơn đang dở, không nhận / đặt đơn mới)
    DateTime? LockPendingAt = null
);

/// <summary>Đổi mật khẩu khi đã đăng nhập: cần mật khẩu hiện tại + mã OTP gửi về email.</summary>
public record ChangePasswordRequest(
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")] string CurrentPassword,
    [Required(ErrorMessage = "Vui lòng nhập mã OTP.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP gồm 6 chữ số.")]
    string Code,
    [Required(ErrorMessage = "Mật khẩu phải từ 8 ký tự, gồm chữ và số.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,50}$", ErrorMessage = "Mật khẩu phải từ 8 ký tự, gồm chữ và số.")]
    string NewPassword
);
