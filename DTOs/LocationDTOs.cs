using System.ComponentModel.DataAnnotations;

namespace HomeService.Api.DTOs;

// US19 – PUT /api/bookings/{id}/location (SRS 9.4)
public class LocationRequest
{
    [Required(ErrorMessage = "Vị trí không hợp lệ.")]
    [Range(-90, 90, ErrorMessage = "Vị trí không hợp lệ.")]
    public double? Latitude { get; set; }

    [Required(ErrorMessage = "Vị trí không hợp lệ.")]
    [Range(-180, 180, ErrorMessage = "Vị trí không hợp lệ.")]
    public double? Longitude { get; set; }
}

public record LocationResponse(
    double Latitude,
    double Longitude,
    DateTime UpdatedAt,
    // Bổ sung: khoảng cách đường chim bay từ Tasker tới địa chỉ đơn (km)
    double? DistanceKm
);

// US33 – POST /api/bookings/{id}/retry (body tùy chọn)
public class RetryBookingRequest
{
    /// <summary>Giờ mới nếu khách muốn đổi; bỏ trống → giữ giờ cũ.</summary>
    public DateTime? ScheduledTime { get; set; }
}
