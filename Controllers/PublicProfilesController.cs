using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// US26 – GET /api/public-profiles/{userId}: xem hồ sơ của bên còn lại trong đơn (khách ↔ Tasker) cùng các đánh giá người đó đã nhận.
// Chỉ xem được khi hai bên có liên quan: chung một đơn, hoặc Tasker đang/đã nhận lời mời đơn của khách đó.
// Hồ sơ chỉ kèm vài đánh giá mới nhất và số lượng theo từng mức sao; xem đủ (lọc theo sao, phân trang) ở GET .../{userId}/reviews.
[ApiController]
[Route("api/public-profiles")]
[Authorize]
public class PublicProfilesController : ControllerBase
{
    private const int PreviewReviews = 3;
    private readonly AppDbContext _db;
    private readonly TaskerAwardService? _awards;

    public PublicProfilesController(AppDbContext db, TaskerAwardService? awards = null)
    {
        _db = db;
        _awards = awards;
    }

    /// <summary>Người đang xem có được xem hồ sơ / đánh giá của userId không (null = không tồn tại hoặc là Admin).</summary>
    private async Task<(bool Allowed, bool SharedBooking, bool IsAdmin)?> AccessAsync(int userId, int viewerId)
    {
        var role = await _db.Users.Where(u => u.Id == userId).Select(u => (UserRole?)u.Role).FirstOrDefaultAsync();
        if (role is null || role == UserRole.Admin) return null;
        var isAdmin = User.IsInRole(UserRole.Admin);
        var sharedBooking = await _db.Bookings.AnyAsync(b =>
            (b.CustomerId == viewerId && b.TaskerId == userId) || (b.TaskerId == viewerId && b.CustomerId == userId));
        var invited = !sharedBooking && await _db.BookingOffers.AnyAsync(o => o.TaskerId == viewerId && o.Booking!.CustomerId == userId);
        return (isAdmin || viewerId == userId || sharedBooking || invited, sharedBooking, isAdmin);
    }

    private IQueryable<Review> Visible(int userId) =>
        _db.Reviews.Where(r => r.RevieweeId == userId && !r.IsFlagged && r.RemovedAt == null);

    private static async Task<List<ReviewResponse>> ProjectAsync(IQueryable<Review> query)
    {
        var list = await query.AsNoTracking()
            .Select(r => new ReviewResponse(r.Id, r.BookingId, r.ReviewerId, r.Reviewer!.FullName, r.RevieweeId,
                r.Rating, r.Comment, r.IsFlagged, r.CreatedAt, r.Reviewee!.FullName, null, null, null, null, null))
            .ToListAsync();
        // Chỉ hiện tên gọi của người viết đánh giá
        return list.Select(r => r with { ReviewerName = NameHelper.GivenName(r.ReviewerName) }).ToList();
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<PublicProfileResponse>> Get(int userId)
    {
        var viewerId = User.GetUserId();
        var access = await AccessAsync(userId, viewerId);
        if (access is null) return NotFound();
        if (!access.Value.Allowed) return Forbid();
        var (_, sharedBooking, isAdmin) = access.Value;
        var user = await _db.Users.AsNoTracking().Include(u => u.TaskerProfile).FirstAsync(u => u.Id == userId);

        var isTasker = user.Role == UserRole.Tasker;
        var completed = await _db.Bookings.CountAsync(b =>
            (isTasker ? b.TaskerId == userId : b.CustomerId == userId) && b.Status == BookingStatus.Paid);

        var visible = Visible(userId);
        var byStar = await visible.GroupBy(r => r.Rating).Select(g => new { Stars = g.Key, Count = g.Count() }).ToListAsync();
        var total = byStar.Sum(x => x.Count);
        decimal? average = total == 0 ? null : Math.Round((decimal)byStar.Sum(x => x.Stars * x.Count) / total, 2);
        var breakdown = Enumerable.Range(1, 5).Reverse()
            .Select(s => new StarCountResponse(s, byStar.Where(x => x.Stars == s).Sum(x => x.Count))).ToList();
        var reviews = await ProjectAsync(visible.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(PreviewReviews));

        var profile = user.TaskerProfile;
        var badges = isTasker ? (await TaskerAwardService.BadgesAsync(_db, new[] { userId })).GetValueOrDefault(userId) : null;
        // Nhãn "Đang trong top N" của tháng đang diễn ra – không ghi số hạng tạm tính vì còn thay đổi
        CurrentTopResponse? currentTop = null;
        if (isTasker && user.IsActive && _awards is not null && await _awards.IsInLiveTopAsync(userId))
        {
            var (y, m) = TaskerAwardService.VnMonthOf(DateTime.UtcNow);
            currentTop = new CurrentTopResponse(y, m, _awards.TopCount,
                $"Đang trong top {_awards.TopCount} Tasker tháng {TaskerAwardService.MonthLabel(y, m)}");
        }
        return Ok(new PublicProfileResponse(
            user.Id,
            sharedBooking || isAdmin || viewerId == userId ? user.FullName : NameHelper.GivenName(user.FullName),
            user.Role,
            UploadUrl.Normalize(user.AvatarUrl),
            user.CreatedAt,
            completed,
            total,
            average,
            isTasker ? profile?.VerificationStatus == TaskerVerificationStatus.Approved : null,
            isTasker ? profile?.WorkingArea : null,
            isTasker ? profile?.Skills : null,
            reviews,
            isTasker ? badges ?? new List<AwardBadgeResponse>() : null,
            currentTop,
            breakdown));
    }

    /// <summary>
    /// Đánh giá người đó đã nhận, mới nhất trước, lọc theo số sao (1–5, bỏ trống = tất cả), phân trang (mặc định 10/trang).
    /// Cùng quyền xem như hồ sơ; người viết chỉ hiện tên gọi.
    /// </summary>
    [HttpGet("{userId:int}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewResponse>>> GetReviews(int userId, [FromQuery] int? stars,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        if (stars is < 1 or > 5) throw ApiException.BadRequest("Số sao lọc phải từ 1 đến 5.");
        var access = await AccessAsync(userId, User.GetUserId());
        if (access is null) return NotFound();
        if (!access.Value.Allowed) return Forbid();

        var (p, size) = Paging.Normalize(page, pageSize ?? 10);
        var query = Visible(userId);
        if (stars is { } s) query = query.Where(r => r.Rating == s);
        var count = await query.CountAsync();
        var items = await ProjectAsync(query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((p - 1) * size).Take(size));
        return Ok(new PagedResult<ReviewResponse>(items, p, size, count));
    }
}
