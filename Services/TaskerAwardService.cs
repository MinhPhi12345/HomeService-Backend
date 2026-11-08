using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// EP09 – Tasker xuất sắc tháng (FR-31, BR-T01 → BR-T04): chốt bảng xếp hạng theo tháng (giờ Việt Nam),
/// Admin duyệt rồi hệ thống trao thưởng (tiền vào ví, chiết khấu ưu đãi tháng sau, huy hiệu, quà hiện vật).
/// </summary>
public class TaskerAwardService
{
    public static readonly TimeSpan VnOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _db;
    private readonly WalletService _wallet;
    private readonly INotificationService _notifications;
    private readonly AuditService _audit;
    private readonly IEmailSender _email;
    private readonly AwardOptions _opt;
    private readonly BusinessOptions _business;
    private readonly ILogger<TaskerAwardService> _logger;

    public TaskerAwardService(AppDbContext db, WalletService wallet, INotificationService notifications, AuditService audit,
        IEmailSender email, IOptions<AwardOptions> options, IOptions<BusinessOptions> business, ILogger<TaskerAwardService> logger)
    {
        _db = db;
        _wallet = wallet;
        _notifications = notifications;
        _audit = audit;
        _email = email;
        _opt = options.Value;
        _business = business.Value;
        _logger = logger;
    }

    // ================= Thời gian theo tháng giờ Việt Nam =================

    /// <summary>[startUtc, endUtc) của tháng year/month theo giờ Việt Nam.</summary>
    public static (DateTime Start, DateTime End) MonthRange(int year, int month)
    {
        var start = DateTime.SpecifyKind(new DateTime(year, month, 1) - VnOffset, DateTimeKind.Utc);
        return (start, DateTime.SpecifyKind(new DateTime(year, month, 1).AddMonths(1) - VnOffset, DateTimeKind.Utc));
    }

    public static (int Year, int Month) VnMonthOf(DateTime utc)
    {
        var vn = utc + VnOffset;
        return (vn.Year, vn.Month);
    }

    public static (int Year, int Month) PreviousMonth(DateTime utc)
    {
        var (y, m) = VnMonthOf(utc);
        return m == 1 ? (y - 1, 12) : (y, m - 1);
    }

    public static string MonthLabel(int year, int month) => $"{month:00}/{year}";

    private static void ValidateMonth(int year, int month)
    {
        if (year is < 2020 or > 2100 || month is < 1 or > 12) throw ApiException.BadRequest("Tháng không hợp lệ.");
    }

    // ================= Chấm điểm (BR-T01, BR-T02) =================

    public record TaskerMetrics(int TaskerId, int Completed, int ReviewCount, decimal RatingSum, int Accepted, int Answered,
        int OnTime, int Started, int Disputes, int NoShows, int LostDisputes, bool IsActive);

    public record ScoredTasker(TaskerMetrics Metrics, decimal Score, decimal RatingAverage, decimal AcceptanceRate, decimal OnTimeRate);

    /// <summary>Đủ điều kiện xét (BR-T01): đủ số đơn, số đánh giá, không có lần không đến, không thua khiếu nại, tài khoản hoạt động.</summary>
    public bool IsEligible(TaskerMetrics m) =>
        m.IsActive && m.Completed >= _opt.MinCompletedBookings && m.ReviewCount >= _opt.MinReviews
        && m.NoShows == 0 && m.LostDisputes == 0;

    /// <summary>
    /// Điểm tổng 0–1 (BR-T02): đánh giá (trung bình có trọng số) 40%, số đơn hoàn tất so với người nhiều nhất 25%,
    /// tỷ lệ nhận lời mời 15%, tỷ lệ đúng giờ 10%, không bị khiếu nại 10%.
    /// </summary>
    public ScoredTasker Score(TaskerMetrics m, int maxCompleted)
    {
        var bayes = (_opt.RatingPriorCount * _opt.RatingPriorMean + m.RatingSum) / (_opt.RatingPriorCount + m.ReviewCount);
        var rating = Math.Clamp((bayes - 1) / 4, 0, 1);
        var completed = maxCompleted == 0 ? 0 : Math.Clamp((decimal)m.Completed / maxCompleted, 0, 1);
        var acceptance = m.Answered == 0 ? 1 : (decimal)m.Accepted / m.Answered;
        var onTime = m.Started == 0 ? 1 : (decimal)m.OnTime / m.Started;
        var noDispute = m.Completed == 0 ? 1 : 1 - Math.Min(1, (decimal)m.Disputes / m.Completed);

        var score = _opt.RatingWeight * rating + _opt.CompletedWeight * completed + _opt.AcceptanceWeight * acceptance
                    + _opt.OnTimeWeight * onTime + _opt.NoDisputeWeight * noDispute;
        var avg = m.ReviewCount == 0 ? 0 : m.RatingSum / m.ReviewCount;
        return new ScoredTasker(m, Math.Round(score, 4), Math.Round(avg, 2), Math.Round(acceptance, 4), Math.Round(onTime, 4));
    }

