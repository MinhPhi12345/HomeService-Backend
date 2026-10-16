using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP03 + EP04 - Đặt dịch vụ, ghép nối & theo dõi (US12-US20)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBookingService _bookingService;
    private readonly OfferService _offers;
    private readonly CancellationService _cancellation;

    public BookingsController(AppDbContext db, IBookingService bookingService, OfferService offers,
        CancellationService cancellation)
    {
        _db = db;
        _bookingService = bookingService;
        _offers = offers;
        _cancellation = cancellation;
    }

    private int CurrentUserId => User.GetUserId();

    // US12/US13 - Khách hàng tạo đơn đặt dịch vụ (chọn thời gian, địa chỉ có tọa độ, phương thức thanh toán)
    [HttpPost]
    [Authorize(Roles = Roles.Customer)]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request)
    {
        var booking = await _bookingService.CreateAsync(CurrentUserId, request);
        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, await ToResponse(booking.Id));
    }

    // US15 - Khách hàng xem lịch sử đặt dịch vụ / US20 - Tasker xem đơn đã nhận (BE tự lọc theo vai trò trong token)
    [HttpGet("my")]
    [Authorize(Roles = Roles.CustomerOrTasker)]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> GetMine()
    {
        var userId = CurrentUserId;
        var query = BookingsWithDetails();
        query = User.IsInRole(UserRole.Tasker)
            ? query.Where(b => b.TaskerId == userId)
            : query.Where(b => b.CustomerId == userId);

        var bookings = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
        var now = DateTime.UtcNow;
        return Ok(bookings.Select(b => BookingMapper.ToResponse(b, now)));
    }

    // Chi tiết đơn – chỉ Khách hàng của đơn, Tasker của đơn hoặc Admin được xem (FR-11)
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingResponse>> GetById(int id)
    {
        var booking = await BookingsWithDetails().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();

        var userId = CurrentUserId;
        var allowed = User.IsInRole(UserRole.Admin) || booking.CustomerId == userId || booking.TaskerId == userId;
        if (!allowed) return Forbid();

        return Ok(BookingMapper.ToResponse(booking, DateTime.UtcNow));
    }

    // US17 - Tasker nhận lời mời – người nhận đầu tiên thắng (409 nếu đơn đã có Tasker khác nhận / lời mời hết hạn)
    [HttpPost("{id:int}/accept")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<IActionResult> Accept(int id)
    {
        await _offers.AcceptAsync(id, CurrentUserId);
        return NoContent();
    }

    // US17 - Tasker từ chối lời mời (chỉ đóng lời mời của Tasker này, đơn vẫn chờ Tasker khác)
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<IActionResult> Reject(int id)
    {
        await _offers.RejectAsync(id, CurrentUserId);
        return NoContent();
    }

    // US34 - Khách xác nhận hoàn thành → giải ngân cho Tasker sau chiết khấu
    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = Roles.Customer)]
    public async Task<IActionResult> Confirm(int id)
    {
        await _bookingService.ConfirmAsync(id, CurrentUserId);
        return NoContent();
    }

    // US14 / US33 - Khách hàng hủy đơn (trạng thái 10, 0, 1, 2, 8 – BR-B04); đơn online được hoàn tiền
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = Roles.Customer)]
    public async Task<IActionResult> Cancel(int id)
    {
        var booking = await _db.Bookings.Include(b => b.Payment).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();
        if (booking.CustomerId != CurrentUserId) return Forbid();
        if (!CancellationService.CancellableStatuses.Contains(booking.Status))
            throw ApiException.BadRequest("Không thể hủy đơn ở trạng thái hiện tại.");

        await _cancellation.CancelAsync(booking, "Khách hàng đã hủy đơn.");
        return NoContent();
    }

    // US33 - Thử lại ghép nối (body tùy chọn { "scheduledTime": "..." } để đổi giờ)
    [HttpPost("{id:int}/retry")]
    [Authorize(Roles = Roles.Customer)]
    public async Task<IActionResult> Retry(int id, [FromBody] RetryBookingRequest? request = null)
    {
        await _bookingService.RetryAsync(id, CurrentUserId, request);
        return NoContent();
    }

    // US19 - Tasker gửi vị trí (polling ~15 giây) khi đơn đang Accepted/InProgress
    [HttpPut("{id:int}/location")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<IActionResult> UpdateLocation(int id, LocationRequest request)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();
        if (booking.TaskerId != CurrentUserId) return Forbid();
        if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.InProgress)
            throw ApiException.BadRequest("Chỉ gửi vị trí khi đơn đang được thực hiện.");

        var profile = await _db.TaskerProfiles.FirstAsync(t => t.UserId == CurrentUserId);
        profile.LastLatitude = request.Latitude;
        profile.LastLongitude = request.Longitude;
        profile.LastLocationAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US19 - Khách hàng xem vị trí Tasker (polling ~10 giây)
    [HttpGet("{id:int}/location")]
    [Authorize(Roles = Roles.CustomerOrAdmin)]
    public async Task<ActionResult<LocationResponse>> GetLocation(int id)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null) return NotFound();
        if (booking.CustomerId != CurrentUserId && !User.IsInRole(UserRole.Admin)) return Forbid();
        if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.InProgress)
            throw ApiException.BadRequest("Chỉ xem vị trí khi đơn đang được thực hiện.");

        var p = await _db.TaskerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == booking.TaskerId);
        if (p?.LastLatitude is null || p.LastLongitude is null || p.LastLocationAt is null) return NotFound();

        double? distance = booking.Latitude is null || booking.Longitude is null
            ? null
            : Math.Round(GeoHelper.DistanceKm(p.LastLatitude.Value, p.LastLongitude.Value, booking.Latitude.Value, booking.Longitude.Value), 2);
        return Ok(new LocationResponse(p.LastLatitude.Value, p.LastLongitude.Value, p.LastLocationAt.Value, distance));
    }

    // US18 - Tasker cập nhật trạng thái thực hiện đơn kèm ảnh (3 Bắt đầu, 4 Hoàn thành)
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<IActionResult> UpdateStatus(int id, UpdateBookingStatusRequest request)
    {
        await _bookingService.UpdateStatusAsync(id, CurrentUserId, request);
        return NoContent();
    }

    private IQueryable<Booking> BookingsWithDetails() => _db.Bookings
        .AsNoTracking()
        .Include(x => x.Customer)
        .Include(x => x.Tasker)
        .Include(x => x.ServiceCategory)
        .Include(x => x.Payment);

    private async Task<BookingResponse> ToResponse(int id)
    {
        var b = await BookingsWithDetails().FirstAsync(x => x.Id == id);
        return BookingMapper.ToResponse(b, DateTime.UtcNow);
    }
}
