using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly AuditService _audit;
    private readonly SettlementOptions _opt;

    public CancellationService(AppDbContext db, SettlementService settlement, WalletService wallet,
        INotificationService notifications, AuditService audit, IOptions<SettlementOptions> options)
    {
        _db = db;
        _settlement = settlement;
        _wallet = wallet;
        _notifications = notifications;
        _audit = audit;
        _opt = options.Value;
    }

    /// <summary>
    /// US14 – Phí hủy nếu hủy ngay bây giờ (BR-C01):
    /// chưa có Tasker nhận hoặc còn ≥ 2 giờ → 0 đ; đã có Tasker và còn &lt; 2 giờ → 20%;
    /// Tasker đã đến (vị trí cách địa chỉ ≤ 200 m) → 50%; Tasker không đến → 0 đ.
    /// </summary>
    public async Task<(decimal Fee, string Reason)> CalculateFeeAsync(Booking booking)
    {
        if (booking.Status != BookingStatus.Accepted || booking.TaskerId is null)
            return (0m, "Chưa có Tasker nhận đơn – hủy miễn phí.");
        if (IsTaskerNoShow(booking))
            return (0m, "Tasker không đến đúng hẹn – hủy miễn phí, hoàn 100%.");

        var now = DateTime.UtcNow;
        if (await HasTaskerArrivedAsync(booking, now))
            return (Money.Round(booking.TotalPrice * _opt.ArrivedCancelFeePercent / 100m),
                $"Tasker đã đến nơi – phí hủy {_opt.ArrivedCancelFeePercent:0}%.");
        if (booking.ScheduledTime - now < TimeSpan.FromHours(_opt.LateCancelHours))
            return (Money.Round(booking.TotalPrice * _opt.LateCancelFeePercent / 100m),
                $"Hủy khi còn dưới {_opt.LateCancelHours:0} giờ đến giờ hẹn – phí {_opt.LateCancelFeePercent:0}%.");
        return (0m, $"Hủy trước giờ hẹn từ {_opt.LateCancelHours:0} giờ trở lên – miễn phí.");
    }

    /// <summary>US37 – đơn đã bị đánh dấu Tasker không đến → hủy miễn phí, hoàn 100%.</summary>
    private static bool IsTaskerNoShow(Booking booking) => booking.IsTaskerNoShow;

    private async Task<bool> HasTaskerArrivedAsync(Booking booking, DateTime now)
    {
        if (booking.Latitude is null || booking.Longitude is null) return false;
        var p = await _db.TaskerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == booking.TaskerId);
        if (p?.LastLatitude is null || p.LastLongitude is null || p.LastLocationAt is null) return false;
        if (p.LastLocationAt < now.AddMinutes(-_opt.ArrivedLocationFreshMinutes)) return false;
        var meters = GeoHelper.DistanceKm(p.LastLatitude.Value, p.LastLongitude.Value,
            booking.Latitude.Value, booking.Longitude.Value) * 1000;
        return meters <= _opt.ArrivedDistanceMeters;
    }

    /// <summary>Số tiền khách được hoàn nếu hủy bây giờ (0 với đơn tiền mặt / chưa thanh toán).</summary>
    public static decimal RefundIfCancelled(Booking booking, decimal fee) =>
        booking.Payment is { Status: PaymentStatus.Held } p ? p.Amount - Math.Min(fee, p.Amount) : 0m;

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
        booking.CancellationFee = fee;

        await _db.BookingOffers
            .Where(o => o.BookingId == booking.Id && o.Status == OfferStatus.Sent)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Closed).SetProperty(o => o.RespondedAt, now));

        if (booking.PaymentMethod != PaymentMethod.Cash)
        {
            await _settlement.RefundHeldAsync(booking, fee, fee > 0 ? feeReason : reason);
        }
        else if (fee > 0)
        {
            // Đơn tiền mặt có phí hủy: ghi vào khoản nợ của khách (FR-10, FR-27), trả nợ online để chuyển cho Tasker
            var customer = await _db.Users.FirstAsync(u => u.Id == booking.CustomerId);
            customer.OutstandingAmount += fee;
            _audit.Add(AuditService.Debt, fee, booking.Id, null, $"Ghi nợ phí hủy đơn tiền mặt: {feeReason}");
            _notifications.Add(booking.CustomerId, "Phát sinh phí hủy",
                $"Đơn #{booking.Id}: phí hủy {MoneyFormat.Vnd(fee)} đã được ghi vào khoản cần thanh toán của bạn. {feeReason}");
        }

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