    /// <summary>Thu thập chỉ số của mọi Tasker đã duyệt trong tháng (xử lý ở bộ nhớ để chạy được cả SQLite lẫn SQL Server).</summary>
    public async Task<List<TaskerMetrics>> CollectMetricsAsync(int year, int month)
    {
        var (start, end) = MonthRange(year, month);
        var taskers = await _db.TaskerProfiles.AsNoTracking()
            .Where(t => t.VerificationStatus == TaskerVerificationStatus.Approved)
            .Select(t => new { t.UserId, t.User!.IsActive })
            .ToListAsync();
        var ids = taskers.Select(t => t.UserId).ToList();

        var completed = await _db.Bookings.AsNoTracking()
            .Where(b => b.TaskerId != null && ids.Contains(b.TaskerId.Value) && b.Status == BookingStatus.Paid
                        && b.CompletedAt >= start && b.CompletedAt < end)
            .Select(b => new { TaskerId = b.TaskerId!.Value, b.ScheduledTime, b.StartedAt })
            .ToListAsync();
        var reviews = await _db.Reviews.AsNoTracking()
            .Where(r => ids.Contains(r.RevieweeId) && !r.IsFlagged && r.RemovedAt == null && r.CreatedAt >= start && r.CreatedAt < end)
            .Select(r => new { r.RevieweeId, r.Rating })
            .ToListAsync();
        // Lời mời đã được trả lời (nhận / từ chối / để hết hạn); lời mời bị đóng vì người khác nhận trước không tính
        var offers = await _db.BookingOffers.AsNoTracking()
            .Where(o => ids.Contains(o.TaskerId) && o.SentAt >= start && o.SentAt < end
                        && (o.Status == OfferStatus.Accepted || o.Status == OfferStatus.Rejected || o.Status == OfferStatus.Expired))
            .Select(o => new { o.TaskerId, o.Status })
            .ToListAsync();
        // Không đến: lời mời đã nhận của đơn bị đánh dấu không đến, hoặc đơn bị chuyển cho Tasker khác sau khi Tasker này không đến
        var noShows = await _db.BookingOffers.AsNoTracking()
            .Where(o => ids.Contains(o.TaskerId) && o.Status == OfferStatus.Accepted
                        && o.Booking!.ScheduledTime >= start && o.Booking.ScheduledTime < end
                        && ((o.Booking.TaskerId == o.TaskerId && o.Booking.IsTaskerNoShow) || o.Booking.TaskerId != o.TaskerId))
            .Select(o => o.TaskerId)
            .ToListAsync();
        var disputes = await _db.Disputes.AsNoTracking()
            .Where(d => ids.Contains(d.TaskerId) && d.CreatedAt >= start && d.CreatedAt < end)
            .Select(d => d.TaskerId)
            .ToListAsync();
        var lost = await _db.Disputes.AsNoTracking()
            .Where(d => ids.Contains(d.TaskerId) && d.Status == DisputeStatus.Resolved && d.ResolvedAt >= start && d.ResolvedAt < end
                        && d.Resolution != null && d.Resolution != DisputeResolution.ReleaseFull)
            .Select(d => d.TaskerId)
            .ToListAsync();

        var grace = TimeSpan.FromMinutes(_opt.OnTimeGraceMinutes);
        return taskers.Select(t =>
        {
            var done = completed.Where(b => b.TaskerId == t.UserId).ToList();
            var started = done.Where(b => b.StartedAt != null).ToList();
            var myReviews = reviews.Where(r => r.RevieweeId == t.UserId).ToList();
            var myOffers = offers.Where(o => o.TaskerId == t.UserId).ToList();
            return new TaskerMetrics(t.UserId, done.Count, myReviews.Count, myReviews.Sum(r => (decimal)r.Rating),
                myOffers.Count(o => o.Status == OfferStatus.Accepted), myOffers.Count,
                started.Count(b => b.StartedAt <= b.ScheduledTime + grace), started.Count,
                disputes.Count(x => x == t.UserId), noShows.Count(x => x == t.UserId), lost.Count(x => x == t.UserId), t.IsActive);
        }).ToList();
    }

