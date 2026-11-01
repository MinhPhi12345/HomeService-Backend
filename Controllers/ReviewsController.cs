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

    // US25 - Khách hàng đánh giá Tasker / US26 - Tasker đánh giá khách hàng, sau khi đơn hoàn thành (1 lần / đơn – BR-R01)
    [HttpPost]
    public async Task<ActionResult<ReviewResponse>> Create(CreateReviewRequest request)
    {
        var userId = User.GetUserId();
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == request.BookingId);
        if (booking is null) throw ApiException.BadRequest("Đơn không tồn tại.");

        // Người đánh giá phải thuộc đơn; người được đánh giá là bên còn lại của đơn
        int expectedReviewee;
        if (booking.CustomerId == userId && booking.TaskerId is not null) expectedReviewee = booking.TaskerId.Value;
        else if (booking.TaskerId == userId) expectedReviewee = booking.CustomerId; // US26
        else throw ApiException.BadRequest("Bạn không thuộc đơn này.");
        if (request.RevieweeId != expectedReviewee)
            throw ApiException.BadRequest("Người được đánh giá không đúng.");

        if (booking.Status != BookingStatus.Completed && booking.Status != BookingStatus.Paid)
            throw ApiException.BadRequest("Chỉ đánh giá được khi đơn đã hoàn thành.");

        // BR-R01: mỗi bên chỉ đánh giá 1 lần / đơn – kể cả khi đánh giá trước đó đã bị Admin gỡ
        var previous = await _db.Reviews.Where(r => r.BookingId == booking.Id && r.ReviewerId == userId)
            .Select(r => new { r.RemovedAt }).FirstOrDefaultAsync();
        if (previous is not null)
            throw ApiException.Conflict(previous.RemovedAt is null
                ? "Bạn đã đánh giá đơn này rồi."
                : "Đánh giá của bạn cho đơn này đã bị quản trị viên gỡ. Mỗi đơn chỉ được đánh giá một lần.");

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
        // Lý do báo cáo / kết quả kiểm duyệt chỉ hiện cho người trong đơn và Admin
        var userId = User.GetUserId();
        var involved = User.IsInRole(UserRole.Admin) || await _db.Bookings.AnyAsync(b => b.Id == bookingId && (b.CustomerId == userId || b.TaskerId == userId));
        var reviews = await Project(_db.Reviews.Where(r => r.BookingId == bookingId && r.RemovedAt == null), involved).ToListAsync();
        return Ok(reviews);
    }

    // US26 - Xem các đánh giá mà một người nhận được (vd: Tasker xem uy tín khách trước khi nhận đơn)
    [HttpGet("user/{userId:int}")]
    public async Task<ActionResult<UserReviewSummary>> GetForUser(int userId)
    {
        var reviews = await Project(_db.Reviews.Where(r => r.RevieweeId == userId && !r.IsFlagged && r.RemovedAt == null)).Take(50).ToListAsync();
        var stats = await _db.Reviews.Where(r => r.RevieweeId == userId && !r.IsFlagged && r.RemovedAt == null)
            .GroupBy(r => r.RevieweeId)
            .Select(g => new { Count = g.Count(), Avg = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();
        // Xem nhận xét về người khác: chỉ hiện tên gọi của người viết (vd. "Lan"), không lộ họ tên đầy đủ
        reviews = reviews.Select(r => r with { ReviewerName = NameHelper.GivenName(r.ReviewerName) }).ToList();
        return Ok(new UserReviewSummary(userId, stats?.Count ?? 0,
            stats is null ? null : Math.Round((decimal)stats.Avg, 2), reviews));
    }

    // US27 - Người trong đơn (người được đánh giá / bên còn lại) báo cáo đánh giá sai sự thật → gắn cờ chờ Admin xử lý.
    // Bắt buộc nêu lý do để Admin có căn cứ phân xử; đánh giá Admin đã xem xét và giữ lại thì không báo cáo lại được.
    [HttpPost("{id:int}/report")]
    public async Task<IActionResult> Report(int id, ReportReviewRequest request, [FromServices] INotificationService notifications)
    {
        var review = await _db.Reviews.Include(r => r.Booking).FirstOrDefaultAsync(r => r.Id == id && r.RemovedAt == null);
        if (review is null) return NotFound();
        var userId = User.GetUserId();
        if (review.ReviewerId == userId) throw ApiException.BadRequest("Không thể báo cáo đánh giá của chính mình.");
        var isParticipant = review.RevieweeId == userId || review.Booking!.CustomerId == userId || review.Booking.TaskerId == userId;
        if (!isParticipant) return Forbid();

        if (review.IsFlagged) return NoContent(); // đã có báo cáo đang chờ xử lý
        if (review.ModeratedAt is not null)
            throw ApiException.BadRequest("Đánh giá này đã được quản trị viên xem xét và giữ lại, không thể báo cáo lại.");

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < 10) throw ApiException.BadRequest("Lý do báo cáo cần ít nhất 10 ký tự.");

        review.IsFlagged = true;
        review.ReportedById = userId;
        review.ReportReason = reason;
        review.ReportedAt = DateTime.UtcNow;
        await notifications.AddForAdminsAsync("Đánh giá bị báo cáo",
            $"Đánh giá #{review.Id} của đơn #{review.BookingId} bị báo cáo: {Shorten(reason, 120)}");
        await _db.SaveChangesAsync();
        await UpdateTaskerRatingAsync(review.RevieweeId);
        return NoContent();
    }

    // US27 - Admin xem các đánh giá bị gắn cờ (kèm người báo cáo và lý do)
    [HttpGet("flagged")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<ReviewResponse>>> GetFlagged()
    {
        var flagged = await Project(_db.Reviews.Where(r => r.IsFlagged), includeModeration: true).ToListAsync();
        return Ok(flagged);
    }

    // US27 - Admin kiểm duyệt đánh giá bị báo cáo: giữ lại (gỡ cờ) hoặc gỡ hẳn. Bắt buộc ghi lý do;
    // người báo cáo và người viết đánh giá được thông báo kết quả, quyết định được ghi vào nhật ký kiểm toán.
    [HttpPut("{id:int}/moderation")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Moderate(int id, ModerateReviewRequest request,
        [FromServices] INotificationService notifications, [FromServices] AuditService audit)
    {
        var review = await _db.Reviews.Include(r => r.Reviewer).Include(r => r.Reviewee).FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();
        if (!review.IsFlagged) throw ApiException.BadRequest("Đánh giá này không ở trạng thái chờ kiểm duyệt.");

        var note = (request.Note ?? string.Empty).Trim();
        if (note.Length < 10) throw ApiException.BadRequest("Lý do quyết định cần ít nhất 10 ký tự.");

        review.IsFlagged = false;
        review.ModerationNote = note;
        review.ModeratedById = User.GetUserId();
        review.ModeratedAt = DateTime.UtcNow;
        var summary = $"đánh giá {review.Rating}★ của {review.Reviewer!.FullName} cho {review.Reviewee!.FullName} (đơn #{review.BookingId})";
        var reportedBy = review.ReportedById;

        if (request.Remove == true)
        {
            audit.Add(AuditService.ReviewRemoved, 0, review.BookingId, null,
                $"Gỡ {summary}. Nội dung: \"{Shorten(review.Comment ?? "(không có)", 120)}\". Báo cáo: \"{Shorten(review.ReportReason ?? "(không ghi)", 120)}\". Lý do gỡ: {note}");
            notifications.Add(review.ReviewerId, "Đánh giá của bạn đã bị gỡ",
                $"Đánh giá {review.Rating}★ của bạn cho đơn #{review.BookingId} đã bị quản trị viên gỡ. Lý do: {note.TrimEnd('.')}. " +
                "Mỗi đơn chỉ được đánh giá một lần nên bạn không thể đánh giá lại đơn này.");
            if (reportedBy is not null && reportedBy != review.ReviewerId)
                notifications.Add(reportedBy.Value, "Báo cáo đánh giá được chấp nhận",
                    $"Đánh giá bạn báo cáo ở đơn #{review.BookingId} đã được gỡ. Lý do: {note}");
            // Ẩn thay vì xóa hẳn: giữ dấu vết và chặn người viết đánh giá lại đơn này (BR-R01)
            review.RemovedAt = DateTime.UtcNow;
        }
        else
        {
            audit.Add(AuditService.ReviewKept, 0, review.BookingId, null,
                $"Giữ lại {summary}. Báo cáo: \"{Shorten(review.ReportReason ?? "(không ghi)", 120)}\". Lý do giữ: {note}");
            if (reportedBy is not null)
                notifications.Add(reportedBy.Value, "Báo cáo đánh giá không được chấp nhận",
                    $"Đánh giá bạn báo cáo ở đơn #{review.BookingId} được giữ lại. Lý do: {note}");
        }

        await _db.SaveChangesAsync();
        await UpdateTaskerRatingAsync(review.RevieweeId);
        return NoContent();
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>BR-R02: ratingAverage = trung bình rating mà Tasker được nhận, làm tròn 2 chữ số; chưa có → 5.00.</summary>
    private async Task UpdateTaskerRatingAsync(int userId)
    {
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == userId);
        if (profile is null) return;
        // Đánh giá đang bị gắn cờ (chờ Admin xử lý) tạm không tính vào điểm trung bình
        var ratings = await _db.Reviews.Where(r => r.RevieweeId == userId && !r.IsFlagged && r.RemovedAt == null).Select(r => r.Rating).ToListAsync();
        profile.RatingAverage = ratings.Count == 0 ? 5.00m : Math.Round((decimal)ratings.Average(), 2, MidpointRounding.AwayFromZero);
        await _db.SaveChangesAsync();
    }

    private IQueryable<ReviewResponse> Project(IQueryable<Review> source, bool includeModeration = false) => source.AsNoTracking()
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new ReviewResponse(r.Id, r.BookingId, r.ReviewerId, r.Reviewer!.FullName, r.RevieweeId,
            r.Rating, r.Comment, r.IsFlagged, r.CreatedAt, r.Reviewee!.FullName,
            includeModeration ? r.ReportReason : null,
            includeModeration ? _db.Users.Where(u => u.Id == r.ReportedById).Select(u => u.FullName).FirstOrDefault() : null,
            includeModeration ? r.ReportedAt : null,
            includeModeration ? r.ModerationNote : null,
            includeModeration ? r.ModeratedAt : null));

    private Task<ReviewResponse> ToResponseAsync(int id) => Project(_db.Reviews.Where(r => r.Id == id)).FirstAsync();
}
