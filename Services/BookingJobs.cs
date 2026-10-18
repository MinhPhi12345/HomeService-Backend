using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// Các tác vụ tự động theo thời hạn (NFR-17: sai lệch ≤ 1 phút, chạy lại an toàn – idempotent).
/// Được BookingWorker gọi mỗi lượt.
/// </summary>
public class BookingJobs
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly SettlementService _settlement;
    private readonly SettlementOptions _opt;
    private readonly ILogger<BookingJobs> _logger;
    private readonly CancellationService _cancellation;

    public BookingJobs(AppDbContext db, INotificationService notifications, SettlementService settlement,
        IOptions<SettlementOptions> options, ILogger<BookingJobs> logger, CancellationService cancellation)
    {
        _cancellation = cancellation;
        _db = db;
        _notifications = notifications;
        _settlement = settlement;
        _opt = options.Value;
        _logger = logger;
    }

    /// <summary>US21 / BR-P08 – đơn online quá 15 phút chưa thanh toán → tự hủy.</summary>
    public async Task ExpireUnpaidBookingsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.Bookings
            .Where(b => b.Status == BookingStatus.AwaitingPayment && b.PaymentDueAt != null && b.PaymentDueAt <= now)
            .Select(b => new { b.Id, b.CustomerId })
            .Take(100)
            .ToListAsync(ct);

        foreach (var b in expired)
        {
            var rows = await _db.Bookings
                .Where(x => x.Id == b.Id && x.Status == BookingStatus.AwaitingPayment)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, BookingStatus.Cancelled)
                                          .SetProperty(x => x.UpdatedAt, now), ct);
            if (rows == 0) continue;
            _notifications.Add(b.CustomerId, "Đơn đã bị hủy",
                $"Đơn #{b.Id} đã tự hủy do quá hạn thanh toán {_opt.PaymentTimeoutMinutes} phút.");
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>US34 – nhắc khách trước hạn tự xác nhận 2 giờ; hết hạn mà không khiếu nại → tự giải ngân.</summary>
    public async Task AutoConfirmAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Nhắc trước hạn tự xác nhận
        var remindBefore = now.AddHours(_opt.ReminderHoursBefore);
        var toRemind = await _db.Bookings
            .Where(b => b.Status == BookingStatus.Completed && b.AutoConfirmAt != null
                        && b.AutoConfirmAt <= remindBefore && b.AutoConfirmAt > now
                        && b.AutoConfirmReminderSentAt == null)
            .Take(100)
            .ToListAsync(ct);
        foreach (var b in toRemind)
        {
            b.AutoConfirmReminderSentAt = now;
            _notifications.Add(b.CustomerId, "Sắp tự xác nhận hoàn thành",
                $"Đơn #{b.Id} sẽ được tự xác nhận và chuyển tiền cho Tasker lúc {VnTime.Format(b.AutoConfirmAt!.Value)}. " +
                "Nếu công việc chưa đạt, hãy khiếu nại trước thời điểm này.");
        }
        if (toRemind.Count > 0) await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();

        // Tự xác nhận & giải ngân (mỗi đơn 1 transaction; Payment.Status là concurrency token → không giải ngân 2 lần)
        var dueIds = await _db.Bookings
            .Where(b => b.Status == BookingStatus.Completed && b.AutoConfirmAt != null && b.AutoConfirmAt <= now)
            .Select(b => b.Id)
            .Take(100)
            .ToListAsync(ct);
        foreach (var id in dueIds)
        {
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var booking = await _db.Bookings.Include(b => b.Payment).FirstAsync(b => b.Id == id, ct);
                if (booking.Status != BookingStatus.Completed) continue;
                await _settlement.ReleaseOnlineAsync(booking, isAuto: true);
                booking.Status = BookingStatus.Paid;
                booking.IsAutoConfirmed = true;
                _notifications.Add(booking.CustomerId, "Đơn đã tự xác nhận hoàn thành",
                    $"Đơn #{booking.Id} đã được hệ thống tự xác nhận sau {_opt.AutoConfirmHours} giờ.");
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation("Đơn {Id} đã được xử lý bởi thao tác khác, bỏ qua", id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>
    /// US33 / BR-M05 – đơn "Chưa có Tasker nhận" quá 24 giờ khách không xử lý, hoặc đã quá giờ hẹn → 9 Expired,
    /// đơn online được hoàn 100%.
    /// </summary>
    public async Task ExpireNoTaskerFoundAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-24);
        var ids = await _db.Bookings
            .Where(b => b.Status == BookingStatus.NoTaskerFound
                        && ((b.UpdatedAt != null && b.UpdatedAt <= cutoff) || b.ScheduledTime <= now))
            .Select(b => b.Id)
            .Take(100)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                var booking = await _db.Bookings.Include(b => b.Payment).FirstAsync(b => b.Id == id, ct);
                if (booking.Status != BookingStatus.NoTaskerFound) continue;
                booking.Status = BookingStatus.Expired;
                booking.UpdatedAt = now;
                await _settlement.RefundHeldAsync(booking, 0, "Đơn đã hết hạn vì chưa có Tasker nhận.");
                _notifications.Add(booking.CustomerId, "Đơn đã hết hạn",
                    $"Đơn #{booking.Id} đã hết hạn vì chưa có Tasker nhận. Bạn có thể tạo đơn mới.");
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation("Đơn {Id} đã được xử lý bởi thao tác khác, bỏ qua", id);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
    }
}
