using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// US26 – GET /api/public-profiles/{userId}: xem hồ sơ của bên còn lại trong đơn (khách ↔ Tasker) cùng các đánh giá người đó đã nhận.
// Chỉ xem được khi hai bên có liên quan: chung một đơn, hoặc Tasker đang/đã nhận lời mời đơn của khách đó.
[ApiController]
[Route("api/public-profiles")]
[Authorize]
public class PublicProfilesController : ControllerBase
{
    private const int MaxReviews = 30;
    private readonly AppDbContext _db;

    public PublicProfilesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<PublicProfileResponse>> Get(int userId)
    {
        var viewerId = User.GetUserId();
        var user = await _db.Users.AsNoTracking().Include(u => u.TaskerProfile).FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.Role == UserRole.Admin) return NotFound();

        var isAdmin = User.IsInRole(UserRole.Admin);
        var sharedBooking = await _db.Bookings.AnyAsync(b =>
            (b.CustomerId == viewerId && b.TaskerId == userId) || (b.TaskerId == viewerId && b.CustomerId == userId));
        var invited = !sharedBooking && await _db.BookingOffers.AnyAsync(o => o.TaskerId == viewerId && o.Booking!.CustomerId == userId);
        if (!isAdmin && viewerId != userId && !sharedBooking && !invited) return Forbid();

        var isTasker = user.Role == UserRole.Tasker;
        var completed = await _db.Bookings.CountAsync(b =>
            (isTasker ? b.TaskerId == userId : b.CustomerId == userId) && b.Status == BookingStatus.Paid);

        var visible = _db.Reviews.Where(r => r.RevieweeId == userId && !r.IsFlagged && r.RemovedAt == null);
        var stats = await visible.GroupBy(r => r.RevieweeId)
            .Select(g => new { Count = g.Count(), Avg = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();
        var reviews = await visible.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(MaxReviews)
            .Select(r => new ReviewResponse(r.Id, r.BookingId, r.ReviewerId, r.Reviewer!.FullName, r.RevieweeId,
                r.Rating, r.Comment, r.IsFlagged, r.CreatedAt, r.Reviewee!.FullName, null, null, null, null, null))
            .ToListAsync();
        // Chỉ hiện tên gọi của người viết đánh giá
        reviews = reviews.Select(r => r with { ReviewerName = NameHelper.GivenName(r.ReviewerName) }).ToList();

        var profile = user.TaskerProfile;
        return Ok(new PublicProfileResponse(
            user.Id,
            sharedBooking || isAdmin || viewerId == userId ? user.FullName : NameHelper.GivenName(user.FullName),
            user.Role,
            UploadUrl.Normalize(user.AvatarUrl),
            user.CreatedAt,
            completed,
            stats?.Count ?? 0,
            stats is null ? null : Math.Round((decimal)stats.Avg, 2),
            isTasker ? profile?.VerificationStatus == TaskerVerificationStatus.Approved : null,
            isTasker ? profile?.WorkingArea : null,
            isTasker ? profile?.Skills : null,
            reviews));
    }
}
