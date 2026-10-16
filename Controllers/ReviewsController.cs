using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP06 - Đánh giá & Nhận xét (US25-US27)
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

    // US25 - Khách hàng đánh giá Tasker sau khi đơn hoàn thành (1 lần / đơn – BR-R01)
    [HttpPost]
    public async Task<ActionResult<ReviewResponse>> Create(CreateReviewRequest request)
    {
        var userId = User.GetUserId();
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == request.BookingId);
        if (booking is null) throw ApiException.BadRequest("Đơn không tồn tại.");

        // Người đánh giá phải thuộc đơn; người được đánh giá là bên còn lại của đơn
        int expectedReviewee;
        if (booking.CustomerId == userId && booking.TaskerId is not null) expectedReviewee = booking.TaskerId.Value;
        else throw ApiException.BadRequest("Bạn không thuộc đơn này.");
        if (request.RevieweeId != expectedReviewee)
            throw ApiException.BadRequest("Người được đánh giá không đúng.");

        if (booking.Status != BookingStatus.Completed && booking.Status != BookingStatus.Paid)
            throw ApiException.BadRequest("Chỉ đánh giá được khi đơn đã hoàn thành.");

        if (await _db.Reviews.AnyAsync(r => r.BookingId == booking.Id && r.ReviewerId == userId))
            throw ApiException.Conflict("Bạn đã đánh giá đơn này rồi.");

        var review = new Review
        {
            BookingId = booking.Id,
            ReviewerId = userId,
            RevieweeId = expectedReviewee,
            Rating = request.Rating!.Value,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
        };
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        await UpdateTaskerRatingAsync(expectedReviewee);

        return CreatedAtAction(nameof(GetForBooking), new { bookingId = booking.Id }, await ToResponseAsync(review.Id));
    }

    [HttpGet("booking/{bookingId:int}")]
    public async Task<ActionResult<IEnumerable<ReviewResponse>>> GetForBooking(int bookingId)
    {
        var reviews = await Project(_db.Reviews.Where(r => r.BookingId == bookingId)).ToListAsync();
        return Ok(reviews);
    }

    // US27 - Admin xem & xử lý các đánh giá bị gắn cờ
    [HttpGet("flagged")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<ReviewResponse>>> GetFlagged()
    {
        var flagged = await Project(_db.Reviews.Where(r => r.IsFlagged)).ToListAsync();
        return Ok(flagged);
    }

    [HttpPut("{id:int}/flag")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ResolveFlag(int id, [FromQuery] bool clear = true)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review is null) return NotFound();

        review.IsFlagged = !clear;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>BR-R02: ratingAverage = trung bình rating mà Tasker được nhận, làm tròn 2 chữ số; chưa có → 5.00.</summary>
    private async Task UpdateTaskerRatingAsync(int userId)
    {
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == userId);
        if (profile is null) return;
        var ratings = await _db.Reviews.Where(r => r.RevieweeId == userId).Select(r => r.Rating).ToListAsync();
        profile.RatingAverage = ratings.Count == 0 ? 5.00m : Math.Round((decimal)ratings.Average(), 2, MidpointRounding.AwayFromZero);
        await _db.SaveChangesAsync();
    }

    private static IQueryable<ReviewResponse> Project(IQueryable<Review> source) => source.AsNoTracking()
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new ReviewResponse(r.Id, r.BookingId, r.ReviewerId, r.Reviewer!.FullName, r.RevieweeId,
            r.Rating, r.Comment, r.IsFlagged, r.CreatedAt, r.Reviewee!.FullName));

    private Task<ReviewResponse> ToResponseAsync(int id) => Project(_db.Reviews.Where(r => r.Id == id)).FirstAsync();
}
