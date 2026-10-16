using System.ComponentModel.DataAnnotations;
using HomeService.Api.Enums;

namespace HomeService.Api.DTOs;

// US04 – body của POST /api/taskers/profile (SRS 9.5)
public class TaskerProfileRequest
{
    [Required(ErrorMessage = "Số CCCD phải gồm 12 chữ số.")]
    [RegularExpression(@"^\d{12}$", ErrorMessage = "Số CCCD phải gồm 12 chữ số.")]
    public string IdCardNumber { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? IdCardImageUrl { get; set; }

    [MaxLength(500, ErrorMessage = "Kỹ năng tối đa 500 ký tự.")]
    public string? Skills { get; set; }

    [Required(ErrorMessage = "Khu vực làm việc không hợp lệ.")]
    public string WorkingArea { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn điểm làm việc trên bản đồ.")]
    [StringLength(255, MinimumLength = 5, ErrorMessage = "Vui lòng chọn điểm làm việc trên bản đồ.")]
    public string BaseAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn điểm làm việc trên bản đồ.")]
    [Range(-90, 90, ErrorMessage = "Vui lòng chọn điểm làm việc trên bản đồ.")]
    public double? BaseLatitude { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn điểm làm việc trên bản đồ.")]
    [Range(-180, 180, ErrorMessage = "Vui lòng chọn điểm làm việc trên bản đồ.")]
    public double? BaseLongitude { get; set; }

    [Required(ErrorMessage = "Bán kính phục vụ không hợp lệ.")]
    public int? ServiceRadiusKm { get; set; }
}

public record TaskerProfileResponse(
    int Id,
    int UserId,
    string IdCardNumber,
    string? IdCardImageUrl,
    string? Skills,
    string? WorkingArea,
    TaskerVerificationStatus VerificationStatus,
    decimal RatingAverage,
    bool IsAvailable,
    string BaseAddress,
    double BaseLatitude,
    double BaseLongitude,
    int ServiceRadiusKm
);

// Phần tử của GET /api/taskers/pending-verification
public record PendingTaskerItem(
    int Id,
    int UserId,
    string FullName,
    string IdCardNumber,
    string? Skills,
    string? WorkingArea,
    // Bổ sung (thêm vào cuối, không phá FE cũ): giúp Admin xem đủ hồ sơ khi duyệt
    string Email,
    string? PhoneNumber,
    string? IdCardImageUrl,
    string BaseAddress,
    double BaseLatitude,
    double BaseLongitude,
    int ServiceRadiusKm,
    DateTime? SubmittedAt
);

// Phần tử của GET /api/taskers/available
public record AvailableTaskerItem(int UserId, string FullName, decimal RatingAverage, string? WorkingArea);
