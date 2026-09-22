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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
