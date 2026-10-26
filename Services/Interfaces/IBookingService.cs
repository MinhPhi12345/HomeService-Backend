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

    /// <summary>US33 – thử lại ghép nối (có thể đổi giờ) khi chưa có Tasker nhận.</summary>
    Task RetryAsync(int bookingId, int customerId, RetryBookingRequest? request);

    /// <summary>US36 – Tasker báo khách chưa thanh toán tiền mặt.</summary>
    Task ReportUnpaidAsync(int bookingId, int taskerId, ReportUnpaidRequest request);
}