    /// <summary>Xếp hạng: điểm cao hơn → nhiều đơn hơn → điểm sao cao hơn (BR-T02).</summary>
    public List<ScoredTasker> Rank(IEnumerable<TaskerMetrics> metrics)
    {
        var eligible = metrics.Where(IsEligible).ToList();
        var max = eligible.Count == 0 ? 0 : eligible.Max(m => m.Completed);
        return eligible.Select(m => Score(m, max))
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Metrics.Completed)
            .ThenByDescending(s => s.RatingAverage)
            .ThenBy(s => s.Metrics.TaskerId)
            .ToList();
    }

    // ================= Chốt bảng xếp hạng =================

    private Task<bool> IsMonthApprovedAsync(int year, int month) =>
        _db.TaskerMonthlyAwards.AnyAsync(a => a.Year == year && a.Month == month && a.ApprovedAt != null);

    /// <summary>
    /// Chốt (hoặc tính lại khi chưa duyệt) bảng xếp hạng top N của một tháng đã kết thúc. Tasker Admin đã loại vẫn bị loại.
    /// </summary>
    public async Task<TaskerAwardMonthResponse> ComputeAsync(int year, int month)
    {
        ValidateMonth(year, month);
        var (_, end) = MonthRange(year, month);
        if (end > DateTime.UtcNow) throw ApiException.BadRequest("Chỉ chốt được bảng xếp hạng của tháng đã kết thúc.");
        if (await IsMonthApprovedAsync(year, month))
            throw ApiException.Conflict("Bảng xếp hạng tháng này đã được duyệt, không thể tính lại.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var old = await _db.TaskerMonthlyAwards.Where(a => a.Year == year && a.Month == month).ToListAsync();
        var excluded = old.Where(a => a.Status == TaskerAwardStatus.Excluded).ToDictionary(a => a.TaskerId, a => a.ExcludeReason);
        _db.TaskerMonthlyAwards.RemoveRange(old);
        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var ranked = Rank(await CollectMetricsAsync(year, month)).Take(Math.Max(1, _opt.TopCount)).ToList();
        foreach (var s in ranked)
        {
            var isExcluded = excluded.TryGetValue(s.Metrics.TaskerId, out var reason);
            _db.TaskerMonthlyAwards.Add(new TaskerMonthlyAward
            {
                Year = year,
                Month = month,
                TaskerId = s.Metrics.TaskerId,
                Score = s.Score,
                CompletedBookings = s.Metrics.Completed,
                ReviewCount = s.Metrics.ReviewCount,
                RatingAverage = s.RatingAverage,
                AcceptanceRate = s.AcceptanceRate,
                OnTimeRate = s.OnTimeRate,
                DisputeCount = s.Metrics.Disputes,
                Status = isExcluded ? TaskerAwardStatus.Excluded : TaskerAwardStatus.Proposed,
                ExcludeReason = isExcluded ? reason : null,
                ComputedAt = now,
            });
        }
        await _db.SaveChangesAsync();
        await ReRankAsync(year, month);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        _logger.LogInformation("Đã chốt Tasker xuất sắc tháng {Month}: {Count} Tasker", MonthLabel(year, month), ranked.Count);
        return await GetMonthAsync(year, month);
    }

    /// <summary>
    /// Tự chốt tháng trước nếu chưa có (BookingWorker gọi). Tháng không có ai đủ điều kiện thì không tạo dòng nào,
    /// nên được BookingWorker giới hạn tần suất gọi.
    /// </summary>
    public async Task<bool> EnsurePreviousMonthComputedAsync(CancellationToken ct = default)
    {
        var (y, m) = PreviousMonth(DateTime.UtcNow);
        if (await _db.TaskerMonthlyAwards.AnyAsync(a => a.Year == y && a.Month == m, ct)) return false;
        await ComputeAsync(y, m);
        return true;
    }

    /// <summary>Đánh lại hạng cho Tasker chưa bị loại theo điểm, gán phần thưởng mặc định cho người Admin chưa tự chỉnh.</summary>
    private async Task ReRankAsync(int year, int month)
    {
        var rows = await _db.TaskerMonthlyAwards.Where(a => a.Year == year && a.Month == month).ToListAsync();
        var rank = 0;
        foreach (var a in rows.Where(r => r.Status != TaskerAwardStatus.Excluded)
                     .OrderByDescending(r => r.Score).ThenByDescending(r => r.CompletedBookings)
                     .ThenByDescending(r => r.RatingAverage).ThenBy(r => r.TaskerId))
        {
            a.Rank = ++rank;
            if (!a.RewardCustomized) ApplyTier(a);
        }
        foreach (var a in rows.Where(r => r.Status == TaskerAwardStatus.Excluded))
        {
            a.Rank = 0;
            a.BonusAmount = 0;
            a.PreferentialRate = null;
            a.HasGift = false;
        }
    }

    private void ApplyTier(TaskerMonthlyAward a)
    {
        var tier = _opt.TierFor(a.Rank);
        a.BonusAmount = tier?.Bonus ?? 0;
        a.PreferentialRate = tier?.CommissionRate is { } r && r < _business.CommissionRate ? r : null;
        a.HasGift = tier?.Gift ?? false;
        a.GiftDescription = a.HasGift ? a.GiftDescription ?? "Quà tặng Tasker xuất sắc tháng" : null;
    }

    // ================= Xem / chỉnh / loại =================

    public async Task<TaskerAwardMonthResponse> GetMonthAsync(int year, int month)
    {
        ValidateMonth(year, month);
        var rows = await _db.TaskerMonthlyAwards.AsNoTracking()
            .Include(a => a.Tasker).ThenInclude(u => u!.TaskerProfile)
            .Where(a => a.Year == year && a.Month == month)
            .ToListAsync();
        var approved = rows.FirstOrDefault(r => r.ApprovedAt != null);
        string? approverName = null;
        if (approved?.ApprovedById is { } adminId)
            approverName = await _db.Users.Where(u => u.Id == adminId).Select(u => u.FullName).FirstOrDefaultAsync();

        var (_, end) = MonthRange(year, month);
        var state = approved is not null ? "Approved" : rows.Count > 0 ? "Proposed" : "NotComputed";
        return new TaskerAwardMonthResponse(year, month, state, end <= DateTime.UtcNow,
            rows.Count == 0 ? null : rows.Max(r => r.ComputedAt), approved?.ApprovedAt, approverName,
            _business.CommissionRate,
            rows.OrderBy(r => r.Status == TaskerAwardStatus.Excluded).ThenBy(r => r.Rank).ThenByDescending(r => r.Score)
                .Select(ToResponse).ToList());
    }

    public static TaskerAwardResponse ToResponse(TaskerMonthlyAward a) => new(
        a.Id, a.Year, a.Month, a.TaskerId, a.Tasker?.FullName ?? string.Empty, a.Tasker?.Email, a.Tasker?.PhoneNumber,
        a.Tasker?.AvatarUrl, a.Tasker?.TaskerProfile?.WorkingArea, a.Rank, Math.Round(a.Score * 100, 2), a.CompletedBookings,
        a.ReviewCount, a.RatingAverage, a.AcceptanceRate, a.OnTimeRate, a.DisputeCount, a.Status, a.ExcludeReason,
        a.BonusAmount, a.PreferentialRate, a.HasGift, a.GiftDescription, a.RewardCustomized, a.ApprovedAt, a.BonusPaidAt,
        a.PreferentialFrom, a.PreferentialTo, a.GiftEmailSentAt, a.GiftInvitation, a.GiftDeliveredAt);

    private async Task<TaskerMonthlyAward> FindAsync(int id) =>
        await _db.TaskerMonthlyAwards.FirstOrDefaultAsync(a => a.Id == id) ?? throw ApiException.NotFound();

    private async Task EnsureEditableAsync(TaskerMonthlyAward a)
    {
        if (a.ApprovedAt is not null || await IsMonthApprovedAsync(a.Year, a.Month))
            throw ApiException.Conflict("Bảng xếp hạng tháng này đã được duyệt, không thể chỉnh sửa.");
    }

    public async Task UpdateRewardAsync(int id, UpdateTaskerAwardRequest request)
    {
        var a = await FindAsync(id);
        await EnsureEditableAsync(a);
        if (a.Status == TaskerAwardStatus.Excluded) throw ApiException.BadRequest("Tasker này đã bị loại, hãy khôi phục trước khi chỉnh thưởng.");
        var bonus = request.BonusAmount ?? 0;
        if (bonus < 0 || decimal.Truncate(bonus) != bonus) throw ApiException.BadRequest("Tiền thưởng không hợp lệ.");
        if (request.PreferentialRate is { } rate && (rate <= 0 || rate >= _business.CommissionRate))
            throw ApiException.BadRequest($"Chiết khấu ưu đãi phải lớn hơn 0% và nhỏ hơn mức chung {_business.CommissionRate * 100:0.##}%.");
        if (request.HasGift && string.IsNullOrWhiteSpace(request.GiftDescription))
            throw ApiException.BadRequest("Vui lòng nhập tên quà tặng.");

        a.BonusAmount = bonus;
        a.PreferentialRate = request.PreferentialRate;
        a.HasGift = request.HasGift;
        a.GiftDescription = request.HasGift ? request.GiftDescription!.Trim() : null;
        a.RewardCustomized = true;
        await _db.SaveChangesAsync();
    }

    public async Task ExcludeAsync(int id, string reason)
    {
        var a = await FindAsync(id);
        await EnsureEditableAsync(a);
        a.Status = TaskerAwardStatus.Excluded;
        a.ExcludeReason = reason.Trim();
        await ReRankAsync(a.Year, a.Month);
        await _db.SaveChangesAsync();
    }

    public async Task RestoreAsync(int id)
    {
        var a = await FindAsync(id);
        await EnsureEditableAsync(a);
        if (a.Status != TaskerAwardStatus.Excluded) return;
        a.Status = TaskerAwardStatus.Proposed;
        a.ExcludeReason = null;
        a.RewardCustomized = false;
        await ReRankAsync(a.Year, a.Month);
        await _db.SaveChangesAsync();
    }

    // ================= Duyệt & trao thưởng (BR-T03) =================

    /// <summary>
    /// Duyệt cả tháng (mỗi tháng 1 lần): cộng tiền thưởng vào ví (giao dịch "Thưởng"), áp chiết khấu ưu đãi đến hết tháng
    /// kế tiếp tháng được xét, gắn huy hiệu, thông báo cho từng Tasker và ghi nhật ký kiểm toán.
    /// </summary>
    public async Task<TaskerAwardMonthResponse> ApproveAsync(int year, int month, int adminId)
    {
        ValidateMonth(year, month);
        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Duyệt NGUYÊN TỬ: chỉ 1 yêu cầu chuyển được các dòng "Chờ duyệt" sang "Đã duyệt"
        if (await IsMonthApprovedAsync(year, month)) throw ApiException.Conflict("Bảng xếp hạng tháng này đã được duyệt.");
        var rows = await _db.TaskerMonthlyAwards
            .Where(a => a.Year == year && a.Month == month && a.Status == TaskerAwardStatus.Proposed && a.ApprovedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, TaskerAwardStatus.Approved)
                .SetProperty(a => a.ApprovedAt, now)
                .SetProperty(a => a.ApprovedById, adminId));
        if (rows == 0)
        {
            if (await IsMonthApprovedAsync(year, month)) throw ApiException.Conflict("Bảng xếp hạng tháng này đã được duyệt.");
            throw ApiException.BadRequest("Chưa có Tasker nào trong danh sách chờ duyệt của tháng này.");
        }

        // Ưu đãi chiết khấu: từ lúc duyệt đến hết tháng kế tiếp tháng được xét (giờ VN); duyệt muộn thì đến hết tháng hiện tại
        var nextMonth = month == 12 ? (year + 1, 1) : (year, month + 1);
        var preferentialTo = MonthRange(nextMonth.Item1, nextMonth.Item2).End;
        if (preferentialTo <= now) { var (cy, cm) = VnMonthOf(now); preferentialTo = MonthRange(cy, cm).End; }

        var label = MonthLabel(year, month);
        var awards = await _db.TaskerMonthlyAwards
            .Where(a => a.Year == year && a.Month == month && a.Status == TaskerAwardStatus.Approved)
            .OrderBy(a => a.Rank).ToListAsync();
        foreach (var a in awards)
        {
            // Đồng bộ giá trị vừa cập nhật bằng ExecuteUpdate vào đối tượng đang được theo dõi
            a.Status = TaskerAwardStatus.Approved;
            a.ApprovedAt = now;
            a.ApprovedById = adminId;
            var parts = new List<string> { $"huy hiệu \"Tasker xuất sắc tháng {label}\"" };
            if (a.BonusAmount > 0 && a.BonusPaidAt is null)
            {
                await _wallet.ApplyAsync(a.TaskerId, a.BonusAmount, WalletTransactionType.Bonus, null,
                    $"Thưởng Tasker xuất sắc tháng {label} (hạng {a.Rank})");
                a.BonusPaidAt = now;
                _audit.Add(AuditService.TaskerBonus, a.BonusAmount, detail:
                    $"Thưởng Tasker xuất sắc tháng {label}: Tasker #{a.TaskerId}, hạng {a.Rank}");
                parts.Add($"{MoneyFormat.Vnd(a.BonusAmount)} tiền thưởng đã cộng vào ví");
            }
            if (a.PreferentialRate is { } rate)
            {
                a.PreferentialFrom = now;
                a.PreferentialTo = preferentialTo;
                parts.Add($"chiết khấu ưu đãi {rate * 100:0.##}% cho đơn nhận đến hết {VnTime.Format(preferentialTo.AddMinutes(-1))[..10]}");
            }
            if (a.HasGift) parts.Add($"quà tặng \"{a.GiftDescription}\" (công ty sẽ gửi email hẹn nhận quà)");
            _notifications.Add(a.TaskerId, $"Chúc mừng bạn là Tasker xuất sắc tháng {label}!",
                $"Bạn đạt hạng {a.Rank}. Phần thưởng: {string.Join("; ", parts)}.");
        }
        _audit.Add(AuditService.TaskerAwardApproved, awards.Sum(a => a.BonusAmount), detail:
            $"Duyệt Tasker xuất sắc tháng {label}: {awards.Count} Tasker, tổng thưởng {awards.Sum(a => a.BonusAmount)}");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetMonthAsync(year, month);
    }

    // ================= Quà hiện vật =================

    public async Task SendGiftEmailAsync(int id, GiftInvitationRequest request)
    {
        var a = await _db.TaskerMonthlyAwards.Include(x => x.Tasker).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw ApiException.NotFound();
        if (a.Status != TaskerAwardStatus.Approved) throw ApiException.BadRequest("Chỉ gửi email mời nhận quà sau khi đã duyệt.");
        if (!a.HasGift) throw ApiException.BadRequest("Tasker này không có quà hiện vật.");
        if (a.GiftDeliveredAt is not null) throw ApiException.BadRequest("Tasker đã nhận quà.");

        var label = MonthLabel(a.Year, a.Month);
        var invitation = $"Thời gian: {request.Time.Trim()} | Địa điểm: {request.Place.Trim()}"
                         + (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" | Ghi chú: {request.Note.Trim()}");
        var (html, text) = GiftEmail.Build(a.Tasker!.FullName, label, a.Rank, a.GiftDescription ?? "Quà tặng",
            request.Time.Trim(), request.Place.Trim(), request.Note?.Trim());
        try
        {
            await _email.SendAsync(a.Tasker.Email, a.Tasker.FullName, $"[HomeService] Mời nhận quà Tasker xuất sắc tháng {label}", html, text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi email mời nhận quà cho Tasker {Id} thất bại", a.TaskerId);
            throw ApiException.ServiceUnavailable("Không gửi được email mời nhận quà. Vui lòng thử lại sau ít phút.");
        }

        a.GiftEmailSentAt = DateTime.UtcNow;
        a.GiftInvitation = invitation.Length > 1000 ? invitation[..1000] : invitation;
        _notifications.Add(a.TaskerId, "Mời nhận quà Tasker xuất sắc tháng",
            $"Mời bạn đến nhận quà \"{a.GiftDescription}\". {invitation}. Chi tiết đã gửi qua email.");
        _audit.Add(AuditService.TaskerAwardGiftEmail, 0, detail: $"Gửi email mời nhận quà tháng {label} cho Tasker #{a.TaskerId}: {invitation}");
        await _db.SaveChangesAsync();
    }

    public async Task MarkGiftDeliveredAsync(int id)
    {
        var a = await FindAsync(id);
        if (a.Status != TaskerAwardStatus.Approved || !a.HasGift) throw ApiException.BadRequest("Tasker này không có quà hiện vật.");
        if (a.GiftDeliveredAt is not null) return;
        a.GiftDeliveredAt = DateTime.UtcNow;
        _audit.Add(AuditService.TaskerAwardGiftDelivered, 0, detail:
            $"Đã trao quà \"{a.GiftDescription}\" tháng {MonthLabel(a.Year, a.Month)} cho Tasker #{a.TaskerId}");
        await _db.SaveChangesAsync();
    }

    /// <summary>Dữ liệu xuất Excel (có email, SĐT – chỉ Admin); mỗi lần xuất ghi nhật ký kiểm toán (NFR-25).</summary>
    public async Task<TaskerAwardMonthResponse> ExportAsync(int year, int month)
    {
        var result = await GetMonthAsync(year, month);
        _audit.Add(AuditService.TaskerAwardExport, 0, detail:
            $"Xuất Excel Tasker xuất sắc tháng {MonthLabel(year, month)} ({result.Items.Count} dòng, có email và SĐT)");
        await _db.SaveChangesAsync();
        return result;
    }

    // ================= Dùng ở nơi khác =================

    /// <summary>Tỷ lệ chiết khấu ưu đãi đang hiệu lực của Tasker (mức thấp nhất), null nếu không có (BR-T04).</summary>
    public static async Task<(decimal Rate, DateTime Until)?> ActivePreferentialAsync(AppDbContext db, int taskerId, DateTime nowUtc)
    {
        var list = await db.TaskerMonthlyAwards.AsNoTracking()
            .Where(a => a.TaskerId == taskerId && a.Status == TaskerAwardStatus.Approved && a.PreferentialRate != null
                        && a.PreferentialFrom <= nowUtc && a.PreferentialTo > nowUtc)
            .Select(a => new { Rate = a.PreferentialRate!.Value, Until = a.PreferentialTo!.Value })
            .ToListAsync();
        if (list.Count == 0) return null;
        var best = list.OrderBy(x => x.Rate).ThenByDescending(x => x.Until).First();
        return (best.Rate, best.Until);
    }

    /// <summary>Huy hiệu đã duyệt của các Tasker, mới nhất trước.</summary>
    public static async Task<Dictionary<int, List<AwardBadgeResponse>>> BadgesAsync(AppDbContext db, IEnumerable<int> taskerIds)
    {
        var ids = taskerIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.TaskerMonthlyAwards.AsNoTracking()
            .Where(a => ids.Contains(a.TaskerId) && a.Status == TaskerAwardStatus.Approved)
            .Select(a => new { a.TaskerId, a.Year, a.Month, a.Rank })
            .ToListAsync();
        return rows.GroupBy(r => r.TaskerId).ToDictionary(g => g.Key, g => g
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .Select(r => new AwardBadgeResponse(r.Year, r.Month, r.Rank, $"Tasker xuất sắc tháng {MonthLabel(r.Year, r.Month)}"))
            .ToList());
    }
}

