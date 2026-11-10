using HomeService.Api.Enums;

namespace HomeService.Api.Models;

/// <summary>BR-B05 – hạng mục khách liệt kê khi đặt dịch vụ tính theo hạng mục (vd Sửa điện nước).</summary>
public class BookingItem
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public int ItemNo { get; set; }  // 1..số lượng
    public string Description { get; set; } = string.Empty;
}

/// <summary>US18 – ảnh lúc Bắt đầu / Hoàn thành (nhiều ảnh, theo từng hạng mục nếu dịch vụ yêu cầu). Không sửa/xóa được.</summary>
public class BookingPhoto
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking? Booking { get; set; }
    public BookingPhotoStage Stage { get; set; }
    public int? ItemNo { get; set; }   // null = ảnh chung của đơn
    public string Url { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
