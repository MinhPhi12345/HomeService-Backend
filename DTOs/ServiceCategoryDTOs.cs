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
    [RegularExpression("^(giờ|lần|m²|buổi|hạng mục|mẻ)$", ErrorMessage = "Đơn vị tính không hợp lệ.")]
    string Unit,

    [MaxLength(500, ErrorMessage = "Link icon tối đa 500 ký tự.")]
    string? IconUrl,

    // SRS v1.8 – số lượng theo dịch vụ & thời lượng (BR-B01, BR-B02). Bỏ trống khi sửa = giữ giá trị cũ.
    [Range(1, 50, ErrorMessage = "Số lượng tối thiểu từ 1 đến 50.")]
    int? MinQuantity = null,

    [Range(1, 50, ErrorMessage = "Số lượng tối đa từ 1 đến 50.")]
    int? MaxQuantity = null,

    [MaxLength(50, ErrorMessage = "Nhãn số lượng tối đa 50 ký tự.")]
    string? QuantityLabel = null,

    [MaxLength(200, ErrorMessage = "Gợi ý số lượng tối đa 200 ký tự.")]
    string? QuantityHint = null,

    [Range(10, 720, ErrorMessage = "Thời lượng mỗi đơn vị từ 10 đến 720 phút.")]
    int? MinutesPerUnit = null,

    /// <summary>BR-B05: khách liệt kê hạng mục khi đặt, Tasker chụp ảnh theo từng hạng mục.</summary>
    bool? RequiresItemList = null,

    // SRS v1.8 – chi tiết dịch vụ (US10), mỗi dòng một ý
    [MaxLength(1000, ErrorMessage = "Mỗi mục chi tiết tối đa 1000 ký tự.")] string? Includes = null,
    [MaxLength(1000, ErrorMessage = "Mỗi mục chi tiết tối đa 1000 ký tự.")] string? Excludes = null,
    [MaxLength(1000, ErrorMessage = "Mỗi mục chi tiết tối đa 1000 ký tự.")] string? CustomerPrepares = null,
    [MaxLength(1000, ErrorMessage = "Mỗi mục chi tiết tối đa 1000 ký tự.")] string? Notes = null
);

public record ServiceCategoryResponse(int Id, string Name, string? Description, decimal BasePrice, string Unit, string? IconUrl, bool IsActive,
    int MinQuantity = 1, int MaxQuantity = 1, string? QuantityLabel = null, string? QuantityHint = null,
    int MinutesPerUnit = 60, bool RequiresItemList = false,
    string? Includes = null, string? Excludes = null, string? CustomerPrepares = null, string? Notes = null);
