using HomeService.Api.DTOs;
using HomeService.Api.Models;

namespace HomeService.Api.Services.Interfaces;

public interface IBookingService
{
    /// <summary>US12/US13 – khách hàng tạo đơn.</summary>
    Task<Booking> CreateAsync(int customerId, CreateBookingRequest request);

    /// <summary>Kiểm tra thời gian đặt hợp lệ: ≥ hiện tại + 1 giờ và ≤ 30 ngày (SRS 6.1).</summary>
    DateTime ValidateScheduledTime(DateTime scheduledTime);
}
