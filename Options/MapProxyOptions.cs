namespace HomeService.Api.Options;

// appsettings.json → "Map". Backend làm "trạm trung chuyển" bản đồ & địa chỉ cho app:
// gọi OpenStreetMap / Nominatim với User-Agent riêng và lưu cache theo đúng chính sách sử dụng,
// nên app không cần API key và không bị chặn khi gọi trực tiếp từ điện thoại.
public class MapProxyOptions
{
    public const string Section = "Map";

    /// <summary>Nguồn ảnh nền bản đồ; {z}/{x}/{y} được thay khi gọi.</summary>
    public string TileUrlTemplate { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>Máy chủ Nominatim để đổi toạ độ ⇄ địa chỉ.</summary>
    public string GeocoderBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>OSM yêu cầu User-Agent nhận diện ứng dụng (nên kèm email liên hệ của nhóm).</summary>
    public string UserAgent { get; set; } = "HomeService-Backend/1.0 (do an sinh vien Nhom 15)";

    /// <summary>Số ngày giữ ảnh bản đồ đã tải trong thư mục cache.</summary>
    public int TileCacheDays { get; set; } = 14;

    /// <summary>Thư mục cache ảnh bản đồ (để trống = &lt;thư mục app&gt;/tile-cache).</summary>
    public string? TileCacheFolder { get; set; }
}
