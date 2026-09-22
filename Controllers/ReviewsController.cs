using System.Security.Claims;
using HomeService.Api.Data;
using HomeService.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP06 - Đánh giá & Nhận xét (US25-US27)
public record CreateReviewRequest(int BookingId, int RevieweeId, int Rating, string? Comment);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReviewsController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // US25 - Khách hàng đánh giá Tasker / US26 - Tasker đánh giá khách hàng
    [HttpPost]
    public async Task<IActionResult> Create(CreateReviewRequest request)
    {
        var review = new Review
        {
            BookingId = request.BookingId,
            ReviewerId = CurrentUserId,
            RevieweeId = request.RevieweeId,
            Rating = request.Rating,
            Comment = request.Comment,
        };
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetForBooking), new { bookingId = request.BookingId }, review);
    }

    [HttpGet("booking/{bookingId:int}")]
    public async Task<IActionResult> GetForBooking(int bookingId)
    {
        var reviews = await _db.Reviews.Where(r => r.BookingId == bookingId).ToListAsync();
        return Ok(reviews);
    }

    // US27 - Admin xem & xử lý các đánh giá bị gắn cờ khiếu nại
    [HttpGet("flagged")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetFlagged()
    {
        var flagged = await _db.Reviews.Where(r => r.IsFlagged).ToListAsync();
        return Ok(flagged);
    }

    [HttpPut("{id:int}/flag")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ResolveFlag(int id, [FromQuery] bool clear = true)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review is null) return NotFound();

        review.IsFlagged = !clear;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
