namespace HomeService.Api.Models;

// EP02 - Quản lý dịch vụ (US07-US11)
public class ServiceCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // vd: Dọn dẹp nhà, Sửa điện nước
    public string? Description { get; set; }
    public decimal BasePrice { get; set; } // US11: bảng giá
    public string Unit { get; set; } = "giờ"; // đơn vị tính giá
    public string? IconUrl { get; set; }
    public bool IsActive { get; set; } = true;

    // SRS v1.8 – số lượng theo từng dịch vụ (BR-B01) & thời lượng ước tính để xếp lịch Tasker (BR-B02)
    public int MinQuantity { get; set; } = 1;
    public int MaxQuantity { get; set; } = 4;
    public string? QuantityLabel { get; set; }  // vd "Số giờ làm", "Số hạng mục cần sửa"
    public string? QuantityHint { get; set; }   // vd "1 mẻ ≈ 7 kg ≈ 15–20 bộ quần áo"
    public int MinutesPerUnit { get; set; } = 60;
    /// <summary>Khách phải liệt kê từng hạng mục khi đặt đơn; Tasker chụp ảnh theo từng hạng mục (BR-B05).</summary>
    public bool RequiresItemList { get; set; }

    // SRS v1.8 – chi tiết dịch vụ (US10): mỗi dòng là một ý
    public string? Includes { get; set; }
    public string? Excludes { get; set; }
    public string? CustomerPrepares { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
