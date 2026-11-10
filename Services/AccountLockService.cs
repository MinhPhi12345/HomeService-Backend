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
/// US30 (SRS v1.8) – khóa / mở khóa tài khoản an toàn khi người dùng còn đơn đang dở (FR-22, BR-A03, BR-A05).
/// <list type="bullet">
/// <item>Khóa sau khi hoàn tất đơn (mặc định): không nhận / đặt đơn mới, tự khóa khi đơn đang dở cuối cùng kết thúc.</item>
/// <item>Khóa ngay: Tasker – đơn đã nhận được gỡ Tasker và tìm Tasker khác, đơn online đang làm dở tự mở khiếu nại,
/// đơn tiền mặt đang làm dở bị hủy không phí; Khách hàng – đơn chưa bắt đầu bị hủy không phí, hoàn 100%.</item>
/// </list>
/// </summary>
public class AccountLockService
{
    // Đơn "đang dở" theo vai trò
    public static readonly BookingStatus[] TaskerActiveStatuses = { BookingStatus.Accepted, BookingStatus.InProgress };
    public static readonly BookingStatus[] CustomerActiveStatuses =
    {
        BookingStatus.AwaitingPayment, BookingStatus.Pending, BookingStatus.Matched,
        BookingStatus.Accepted, BookingStatus.InProgress, BookingStatus.NoTaskerFound,
    };

    private readonly AppDbContext _db;
    private readonly CancellationService _cancellation;
    private readonly WalletService _wallet;
    private readonly INotificationService _notifications;
    private readonly AuditService _audit;
    private readonly AccountEmailService _emails;
    private readonly BusinessOptions _business;
    private readonly ILogger<AccountLockService> _logger;

    public AccountLockService(AppDbContext db, CancellationService cancellation, WalletService wallet,
        INotificationService notifications, AuditService audit, AccountEmailService emails,
        IOptions<BusinessOptions> business, ILogger<AccountLockService> logger)
    {
        _db = db;
        _cancellation = cancellation;
        _wallet = wallet;
        _notifications = notifications;
        _audit = audit;
        _emails = emails;
        _business = business.Value;
        _logger = logger;
    }

    public async Task<List<Booking>> ActiveBookingsAsync(User u) => u.Role switch
    {
        UserRole.Tasker => await _db.Bookings.Include(b => b.ServiceCategory).Include(b => b.Payment)
            .Where(b => b.TaskerId == u.Id && TaskerActiveStatuses.Contains(b.Status))
            .OrderBy(b => b.ScheduledTime).ToListAsync(),
        UserRole.Customer => await _db.Bookings.Include(b => b.ServiceCategory).Include(b => b.Payment)
            .Where(b => b.CustomerId == u.Id && CustomerActiveStatuses.Contains(b.Status))
            .OrderBy(b => b.ScheduledTime).ToListAsync(),
        _ => new List<Booking>(),
    };

    /// <summary>Hộp thoại khóa: danh sách đơn đang dở + bản xem trước email.</summary>
    public async Task<LockPreviewResponse> PreviewAsync(int userId, int adminId, LockAccountRequest request)
    {
        var user = await LoadTargetAsync(userId, adminId);
        var active = await ActiveBookingsAsync(user);
        var items = active.Select(b => new ActiveOrderItem(b.Id, b.ServiceCategory?.Name ?? string.Empty, b.Status,
            b.ScheduledTime, b.PaymentMethod, ImmediateActionText(user, b))).ToList();

        var reason = request.Reason ?? AccountLockReason.Other;
        var detail = string.IsNullOrWhiteSpace(request.Detail) ? "(chi tiết lý do)" : request.Detail;
        var pending = request.Mode == AccountLockMode.AfterOrders && active.Count > 0;
        var mail = pending
            ? _emails.LockScheduled(user, reason, detail, active.Count, user.Role == UserRole.Tasker)
            : _emails.Locked(user, reason, detail, DateTime.UtcNow,
                request.Mode == AccountLockMode.Immediate ? items.Select(i => $"Đơn #{i.BookingId}: {i.ImmediateAction}").ToList() : null);
        return new LockPreviewResponse(items, mail.Subject, mail.Text);
    }

