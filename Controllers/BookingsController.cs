using System.Security.Claims;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
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

    public BookingsController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // US12/US13 - Khách hàng tạo đơn đặt dịch vụ
    [HttpPost]
    [Authorize(Roles = "Customer")]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request)
    {
        var service = await _db.ServiceCategories.FindAsync(request.ServiceCategoryId);
        if (service is null || !service.IsActive) return BadRequest(new { message = "Dịch vụ không tồn tại." });

        var booking = new Booking
        {
            CustomerId = CurrentUserId,
            ServiceCategoryId = request.ServiceCategoryId,
            ScheduledTime = request.ScheduledTime,
            Address = request.Address,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Note = request.Note,
            TotalPrice = service.BasePrice, // TODO: tính theo số lượng/hệ số khi mở rộng
            Status = BookingStatus.Pending,
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        // TODO (US16): gọi MatchingService để tự động ghép nối Tasker phù hợp theo khu vực/lịch rảnh/đánh giá

        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, await ToResponse(booking.Id));
    }

    // US15 - Khách hàng xem lịch sử đặt dịch vụ / US20 - Tasker xem đơn đã nhận
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<BookingResponse>>> GetMine()
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var query = _db.Bookings.AsQueryable();
        query = role == "Tasker"
            ? query.Where(b => b.TaskerId == CurrentUserId)
            : query.Where(b => b.CustomerId == CurrentUserId);

        var bookings = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
        var result = new List<BookingResponse>();
        foreach (var b in bookings) result.Add(await ToResponse(b.Id));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingResponse>> GetById(int id)
    {
        var exists = await _db.Bookings.AnyAsync(b => b.Id == id);
        if (!exists) return NotFound();
        return Ok(await ToResponse(id));
    }

    // US17 - Tasker nhận / từ chối đơn
    [HttpPost("{id:int}/accept")]
    [Authorize(Roles = "Tasker")]
    public async Task<IActionResult> Accept(int id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        booking.TaskerId = CurrentUserId;
        booking.Status = BookingStatus.Accepted;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = "Tasker")]
    public async Task<IActionResult> Reject(int id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        booking.Status = BookingStatus.Rejected;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
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

    // US18 - Tasker cập nhật trạng thái thực hiện đơn
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Tasker")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateBookingStatusRequest request)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();

        booking.Status = request.Status;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // TODO (US29): tạo Notification cho khách hàng khi trạng thái thay đổi
        return NoContent();
    }

    private async Task<BookingResponse> ToResponse(int id)
    {
        var b = await _db.Bookings
            .Include(x => x.Customer)
            .Include(x => x.Tasker)
            .Include(x => x.ServiceCategory)
            .FirstAsync(x => x.Id == id);

        return new BookingResponse(
            b.Id,
            b.ServiceCategory!.Name,
            b.CustomerId,
            b.Customer!.FullName,
            b.TaskerId,
            b.Tasker?.FullName,
            b.ScheduledTime,
            b.Address,
            b.TotalPrice,
            b.Status,
            b.CreatedAt
        );
    }
}
