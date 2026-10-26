using HomeService.Api.DTOs;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

/// <summary>
/// Bản đồ & địa chỉ miễn phí (OpenStreetMap + Nominatim) đi qua Backend.
/// App/Web dùng: TileLayer urlTemplate = "{baseUrl}/geo/tiles/{z}/{x}/{y}.png".
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GeoController : ControllerBase
{
    private readonly GeoService _geo;

    public GeoController(GeoService geo) => _geo = geo;

    // Ảnh nền bản đồ – không cần token vì thư viện bản đồ tự tải ảnh
    [HttpGet("tiles/{z:int}/{x:int}/{y:int}.png")]
    [AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public async Task<IActionResult> Tile(int z, int x, int y, CancellationToken ct)
    {
        var bytes = await _geo.GetTileAsync(z, x, y, ct);
        return bytes is null ? NotFound() : File(bytes, "image/png");
    }

    // Kiểm tra nhanh Backend có ra được OpenStreetMap không (mở trên trình duyệt để chẩn đoán bản đồ trắng)
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var tiles = await _geo.GetTileAsync(0, 0, 0, ct) is not null;
        var geocoder = (await _geo.SearchAsync("Hồ Chí Minh", ct)).Count > 0;
        return Ok(new
        {
            tiles,
            geocoder,
            error = tiles && geocoder ? null : GeoService.LastError,
            message = tiles && geocoder
                ? "Backend tải được bản đồ và tra được địa chỉ."
                : "Backend chưa kết nối được OpenStreetMap – kiểm tra Internet/tường lửa của máy chạy Backend.",
        });
    }

    // Toạ độ => địa chỉ gọn (để tự điền ô địa chỉ khi khách ghim vị trí)
    [HttpGet("reverse")]
    [Authorize]
    public async Task<ActionResult<GeoAddressResponse>> Reverse([FromQuery] double lat, [FromQuery] double lng,
        CancellationToken ct)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180) return BadRequest(new { message = "Vị trí không hợp lệ." });
        return Ok(new GeoAddressResponse(await _geo.ReverseAsync(lat, lng, ct)));
    }

    // Địa chỉ => danh sách vị trí ở Việt Nam (tìm khi người dùng bấm tìm, không gọi theo từng ký tự)
    [HttpGet("search")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<GeoPlaceItem>>> Search([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
            return BadRequest(new { message = "Nhập ít nhất 3 ký tự để tìm địa chỉ." });
        if (q.Length > 200) return BadRequest(new { message = "Địa chỉ tìm kiếm quá dài." });
        return Ok(await _geo.SearchAsync(q, ct));
    }
}