    public async Task<LockResultResponse> LockAsync(int userId, int adminId, LockAccountRequest request)
    {
        var user = await LoadTargetAsync(userId, adminId);
        if (!user.IsActive) throw ApiException.BadRequest("Tài khoản đã bị khóa.");

        var now = DateTime.UtcNow;
        var reason = request.Reason ?? AccountLockReason.Other;
        var detail = request.Detail.Trim();
        var active = await ActiveBookingsAsync(user);

        user.LockReason = reason;
        user.LockDetail = detail;
        user.LockedById = adminId;
        if (user.TaskerProfile is not null) user.TaskerProfile.IsAvailable = false; // ngừng nhận lời mời ngay

        // Khóa sau khi hoàn tất đơn (chỉ khi thật sự còn đơn đang dở)
        if (request.Mode == AccountLockMode.AfterOrders && active.Count > 0)
        {
            user.LockPendingAt = now;
            _audit.Add(AuditService.AccountLockScheduled, 0, null, null,
                $"Chờ khóa {Who(user)} sau {active.Count} đơn đang dở – {AccountLockReasonText.Of(reason)}: {detail}");
            await _db.SaveChangesAsync();
            await _emails.SendSafeAsync(user, _emails.LockScheduled(user, reason, detail, active.Count, user.Role == UserRole.Tasker));
            return new LockResultResponse("Pending", active.Count,
                active.Select(b => $"Đơn #{b.Id}: tiếp tục thực hiện, khóa có hiệu lực khi đơn kết thúc.").ToList());
        }

        // Khóa ngay
        user.IsActive = false;
        user.LockedAt = now;
        user.LockPendingAt = null;
        _audit.Add(AuditService.AccountLocked, 0, null, null,
            $"Khóa {Who(user)} – {AccountLockReasonText.Of(reason)}: {detail}");
        await _db.SaveChangesAsync();

        var actions = new List<string>();
        foreach (var b in active)
        {
            try
            {
                actions.Add($"Đơn #{b.Id}: {await HandleImmediateAsync(user, b, now)}");
            }
            catch (DbUpdateConcurrencyException)
            {
                actions.Add($"Đơn #{b.Id}: vừa được cập nhật bởi thao tác khác, vui lòng kiểm tra lại.");
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        var fresh = await _db.Users.FirstAsync(u => u.Id == user.Id);
        await _emails.SendSafeAsync(fresh, _emails.Locked(fresh, reason, detail, now, actions));
        return new LockResultResponse("Locked", active.Count, actions);
    }

    /// <summary>Mở khóa / hủy lệnh chờ khóa. viaAppeal = true khi chấp nhận yêu cầu mở khóa (US48).</summary>
    public async Task UnlockAsync(int userId, int? adminId, string? note, bool viaAppeal = false)
    {
        var user = await _db.Users.Include(u => u.TaskerProfile).FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw ApiException.NotFound();
        var wasPending = user.IsActive && user.LockPendingAt is not null;
        if (user.IsActive && !wasPending) throw ApiException.BadRequest("Tài khoản không bị khóa.");

        var now = DateTime.UtcNow;
        user.IsActive = true;
        user.LockPendingAt = null;
        user.LockReason = null;
        user.LockDetail = null;
        user.LockedAt = null;
        user.LockedById = null;

        // Yêu cầu mở khóa đang chờ của lần khóa này được đóng lại
        if (!viaAppeal)
        {
            var pendingAppeals = await _db.AccountAppeals
                .Where(a => a.UserId == userId && a.Status == AppealStatus.Pending).ToListAsync();
            foreach (var a in pendingAppeals)
            {
                a.Status = AppealStatus.Approved;
                a.AdminNote = "Tài khoản đã được quản trị viên mở khóa trực tiếp.";
                a.ResolvedById = adminId;
                a.ResolvedAt = now;
            }
        }

        _audit.Add(wasPending ? AuditService.AccountLockCancelled : AuditService.AccountUnlocked, 0, null, null,
            $"{(wasPending ? "Hủy lệnh chờ khóa" : "Mở khóa")} {Who(user)}" +
            (string.IsNullOrWhiteSpace(note) ? string.Empty : $": {note.Trim()}"));
        await _db.SaveChangesAsync();
        await _emails.SendSafeAsync(user, _emails.Unlocked(user, note, viaAppeal, wasPending));
    }

    /// <summary>
    /// Tác vụ nền: tài khoản "chờ khóa" không còn đơn đang dở → khóa có hiệu lực và gửi email.
    /// Idempotent, chạy mỗi lượt BookingWorker. Trả về số tài khoản đã khóa.
    /// </summary>
    public async Task<int> FinalizePendingLocksAsync(CancellationToken ct = default)
    {
        var pending = await _db.Users
            .Where(u => u.IsActive && u.LockPendingAt != null)
            .Select(u => u.Id)
            .Take(50)
            .ToListAsync(ct);

        var locked = 0;
        foreach (var id in pending)
        {
            try
            {
                var user = await _db.Users.Include(u => u.TaskerProfile).FirstAsync(u => u.Id == id, ct);
                if (!user.IsActive || user.LockPendingAt is null) continue;
                var stillActive = user.Role == UserRole.Tasker
                    ? await _db.Bookings.AnyAsync(b => b.TaskerId == id && TaskerActiveStatuses.Contains(b.Status), ct)
                    : await _db.Bookings.AnyAsync(b => b.CustomerId == id && CustomerActiveStatuses.Contains(b.Status), ct);
                if (stillActive) continue;

                var now = DateTime.UtcNow;
                user.IsActive = false;
                user.LockedAt = now;
                user.LockPendingAt = null;
                if (user.TaskerProfile is not null) user.TaskerProfile.IsAvailable = false;
                _audit.Add(AuditService.AccountLocked, 0, null, null,
                    $"Khóa có hiệu lực (các đơn đang dở đã kết thúc) {Who(user)} – {AccountLockReasonText.Of(user.LockReason)}: {user.LockDetail}");
                await _db.SaveChangesAsync(ct);
                locked++;
                await _emails.SendSafeAsync(user, _emails.Locked(user, user.LockReason ?? AccountLockReason.Other,
                    user.LockDetail ?? string.Empty, now));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Khóa tài khoản chờ #{UserId} thất bại", id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return locked;
    }

    private async Task<User> LoadTargetAsync(int userId, int adminId)
    {
        var user = await _db.Users.Include(u => u.TaskerProfile).FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw ApiException.NotFound();
        if (user.Id == adminId) throw ApiException.BadRequest("Không thể tự khóa tài khoản của chính mình.");
        return user;
    }

    /// <summary>Mô tả cách đơn được xử lý nếu khóa ngay (hiện trong hộp thoại & email).</summary>
    public static string ImmediateActionText(User user, Booking b)
    {
        if (user.Role == UserRole.Tasker)
        {
            if (b.Status == BookingStatus.Accepted)
                return "gỡ Tasker, hệ thống tìm Tasker khác cho khách (khách được hủy miễn phí).";
            return b.PaymentMethod == PaymentMethod.Cash
                ? "đơn tiền mặt đang làm dở bị hủy, khách không phải trả tiền."
                : "tự mở khiếu nại để quản trị viên quyết định hoàn tiền cho khách.";
        }
        return b.Status == BookingStatus.InProgress
            ? "Tasker đang làm – tiếp tục thực hiện đến khi hoàn thành."
            : "hủy đơn không tính phí" + (b.Payment is { Status: PaymentStatus.Held } ? ", hoàn 100% số tiền đã trả." : ".");
    }

    private async Task<string> HandleImmediateAsync(User user, Booking snapshot, DateTime now)
    {
        var booking = await _db.Bookings.Include(b => b.Payment).Include(b => b.ServiceCategory)
            .FirstAsync(b => b.Id == snapshot.Id);
        var text = ImmediateActionText(user, booking);

        if (user.Role == UserRole.Customer)
        {
            if (booking.Status == BookingStatus.InProgress) return text;
            await _cancellation.CancelAsync(booking, "Tài khoản khách hàng của đơn đã bị khóa – đơn được hủy, không tính phí.",
                bySystem: false, waiveFee: true);
            return text;
        }

        // Tasker
        if (booking.Status == BookingStatus.Accepted)
        {
            // Gỡ Tasker → vòng ghép nối mới, không mời lại Tasker bị khóa (đã có lời mời Accepted)
            var rows = await _db.Bookings
                .Where(b => b.Id == booking.Id && b.Status == BookingStatus.Accepted && b.TaskerId == user.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.TaskerId, (int?)null)
                    .SetProperty(b => b.Status, BookingStatus.Pending)
                    .SetProperty(b => b.MatchRound, b => b.MatchRound + 1)
                    .SetProperty(b => b.CurrentWave, 0)
                    .SetProperty(b => b.NextMatchAt, (DateTime?)null)
                    .SetProperty(b => b.SearchExhaustedAt, (DateTime?)null)
                    .SetProperty(b => b.ManualSearchAvailableAt, (DateTime?)null)
                    .SetProperty(b => b.CommissionRate, _business.CommissionRate)
                    .SetProperty(b => b.IsTaskerNoShow, false)
                    .SetProperty(b => b.NoShowAt, (DateTime?)null)
                    .SetProperty(b => b.ReminderSentAt, (DateTime?)null)
                    .SetProperty(b => b.UpdatedAt, now));
            if (rows == 0) return "đơn vừa thay đổi trạng thái, không cần xử lý.";
            _notifications.Add(booking.CustomerId, "Đang tìm Tasker khác cho bạn",
                $"Tasker không thể tiếp tục thực hiện đơn #{booking.Id} {booking.ServiceCategory?.Name}. Hệ thống đang tìm Tasker khác; " +
                "nếu không muốn chờ, bạn có thể hủy đơn miễn phí.");
            await _db.SaveChangesAsync();
            if (booking.PaymentMethod == PaymentMethod.Cash)
            {
                await _wallet.RecalculateReservedAsync(user.Id);
                await _db.SaveChangesAsync();
            }
            return text;
        }

        // InProgress
        if (booking.PaymentMethod == PaymentMethod.Cash || booking.Payment is null)
        {
            await _cancellation.CancelAsync(booking, "Tasker của đơn đã bị khóa tài khoản khi đang thực hiện – đơn được hủy, bạn không phải trả tiền.",
                bySystem: true, waiveFee: true);
            return text;
        }

        // Đơn online đang làm dở → hệ thống tự mở khiếu nại, tiền tiếp tục tạm giữ chờ Admin quyết định
        var dispute = new Dispute
        {
            BookingId = booking.Id,
            CustomerId = booking.CustomerId,
            TaskerId = user.Id,
            Reason = DisputeReason.TaskerLocked,
            Description = $"Hệ thống tự mở khiếu nại: Tasker bị khóa tài khoản khi đơn đang thực hiện ({AccountLockReasonText.Of(user.LockReason)}).",
            EvidenceUrls = string.Empty,
            CreatedAt = now,
        };
        _db.Disputes.Add(dispute);
        booking.Status = BookingStatus.Disputed;
        booking.UpdatedAt = now;
        _notifications.Add(booking.CustomerId, "Đơn đang được quản trị viên xử lý",
            $"Tasker của đơn #{booking.Id} không thể tiếp tục công việc. Quản trị viên sẽ xem xét và hoàn tiền phù hợp cho bạn.");
        await _notifications.AddForAdminsAsync("Khiếu nại do khóa Tasker",
            $"Đơn #{booking.Id} đang thực hiện thì Tasker bị khóa – cần quyết định hoàn tiền cho khách.");
        await _db.SaveChangesAsync();
        return text;
    }

    private static string Who(User u) => $"#{u.Id} {u.FullName} ({u.Email})";
}
