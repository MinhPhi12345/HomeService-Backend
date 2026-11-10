using System.Security.Cryptography;
using System.Text;
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
/// US48 – yêu cầu mở khóa tài khoản (SRS v1.8, BR-A06):
/// người bị khóa xác thực email bằng OTP → nhận phiên 30 phút → tải tối đa 3 ảnh và gửi giải trình.
/// Mỗi tài khoản chỉ có 1 yêu cầu đang chờ; bị từ chối phải chờ AppealCooldownDays ngày; tối đa AppealMaxPerLock lần cho mỗi lần khóa.
/// </summary>
public class AppealService
{
    private readonly AppDbContext _db;
    private readonly OtpService _otp;
    private readonly AccountLockService _locks;
    private readonly AccountEmailService _emails;
    private readonly INotificationService _notifications;
    private readonly AuditService _audit;
    private readonly AccountOptions _opt;

    public AppealService(AppDbContext db, OtpService otp, AccountLockService locks, AccountEmailService emails,
        INotificationService notifications, AuditService audit, IOptions<AccountOptions> options)
    {
        _db = db;
        _otp = otp;
        _locks = locks;
        _emails = emails;
        _notifications = notifications;
        _audit = audit;
        _opt = options.Value;
    }

    // ---------- Người bị khóa (không cần đăng nhập) ----------

