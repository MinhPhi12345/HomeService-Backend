namespace HomeService.Api.DTOs;

// GET /api/geo/reverse – địa chỉ gọn của một toạ độ (null nếu không tra được)
public record GeoAddressResponse(string? Address);

// GET /api/geo/search – một kết quả tìm địa chỉ
public record GeoPlaceItem(double Latitude, double Longitude, string Address, string? Title);
