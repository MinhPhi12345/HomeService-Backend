using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>US17 – Tasker nhận / từ chối lời mời nhận đơn (FR-13). Người nhận đầu tiên thắng (BR-M04).</summary>
public class OfferService
{
    private readonly AppDbContext _db;
    private readonly WalletService _wallet;
    private readonly INotificationService _notifications;
    private readonly MatchingOptions _matching;

    public OfferService(AppDbContext db, WalletService wallet, INotificationService notifications, IOptions<MatchingOptions> matching)
    {
        _db = db;
        _wallet = wallet;
        _notifications = notifications;
        _matching = matching.Value;
    }

    public async Task AcceptAsync(int bookingId, int taskerId)
    {
        var now = DateTime.UtcNow;
        var booking = await _db.Bookings.Include(b => b.ServiceCategory).FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == taskerId);
        if (profile is null || profile.VerificationStatus != TaskerVerificationStatus.Approved)
            throw ApiException.BadRequest("Hồ sơ Tasker chưa được duyệt."); // BR-A04

        var offer = await GetOfferAsync(booking.Id, booking.MatchRound, taskerId);

        if (booking.TaskerId != null || (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Matched))
        {
            if (booking.TaskerId != null && booking.TaskerId != taskerId)
                throw ApiException.Conflict("Đơn đã được Tasker khác nhận.");
            throw ApiException.Conflict("Đơn không còn nhận Tasker.");
        }

        // BR-B02: không nhận 2 đơn cách nhau dưới 2 giờ
        var gapFrom = booking.ScheduledTime.AddHours(-_matching.ScheduleGapHours);
        var gapTo = booking.ScheduledTime.AddHours(_matching.ScheduleGapHours);
        var conflict = await _db.Bookings.AnyAsync(b => b.TaskerId == taskerId && b.Id != booking.Id
            && (b.Status == BookingStatus.Accepted || b.Status == BookingStatus.InProgress)
            && b.ScheduledTime > gapFrom && b.ScheduledTime < gapTo);
        if (conflict) throw ApiException.Conflict("Bạn đã có đơn khác trùng lịch (cách nhau dưới 2 giờ).");

        // BR-P04/BR-P05: đơn tiền mặt cần đủ số dư khả dụng để tạm giữ chiết khấu
        var commission = Money.Commission(booking.TotalPrice, booking.CommissionRate);
        if (booking.PaymentMethod == PaymentMethod.Cash && profile.AvailableBalance < commission)
            throw ApiException.BadRequest("Số dư ví không đủ để nhận đơn tiền mặt này, vui lòng nạp thêm.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Chốt người nhận NGUYÊN TỬ: chỉ 1 Tasker cập nhật được đơn còn Pending/Matched và chưa có taskerId (NFR-13)
        var won = await _db.Bookings
            .Where(b => b.Id == bookingId && b.TaskerId == null &&
                        (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Matched))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.TaskerId, taskerId)
                .SetProperty(b => b.Status, BookingStatus.Accepted)
                .SetProperty(b => b.NextMatchAt, (DateTime?)null)
                .SetProperty(b => b.UpdatedAt, now));
        if (won == 0)
            throw ApiException.Conflict("Đơn đã được Tasker khác nhận.");

        await _db.BookingOffers.Where(o => o.Id == offer.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Accepted).SetProperty(o => o.RespondedAt, now));
        await _db.BookingOffers.Where(o => o.BookingId == bookingId && o.Id != offer.Id && o.Status == OfferStatus.Sent)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Closed).SetProperty(o => o.RespondedAt, now));

        if (booking.PaymentMethod == PaymentMethod.Cash)
            await _wallet.RecalculateReservedAsync(taskerId); // tạm giữ chiết khấu ước tính

        var taskerName = await _db.Users.Where(u => u.Id == taskerId).Select(u => u.FullName).FirstAsync();
        _notifications.Add(booking.CustomerId, "Đã có Tasker nhận đơn",
            $"{taskerName} đã nhận đơn {booking.ServiceCategory!.Name} lúc {VnTime.Format(booking.ScheduledTime)}.");

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RejectAsync(int bookingId, int taskerId)
    {
        var booking = await _db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        var profile = await _db.TaskerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == taskerId);
        if (profile is null || profile.VerificationStatus != TaskerVerificationStatus.Approved)
            throw ApiException.BadRequest("Hồ sơ Tasker chưa được duyệt.");

        var offer = await GetOfferAsync(booking.Id, booking.MatchRound, taskerId);
        if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Matched)
            throw ApiException.Conflict("Đơn không còn nhận Tasker.");

        // Chỉ lời mời của Tasker này chuyển Rejected; đơn vẫn ở Matched cho các Tasker khác.
        // Nếu mọi Tasker trong đợt đều từ chối, BookingWorker chuyển sang đợt kế ngay ở lượt chạy tới.
        var rows = await _db.BookingOffers
            .Where(o => o.Id == offer.Id && o.Status == OfferStatus.Sent)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Rejected)
                                      .SetProperty(o => o.RespondedAt, DateTime.UtcNow));
        if (rows == 0) throw ApiException.Conflict("Lời mời đã hết hạn.");
    }

    /// <summary>Lời mời Sent còn hạn của Tasker cho đơn (vòng hiện tại). Không có → 403; hết hạn/đóng → 409.</summary>
    private async Task<Models.BookingOffer> GetOfferAsync(int bookingId, int round, int taskerId)
    {
        var offer = await _db.BookingOffers.AsNoTracking()
            .Where(o => o.BookingId == bookingId && o.TaskerId == taskerId && o.Round == round)
            .FirstOrDefaultAsync();
        if (offer is null) throw ApiException.Forbidden();

        return offer.Status switch
        {
            OfferStatus.Sent when offer.ExpiresAt > DateTime.UtcNow => offer,
            OfferStatus.Sent or OfferStatus.Expired => throw ApiException.Conflict("Lời mời đã hết hạn."),
            OfferStatus.Closed => throw ApiException.Conflict("Đơn đã được Tasker khác nhận."),
            OfferStatus.Accepted => throw ApiException.Conflict("Bạn đã nhận đơn này."),
            _ => throw ApiException.Conflict("Bạn đã từ chối lời mời này."),
        };
    }
}
