using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// US16/US17 – Tasker xem lời mời nhận đơn đang chờ
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Tasker)]
public class OffersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OffersController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Mặc định chỉ trả lời mời đang chờ (Sent, còn hạn). ?all=true để xem cả lịch sử lời mời.
    /// FE đếm ngược theo expiresAt (giờ server, UTC).
    /// </summary>
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<OfferResponse>>> GetMine([FromQuery] bool all = false)
    {
        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var query = _db.BookingOffers.AsNoTracking().Where(o => o.TaskerId == userId);
        if (!all) query = query.Where(o => o.Status == OfferStatus.Sent && o.ExpiresAt > now);

        var offers = await query
            .OrderByDescending(o => o.SentAt)
            .Take(100)
            .Select(o => new
            {
                o.Id, o.BookingId, ServiceName = o.Booking!.ServiceCategory!.Name, o.Booking.Address,
                o.Booking.ScheduledTime, o.DistanceKm, o.Booking.TotalPrice, o.Booking.PaymentMethod,
                o.Booking.CommissionRate, o.ExpiresAt, o.Status, o.Wave, o.Booking.Note, o.Booking.Quantity,
                o.Booking.CustomerId,
            })
            .ToListAsync();

        var customerIds = offers.Select(o => o.CustomerId).Distinct().ToList();
        var ratings = await _db.Reviews
            .Where(r => customerIds.Contains(r.RevieweeId) && !r.IsFlagged)
            .GroupBy(r => r.RevieweeId)
            .Select(g => new { CustomerId = g.Key, Avg = g.Average(r => (double)r.Rating) })
            .ToDictionaryAsync(x => x.CustomerId, x => (decimal?)Math.Round((decimal)x.Avg, 2));

        return Ok(offers.Select(o => new OfferResponse(
            o.Id, o.BookingId, o.ServiceName, o.Address, o.ScheduledTime, o.DistanceKm, o.TotalPrice,
            o.PaymentMethod, Math.Round(o.TotalPrice * o.CommissionRate, 0, MidpointRounding.AwayFromZero),
            o.ExpiresAt, o.Status, o.Wave, o.Note, o.Quantity, ratings.GetValueOrDefault(o.CustomerId))));
    }
}