    /// <summary>Bước 1: gửi OTP. Luôn trả cùng kiểu kết quả để không lộ email nào đang bị khóa.</summary>
    public async Task<OtpSentResponse> SendOtpAsync(string email)
    {
        email = AuthService.NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.IsActive)
            return new OtpSentResponse(OtpService.MaskEmail(email), DateTime.UtcNow.AddMinutes(_otp.ExpiryMinutes), _otp.ResendSeconds);
        return await _otp.SendAsync(user, OtpPurpose.AccountAppeal);
    }

    /// <summary>Bước 2: đúng mã OTP → cấp token phiên (chỉ lưu bản băm) và trả thông tin khóa.</summary>
    public async Task<AppealSessionResponse> VerifyAsync(string email, string code)
    {
        email = AuthService.NormalizeEmail(email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsActive)
                   ?? throw ApiException.BadRequest("Mã OTP đã hết hạn hoặc chưa được gửi. Vui lòng bấm \"Gửi lại mã\".");
        await _otp.VerifyAsync(user.Id, OtpPurpose.AccountAppeal, code);

        var now = DateTime.UtcNow;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var session = new OtpCode
        {
            UserId = user.Id,
            Purpose = OtpPurpose.AppealSession,
            CodeHash = HashToken(token),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_opt.AppealSessionMinutes),
        };
        _db.OtpCodes.Add(session);
        await _db.SaveChangesAsync();
        return new AppealSessionResponse(token, session.ExpiresAt, await BuildContextAsync(user));
    }

    public async Task<AppealContextResponse> GetContextAsync(string? token)
    {
        var (user, _) = await ValidateSessionAsync(token);
        return await BuildContextAsync(user);
    }

    /// <summary>Tải ảnh đính kèm trong phiên đã xác thực.</summary>
    public async Task<string> UploadAsync(string? token, IFormFile? file, string uploadsRoot)
    {
        await ValidateSessionAsync(token);
        return await ImageStorage.SaveAsync(file, uploadsRoot);
    }

    /// <summary>Bước 3: gửi yêu cầu mở khóa.</summary>
    public async Task<AppealContextResponse> SubmitAsync(SubmitAppealRequest request)
    {
        var (user, session) = await ValidateSessionAsync(request.Token);
        var now = DateTime.UtcNow;
        var lockedAt = user.LockedAt ?? DateTime.MinValue;
        var state = await EligibilityAsync(user, now);
        if (!state.CanSubmit) throw ApiException.BadRequest(state.Message!);

        var urls = (request.AttachmentUrls ?? new List<string>())
            .Select(UploadUrl.Normalize).Where(u => !string.IsNullOrEmpty(u)).Select(u => u!).Distinct().ToList();
        if (urls.Count > _opt.AppealMaxAttachments)
            throw ApiException.BadRequest($"Tối đa {_opt.AppealMaxAttachments} ảnh đính kèm.");
        if (urls.Any(u => !ImageStorage.IsUploadedImage(u)))
            throw ApiException.BadRequest("Ảnh đính kèm không hợp lệ.");

        var appeal = new AccountAppeal
        {
            UserId = user.Id,
            LockedAt = lockedAt,
            Content = request.Content.Trim(),
            AttachmentUrls = string.Join(',', urls),
            CreatedAt = now,
        };
        _db.AccountAppeals.Add(appeal);
        session.UsedAt = now; // mỗi phiên chỉ gửi được 1 yêu cầu
        var attemptNo = state.Used + 1;
        _audit.Add(AuditService.AppealSubmitted, 0, null, null, $"Người dùng #{user.Id} ({user.Email}) gửi yêu cầu mở khóa lần {attemptNo}");
        await _notifications.AddForAdminsAsync("Yêu cầu mở khóa mới",
            $"{user.FullName} ({user.Email}) gửi yêu cầu mở khóa tài khoản (lần {attemptNo}/{_opt.AppealMaxPerLock}).");
        await _db.SaveChangesAsync();

        await _emails.SendSafeAsync(user, _emails.AppealReceived(user, now, attemptNo));
        return await BuildContextAsync(user);
    }

    // ---------- Admin ----------

    public async Task<List<AppealListItem>> ListAsync(AppealStatus? status)
    {
        var query = _db.AccountAppeals.AsNoTracking().Include(a => a.User).AsQueryable();
        if (status is not null) query = query.Where(a => a.Status == status);
        var appeals = await query
            .OrderBy(a => a.Status == AppealStatus.Pending ? 0 : 1)
            .ThenByDescending(a => a.CreatedAt)
            .Take(300)
            .ToListAsync();
        var userIds = appeals.Select(a => a.UserId).Distinct().ToList();
        var all = await _db.AccountAppeals.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId))
            .Select(a => new { a.Id, a.UserId, a.LockedAt, a.CreatedAt })
            .ToListAsync();
        return appeals.Select(a => new AppealListItem(
            a.Id, a.UserId, a.User!.FullName, a.User.Email, a.User.Role, a.User.LockReason, a.LockedAt,
            all.Count(x => x.UserId == a.UserId && x.LockedAt == a.LockedAt && x.CreatedAt <= a.CreatedAt),
            a.Status, a.Content.Length > 120 ? a.Content[..120] + "…" : a.Content, a.CreatedAt, a.ResolvedAt)).ToList();
    }

    public async Task<AppealDetailResponse> GetAsync(int id)
    {
        var a = await _db.AccountAppeals.AsNoTracking().Include(x => x.User).ThenInclude(u => u!.TaskerProfile)
                    .FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound();
        var u = a.User!;
        var others = await _db.AccountAppeals.AsNoTracking()
            .Where(x => x.UserId == u.Id && x.Id != a.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AppealHistoryItem(x.Id, x.Status, x.CreatedAt, x.ResolvedAt, x.AdminNote))
            .ToListAsync();
        var attemptNo = await _db.AccountAppeals.CountAsync(x => x.UserId == u.Id && x.LockedAt == a.LockedAt && x.CreatedAt <= a.CreatedAt);
        var names = await NamesAsync(new[] { u.LockedById, a.ResolvedById });

        var bookings = u.Role == UserRole.Tasker
            ? _db.Bookings.Where(b => b.TaskerId == u.Id)
            : _db.Bookings.Where(b => b.CustomerId == u.Id);
        var summary = new AppealUserSummary(
            u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive, u.CreatedAt,
            u.LockReason, u.LockDetail, u.LockedAt, u.LockedById is { } lb ? names.GetValueOrDefault(lb) : null,
            u.OutstandingAmount, u.IsCashBlocked, u.UnpaidReportCount,
            u.TaskerProfile?.ViolationPoints, u.TaskerProfile?.RatingAverage,
            await bookings.CountAsync(),
            await bookings.CountAsync(b => b.Status == BookingStatus.Paid),
            await bookings.CountAsync(b => b.Status == BookingStatus.Cancelled),
            await _db.Disputes.CountAsync(d => d.TaskerId == u.Id),
            await _db.Disputes.CountAsync(d => d.CustomerId == u.Id));

        return new AppealDetailResponse(a.Id, a.Status, a.Content, Dispute.SplitUrls(a.AttachmentUrls),
            a.CreatedAt, a.ResolvedAt, a.AdminNote, a.ResolvedById is { } rb ? names.GetValueOrDefault(rb) : null,
            attemptNo, _opt.AppealMaxPerLock, summary, others);
    }

    public async Task ApproveAsync(int id, int adminId, string? note)
    {
        var appeal = await LoadPendingAsync(id);
        var now = DateTime.UtcNow;
        appeal.Status = AppealStatus.Approved;
        appeal.AdminNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        appeal.ResolvedById = adminId;
        appeal.ResolvedAt = now;
        _audit.Add(AuditService.AppealApproved, 0, null, null, $"Chấp nhận yêu cầu mở khóa #{appeal.Id} của người dùng #{appeal.UserId}");
        await _db.SaveChangesAsync();
        await _locks.UnlockAsync(appeal.UserId, adminId, appeal.AdminNote, viaAppeal: true);
    }

    public async Task RejectAsync(int id, int adminId, string note)
    {
        var appeal = await LoadPendingAsync(id);
        var now = DateTime.UtcNow;
        appeal.Status = AppealStatus.Rejected;
        appeal.AdminNote = note.Trim();
        appeal.ResolvedById = adminId;
        appeal.ResolvedAt = now;
        _audit.Add(AuditService.AppealRejected, 0, null, null, $"Từ chối yêu cầu mở khóa #{appeal.Id} của người dùng #{appeal.UserId}: {appeal.AdminNote}");
        await _db.SaveChangesAsync();

        var user = appeal.User!;
        var used = await _db.AccountAppeals.CountAsync(a => a.UserId == user.Id && a.LockedAt == appeal.LockedAt);
        var remaining = Math.Max(0, _opt.AppealMaxPerLock - used);
        await _emails.SendSafeAsync(user, _emails.AppealRejected(user, appeal.AdminNote, now.AddDays(_opt.AppealCooldownDays), remaining));
    }

    // ---------- Nội bộ ----------

    private async Task<AccountAppeal> LoadPendingAsync(int id)
    {
        var appeal = await _db.AccountAppeals.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id)
                     ?? throw ApiException.NotFound();
        if (appeal.Status != AppealStatus.Pending) throw ApiException.BadRequest("Yêu cầu này đã được xử lý.");
        if (appeal.User!.IsActive) throw ApiException.BadRequest("Tài khoản đã được mở khóa.");
        return appeal;
    }

    private async Task<(User User, OtpCode Session)> ValidateSessionAsync(string? token)
    {
        const string expired = "Phiên gửi yêu cầu đã hết hạn, vui lòng xác thực email lại.";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) throw ApiException.BadRequest(expired);
        var now = DateTime.UtcNow;
        var hash = HashToken(token.Trim());
        var session = await _db.OtpCodes.Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Purpose == OtpPurpose.AppealSession && o.CodeHash == hash && o.UsedAt == null && o.ExpiresAt > now)
            ?? throw ApiException.BadRequest(expired);
        if (session.User is null || session.User.IsActive) throw ApiException.BadRequest("Tài khoản không bị khóa.");
        return (session.User, session);
    }

    private sealed record Eligibility(bool CanSubmit, string? Message, int Used, DateTime? NextAt);

    private async Task<Eligibility> EligibilityAsync(User user, DateTime now)
    {
        var lockedAt = user.LockedAt ?? DateTime.MinValue;
        var appeals = await _db.AccountAppeals.AsNoTracking()
            .Where(a => a.UserId == user.Id && a.LockedAt == lockedAt)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        var used = appeals.Count;
        var pending = appeals.FirstOrDefault(a => a.Status == AppealStatus.Pending);
        if (pending is not null)
            return new(false, $"Bạn đã có yêu cầu đang chờ xử lý (gửi lúc {VnTime.Format(pending.CreatedAt)}).", used, null);
        if (used >= _opt.AppealMaxPerLock)
            return new(false, $"Bạn đã gửi đủ {_opt.AppealMaxPerLock} yêu cầu cho lần khóa này.", used, null);
        var lastRejected = appeals.FirstOrDefault(a => a.Status == AppealStatus.Rejected && a.ResolvedAt != null);
        if (lastRejected is not null)
        {
            var next = lastRejected.ResolvedAt!.Value.AddDays(_opt.AppealCooldownDays);
            if (next > now) return new(false, $"Yêu cầu trước bị từ chối. Bạn có thể gửi lại từ {VnTime.Format(next)}.", used, next);
        }
        return new(true, null, used, null);
    }

    private async Task<AppealContextResponse> BuildContextAsync(User user)
    {
        var now = DateTime.UtcNow;
        var state = await EligibilityAsync(user, now);
        var lockedAt = user.LockedAt ?? DateTime.MinValue;
        var history = await _db.AccountAppeals.AsNoTracking()
            .Where(a => a.UserId == user.Id && a.LockedAt == lockedAt)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AppealHistoryItem(a.Id, a.Status, a.CreatedAt, a.ResolvedAt, a.AdminNote))
            .ToListAsync();
        return new AppealContextResponse(user.FullName, OtpService.MaskEmail(user.Email), user.LockReason,
            AccountLockReasonText.Of(user.LockReason), user.LockDetail, user.LockedAt,
            state.CanSubmit, state.Message, Math.Max(0, _opt.AppealMaxPerLock - state.Used), state.NextAt, history);
    }

    private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int?> ids)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return await _db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"HomeService-APPEAL|{token}")));
}
