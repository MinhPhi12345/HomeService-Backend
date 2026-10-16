using HomeService.Api.DTOs;
using HomeService.Api.Models;

namespace HomeService.Api.Services.Interfaces;

public interface IBookingService
{
    /// <summary>US12/US13 – khách hàng tạo đơn.</summary>
    Task<Booking> CreateAsync(int customerId, CreateBookingRequest request);

    /// <summary>Kiểm tra thời gian đặt hợp lệ: ≥ hiện tại + 1 giờ và ≤ 30 ngày (SRS 6.1).</summary>
    DateTime ValidateScheduledTime(DateTime scheduledTime);

    /// <summary>US18 – Tasker cập nhật trạng thái thực hiện kèm ảnh.</summary>
    Task UpdateStatusAsync(int bookingId, int taskerId, UpdateBookingStatusRequest request);

    /// <summary>US34 – khách xác nhận hoàn thành → giải ngân.</summary>
    Task ConfirmAsync(int bookingId, int customerId);
}
