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

    public BookingsController(AppDbContext db, IBookingService bookingService, OfferService offers)
    {
        _db = db;
        _bookingService = bookingService;
        _offers = offers;
    }

    private int CurrentUserId => User.GetUserId();

    // US12/US13 - Khách hàng tạo đơn đặt dịch vụ (chọn thời gian, địa chỉ có tọa độ, phương thức thanh toán)
    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request)
    {
        var booking = await _bookingService.CreateAsync(CurrentUserId, request);
        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, await ToResponse(booking.Id));
    }

    // US15 - Khách hàng xem lịch sử đặt dịch vụ / US20 - Tasker xem đơn đã nhận (BE tự lọc theo vai trò trong token)
    [HttpGet("my")]
    [Authorize(Roles = "Customer,Tasker")]
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
    [Authorize(Roles = "Tasker")]
    public async Task<IActionResult> Accept(int id)
    {
        await _offers.AcceptAsync(id, CurrentUserId);
        return NoContent();
    }

    // US17 - Tasker từ chối lời mời (chỉ đóng lời mời của Tasker này, đơn vẫn chờ Tasker khác)
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Tasker")]
    public async Task<IActionResult> Reject(int id)
    {
        await _offers.RejectAsync(id, CurrentUserId);
        return NoContent();
    }

    // US34 - Khách xác nhận hoàn thành → giải ngân cho Tasker sau chiết khấu
    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Confirm(int id)
    {
        await _bookingService.ConfirmAsync(id, CurrentUserId);
        return NoContent();
    }

    // US14 - Khách hàng hủy đơn
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Customer")]
    public async Task<IActionResult> Cancel(int id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();
        if (booking.CustomerId != CurrentUserId) return Forbid();

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US18 - Tasker cập nhật trạng thái thực hiện đơn kèm ảnh (3 Bắt đầu, 4 Hoàn thành)
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Tasker")]
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
