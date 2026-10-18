using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// US16 – Tự động ghép nối Tasker theo đợt (FR-12, BR-M01 → BR-M06).
/// Được BookingWorker gọi định kỳ (Matching:TickSeconds). Mọi trạng thái nằm trong DB
/// (Bookings.CurrentWave, NextMatchAt, BookingOffers) nên Backend khởi động lại vẫn chạy tiếp.
/// </summary>
public class MatchingService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly MatchingOptions _opt;
    private readonly ILogger<MatchingService> _logger;

    public MatchingService(AppDbContext db, INotificationService notifications,
        IOptions<MatchingOptions> options, ILogger<MatchingService> logger)
    {
        _db = db;
        _notifications = notifications;
        _opt = options.Value;
        _logger = logger;
    }

    /// <summary>Xử lý tất cả đơn đang đến lượt ghép nối. Trả về số đơn đã xử lý.</summary>
    public async Task<int> ProcessDueBookingsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Đơn đến lượt: (a) Pending chưa bắt đầu/đến giờ thử lại; (b) Matched hết hạn chờ đợt hiện tại
        // hoặc mọi lời mời của đợt hiện tại đã bị từ chối (sang đợt kế ngay, không chờ hết giờ).
        var dueIds = await _db.Bookings
            .Where(b => b.TaskerId == null &&
                        ((b.Status == BookingStatus.Pending && (b.NextMatchAt == null || b.NextMatchAt <= now)) ||
                         (b.Status == BookingStatus.Matched &&
                          (b.NextMatchAt == null || b.NextMatchAt <= now ||
                           !b.Offers.Any(o => o.Round == b.MatchRound && o.Wave == b.CurrentWave && o.Status == OfferStatus.Sent)))))
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var id in dueIds)
        {
            try
            {
                await ProcessBookingAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ghép nối đơn {BookingId} thất bại", id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return dueIds.Count;
    }

    private async Task ProcessBookingAsync(int bookingId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var booking = await _db.Bookings
            .Include(b => b.ServiceCategory)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null || booking.TaskerId != null ||
            (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Matched))
            return;

        // Đóng các lời mời còn treo của đợt trước (hết hạn trả lời)
        await _db.BookingOffers
            .Where(o => o.BookingId == bookingId && o.Status == OfferStatus.Sent)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OfferStatus.Expired)
                .SetProperty(o => o.RespondedAt, now), ct);

        var wave = booking.CurrentWave + 1;
        var waveCount = Math.Min(_opt.Radii.Length, _opt.Timeouts.Length);
        List<Candidate> chosen = new();

        // Đợt không có Tasker đủ điều kiện → bỏ qua, sang đợt kế
        for (; wave <= waveCount; wave++)
        {
            var candidates = await FindCandidatesAsync(booking, _opt.Radii[wave - 1], now, ct);
            chosen = candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.DistanceKm)
                .Take(Math.Max(1, _opt.OffersPerWave))
                .ToList();
            if (chosen.Count > 0) break;
        }

        if (chosen.Count > 0)
        {
            var expiresAt = now.AddSeconds(_opt.Timeouts[wave - 1]);

            // Cập nhật có điều kiện: nếu vừa có Tasker nhận đơn (hoặc khách hủy) thì không làm gì
            var updated = await _db.Bookings
                .Where(b => b.Id == bookingId && b.TaskerId == null &&
                            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Matched))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, BookingStatus.Matched)
                    .SetProperty(b => b.CurrentWave, wave)
                    .SetProperty(b => b.NextMatchAt, expiresAt)
                    .SetProperty(b => b.UpdatedAt, now), ct);
            if (updated == 0) return;

            foreach (var c in chosen)
            {
                _db.BookingOffers.Add(new BookingOffer
                {
                    BookingId = bookingId,
                    TaskerId = c.TaskerId,
                    Round = booking.MatchRound,
                    Wave = wave,
                    DistanceKm = Math.Round((decimal)c.DistanceKm, 2),
                    Score = Math.Round((decimal)c.Score, 4),
                    Status = OfferStatus.Sent,
                    SentAt = now,
                    ExpiresAt = expiresAt,
                });
                // US28 – thông báo đơn mới cho Tasker
                _notifications.Add(c.TaskerId, "Bạn có đơn mới",
                    $"{booking.ServiceCategory!.Name} – {booking.Address} – cách bạn {c.DistanceKm:0.0} km. " +
                    $"Giá {MoneyFormat.Vnd(booking.TotalPrice)}. Hãy trả lời trong {_opt.Timeouts[wave - 1]} giây.");
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogInformation("Đơn {BookingId}: gửi đợt {Wave} cho {Count} Tasker", bookingId, wave, chosen.Count);
            return;
        }

        // Hết các đợt mà không ai nhận
        var stopRetryAt = booking.ScheduledTime.AddHours(-_opt.StopRetryHoursBefore);
        if (now < stopRetryAt)
        {
            // BR-M05: đơn đặt trước giữ status 0 và tự thử lại sau mỗi 30 phút đến T − 2 giờ
            var nextTry = now.AddMinutes(_opt.AdvanceRetryMinutes);
            if (nextTry > stopRetryAt) nextTry = stopRetryAt;
            await _db.Bookings
                .Where(b => b.Id == bookingId && b.TaskerId == null &&
                            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Matched))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, BookingStatus.Pending)
                    .SetProperty(b => b.CurrentWave, 0)
                    .SetProperty(b => b.NextMatchAt, nextTry)
                    .SetProperty(b => b.UpdatedAt, now), ct);
            await tx.CommitAsync(ct);
            return;
        }

        // FR-24: chuyển sang "Chưa có Tasker nhận" và báo khách hàng
        var rows = await _db.Bookings
            .Where(b => b.Id == bookingId && b.TaskerId == null &&
                        (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Matched))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.NoTaskerFound)
                .SetProperty(b => b.NextMatchAt, (DateTime?)null)
                .SetProperty(b => b.UpdatedAt, now), ct);
        if (rows > 0)
        {
            _notifications.Add(booking.CustomerId, "Chưa có Tasker nhận đơn",
                "Hiện chưa có Tasker nhận đơn của bạn. Bạn có thể thử lại hoặc đổi giờ.");
            await _db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Lọc cứng (BR-M01) rồi chấm điểm (BR-M02) các Tasker trong bán kính của đợt.</summary>
    private async Task<List<Candidate>> FindCandidatesAsync(Booking booking, double waveRadiusKm, DateTime now, CancellationToken ct)
    {
        if (booking.Latitude is null || booking.Longitude is null) return new();

        // Tasker đã được mời trong vòng ghép hiện tại → không gửi lại (BR-M03)
        // + loại Tasker từng nhận đơn này rồi không đến (FR-28: "ghép lại và loại Tasker cũ")
        var invited = await _db.BookingOffers
            .Where(o => o.BookingId == booking.Id && (o.Round == booking.MatchRound || o.Status == OfferStatus.Accepted))
            .Select(o => o.TaskerId)
            .ToListAsync(ct);

        var profiles = await _db.TaskerProfiles
            .AsNoTracking()
            .Where(t => t.VerificationStatus == TaskerVerificationStatus.Approved
                        && t.IsAvailable
                        && t.User!.IsActive
                        && (t.SuspendedUntil == null || t.SuspendedUntil <= now)
                        && !invited.Contains(t.UserId))
            .ToListAsync(ct);

        profiles = profiles.Where(t => SkillMatcher.Matches(t.Skills, booking.ServiceCategory!.Name)).ToList();
        if (profiles.Count == 0) return new();

        // Vị trí ghép nối (BR-M06) + bán kính: trong bán kính đợt và trong bán kính phục vụ Tasker đã chọn
        var freshAfter = now.AddMinutes(-_opt.LocationFreshMinutes);
        var inRange = new List<(TaskerProfile Profile, double Distance)>();
        foreach (var t in profiles)
        {
            var useLast = t.LastLocationAt != null && t.LastLocationAt >= freshAfter
                          && t.LastLatitude != null && t.LastLongitude != null;
            var lat = useLast ? t.LastLatitude!.Value : t.BaseLatitude;
            var lng = useLast ? t.LastLongitude!.Value : t.BaseLongitude;
            var d = GeoHelper.DistanceKm(lat, lng, booking.Latitude.Value, booking.Longitude.Value);
            if (d <= waveRadiusKm && d <= t.ServiceRadiusKm) inRange.Add((t, d));
        }
        if (inRange.Count == 0) return new();

        var ids = inRange.Select(x => x.Profile.UserId).ToList();

        // BR-B02: không trùng lịch với đơn Accepted/InProgress khác (cách nhau dưới 2 giờ)
        var gapFrom = booking.ScheduledTime.AddHours(-_opt.ScheduleGapHours);
        var gapTo = booking.ScheduledTime.AddHours(_opt.ScheduleGapHours);
        var busy = await _db.Bookings
            .Where(b => b.TaskerId != null && ids.Contains(b.TaskerId.Value) && b.Id != booking.Id
                        && (b.Status == BookingStatus.Accepted || b.Status == BookingStatus.InProgress)
                        && b.ScheduledTime > gapFrom && b.ScheduledTime < gapTo)
            .Select(b => b.TaskerId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // Số liệu chấm điểm: tỷ lệ nhận đơn 30 ngày & lần nhận đơn gần nhất
        var since = now.AddDays(-30);
        var offerStats = await _db.BookingOffers
            .Where(o => ids.Contains(o.TaskerId) && o.SentAt >= since)
            .GroupBy(o => o.TaskerId)
            .Select(g => new { TaskerId = g.Key, Sent = g.Count(), Accepted = g.Count(o => o.Status == OfferStatus.Accepted) })
            .ToListAsync(ct);
        var lastAccepted = await _db.BookingOffers
            .Where(o => ids.Contains(o.TaskerId) && o.Status == OfferStatus.Accepted && o.RespondedAt != null)
            .GroupBy(o => o.TaskerId)
            .Select(g => new { TaskerId = g.Key, Last = g.Max(o => o.RespondedAt) })
            .ToListAsync(ct);

        var commissionEstimate = Money.Commission(booking.TotalPrice, booking.CommissionRate);

        var result = new List<Candidate>();
        foreach (var (t, d) in inRange)
        {
            if (busy.Contains(t.UserId)) continue;

            // BR-P04: đơn tiền mặt chỉ mời Tasker đủ số dư ví để trừ chiết khấu
            if (booking.PaymentMethod == PaymentMethod.Cash && t.WalletBalance - t.ReservedAmount < commissionEstimate) continue;

            var maxRadius = _opt.Radii.Max();
            var distanceScore = Math.Clamp(1 - d / maxRadius, 0, 1);
            var ratingScore = Math.Clamp(((double)t.RatingAverage - 1) / 4, 0, 1);
            var stat = offerStats.FirstOrDefault(s => s.TaskerId == t.UserId);
            var acceptScore = stat is null || stat.Sent == 0 ? 0.5 : (double)stat.Accepted / stat.Sent;
            var last = lastAccepted.FirstOrDefault(s => s.TaskerId == t.UserId)?.Last;
            var fairScore = last is null ? 1 : Math.Min(1, (now - last.Value).TotalHours / 24);

            var score = 0.40 * distanceScore + 0.30 * ratingScore + 0.20 * acceptScore + 0.10 * fairScore;
            result.Add(new Candidate(t.UserId, d, score));
        }
        return result;
    }

    private sealed record Candidate(int TaskerId, double DistanceKm, double Score);
}
