using HomeService.Api.Data;
using HomeService.Api.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP08 - Quản trị & Báo cáo (US31-US32)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    // US31 - Báo cáo doanh thu theo khoảng thời gian
    [HttpGet("revenue")]
    public async Task<IActionResult> GetRevenue([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var payments = await _db.Payments
            .Where(p => p.Status == PaymentStatus.Success && p.PaidAt >= from && p.PaidAt <= to)
            .ToListAsync();

        return Ok(new
        {
            From = from,
            To = to,
            TotalRevenue = payments.Sum(p => p.Amount),
            TotalCommission = payments.Sum(p => p.CommissionAmount),
            TransactionCount = payments.Count
        });
    }

    // US32 - Báo cáo số lượng đơn hoàn thành/hủy & hiệu suất Tasker
    [HttpGet("bookings-summary")]
    public async Task<IActionResult> GetBookingsSummary([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var bookings = await _db.Bookings
            .Where(b => b.CreatedAt >= from && b.CreatedAt <= to)
            .ToListAsync();

        var summary = bookings
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() });

        return Ok(summary);
    }

    [HttpGet("tasker-performance")]
    public async Task<IActionResult> GetTaskerPerformance()
    {
        var performance = await _db.Bookings
            .Where(b => b.TaskerId != null)
            .GroupBy(b => b.TaskerId)
            .Select(g => new
            {
                TaskerId = g.Key,
                CompletedCount = g.Count(b => b.Status == BookingStatus.Paid || b.Status == BookingStatus.Completed),
                CancelledCount = g.Count(b => b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Rejected),
            })
            .ToListAsync();

        return Ok(performance);
    }
}