/// <summary>Nội dung email mời nhận quà (HTML + văn bản thuần).</summary>
public static class GiftEmail
{
    public static (string Html, string Text) Build(string name, string monthLabel, int rank, string gift, string time, string place, string? note)
    {
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var noteHtml = string.IsNullOrWhiteSpace(note) ? string.Empty : $"<p><b>Ghi chú:</b> {E(note)}</p>";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#1f2d3d">
              <h2 style="color:#0f9b8e">Chúc mừng {E(name)}!</h2>
              <p>Bạn là <b>Tasker xuất sắc tháng {E(monthLabel)}</b> của HomeService (hạng {rank}). Cảm ơn bạn đã luôn tận tâm với khách hàng.</p>
              <p>Công ty trân trọng mời bạn đến nhận quà <b>{E(gift)}</b>:</p>
              <p><b>Thời gian:</b> {E(time)}<br><b>Địa điểm:</b> {E(place)}</p>
              {noteHtml}
              <p>Vui lòng mang theo CCCD để đối chiếu khi nhận quà.</p>
              <p style="color:#6b7c8f;font-size:13px">Email được gửi tự động từ hệ thống HomeService.</p>
            </div>
            """;
        var text = $"Chúc mừng {name}! Bạn là Tasker xuất sắc tháng {monthLabel} của HomeService (hạng {rank}).\n" +
                   $"Mời bạn đến nhận quà \"{gift}\".\nThời gian: {time}\nĐịa điểm: {place}\n" +
                   (string.IsNullOrWhiteSpace(note) ? string.Empty : $"Ghi chú: {note}\n") +
                   "Vui lòng mang theo CCCD để đối chiếu khi nhận quà.";
        return (html, text);
    }
}
