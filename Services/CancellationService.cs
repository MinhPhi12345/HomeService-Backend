using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>Hủy đơn & hoàn tiền (FR-10, FR-24, FR-28).</summary>
public class CancellationService
{
    // BR-B04: khách chỉ hủy được ở các trạng thái này
    public static readonly BookingStatus[] CancellableStatuses =
    {
        BookingStatus.AwaitingPayment, BookingStatus.Pending, BookingStatus.Matched,
        BookingStatus.Accepted, BookingStatus.NoTaskerFound,
    };

    private readonly AppDbContext _db;
    private readonly SettlementService _settlement;
    private readonly WalletService _wallet;
    private readonly INotificationService _notifications;

    public CancellationService(AppDbContext db, SettlementService settlement, WalletService wallet,
        INotificationService notifications)
    {
        _db = db;
        _settlement = settlement;
        _wallet = wallet;
        _notifications = notifications;
    }

    /// <summary>Phí hủy nếu hủy ngay bây giờ (BR-C01). Sprint 4: hủy miễn phí; Sprint 5 (US14) bổ sung phí hủy muộn.</summary>
    public virtual Task<(decimal Fee, string Reason)> CalculateFeeAsync(Booking booking)
        => Task.FromResult((0m, "Hủy miễn phí."));

    /// <summary>Hủy đơn: đóng lời mời, hoàn tiền tạm giữ (trừ phí hủy nếu có), báo Tasker, giải phóng tạm giữ ví.</summary>
    public async Task CancelAsync(Booking booking, string reason, bool bySystem = false)
    {
        var now = DateTime.UtcNow;
        var (fee, feeReason) = await CalculateFeeAsync(booking);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var taskerId = booking.TaskerId;

        booking.Status = BookingStatus.Cancelled;
        booking.NextMatchAt = null;
        booking.UpdatedAt = now;

        await _db.BookingOffers
            .Where(o => o.BookingId == booking.Id && o.Status == OfferStatus.Sent)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Closed).SetProperty(o => o.RespondedAt, now));

        if (booking.PaymentMethod != PaymentMethod.Cash)
            await _settlement.RefundHeldAsync(booking, fee, fee > 0 ? feeReason : reason);

        if (taskerId is not null)
            _notifications.Add(taskerId.Value, "Đơn đã bị hủy",
                $"Đơn #{booking.Id} lúc {VnTime.Format(booking.ScheduledTime)} đã bị hủy. {reason}");
        if (bySystem)
            _notifications.Add(booking.CustomerId, "Đơn đã bị hủy", $"Đơn #{booking.Id} đã bị hủy. {reason}");

        await _db.SaveChangesAsync();
        if (taskerId is not null && booking.PaymentMethod == PaymentMethod.Cash)
        {
            await _wallet.RecalculateReservedAsync(taskerId.Value); // BR-P05: hủy → giải phóng phần giữ
            await _db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }
}
