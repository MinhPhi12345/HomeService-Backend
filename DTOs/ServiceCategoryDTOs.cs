using System.ComponentModel.DataAnnotations;

namespace HomeService.Api.DTOs;

// US07/US08/US11 – PUT phải gửi ĐẦY ĐỦ field vì BE ghi đè toàn bộ (SRS 9.3)
public record UpsertServiceCategoryRequest(
    [Required(ErrorMessage = "Tên dịch vụ phải từ 2 đến 100 ký tự.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên dịch vụ phải từ 2 đến 100 ký tự.")]
    string Name,

    [MaxLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    string? Description,

    [Range(1, 100_000_000, ErrorMessage = "Giá phải lớn hơn 0.")]
    decimal BasePrice,

    [Required(ErrorMessage = "Đơn vị tính không hợp lệ.")]
    [RegularExpression("^(giờ|lần|m²|buổi)$", ErrorMessage = "Đơn vị tính không hợp lệ.")]
    string Unit,

    [MaxLength(500, ErrorMessage = "Link icon tối đa 500 ký tự.")]
    string? IconUrl
);

public record ServiceCategoryResponse(int Id, string Name, string? Description, decimal BasePrice, string Unit, string? IconUrl, bool IsActive);
