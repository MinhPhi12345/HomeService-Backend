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

/// <summary>US35 – Khiếu nại và xử lý tranh chấp (FR-26, BR-D01, BR-D02).</summary>
public class DisputeService
{
    private readonly AppDbContext _db;
    private readonly SettlementService _settlement;
    private readonly TaskerDisciplineService _discipline;
    private readonly INotificationService _notifications;
    private readonly AuditService _audit;
    private readonly IBookingService _bookings;
    private readonly SettlementOptions _opt;

    public DisputeService(AppDbContext db, SettlementService settlement, TaskerDisciplineService discipline,
        INotificationService notifications, AuditService audit, IBookingService bookings, IOptions<SettlementOptions> options)
    {
        _db = db;
        _settlement = settlement;
        _discipline = discipline;
        _notifications = notifications;
        _audit = audit;
        _bookings = bookings;
        _opt = options.Value;
    }

    /// <summary>Còn được khiếu nại? (dùng cho cả canDispute trong BookingResponse)</summary>
    public static bool CanDispute(Booking b, DateTime now, int disputeHours)
    {
        if (b.Dispute is not null || b.CompletedAt is null) return false;
        if (now > b.CompletedAt.Value.AddHours(disputeHours)) return false;
        var onlineWaiting = b.Status == BookingStatus.Completed && b.PaymentMethod != PaymentMethod.Cash;
        var cashPaid = b.Status == BookingStatus.Paid && b.PaymentMethod == PaymentMethod.Cash;
        return onlineWaiting || cashPaid;
    }

    public async Task<Dispute> CreateAsync(int bookingId, int customerId, CreateDisputeRequest request)
    {
        var now = DateTime.UtcNow;
        var booking = await _db.Bookings.Include(b => b.Dispute).FirstOrDefaultAsync(b => b.Id == bookingId)
                      ?? throw ApiException.NotFound();
        if (booking.CustomerId != customerId) throw ApiException.Forbidden();
        if (booking.Dispute is not null) throw ApiException.Conflict("Mỗi đơn chỉ được khiếu nại 1 lần.");
        if (!Enum.IsDefined(request.Reason!.Value)) throw ApiException.BadRequest("Lý do khiếu nại không hợp lệ.");
        if (!CanDispute(booking, now, _opt.DisputeHours))
            throw ApiException.BadRequest("Đã hết thời hạn khiếu nại.");

        var dispute = new Dispute
        {
            BookingId = booking.Id,
            CustomerId = customerId,
            TaskerId = booking.TaskerId!.Value,
            Reason = request.Reason.Value,
            Description = request.Description.Trim(),
            EvidenceUrls = Dispute.JoinUrls(request.EvidenceUrls),
            CreatedAt = now,
        };
        _db.Disputes.Add(dispute);

        // Đơn → 11 Disputed: dừng tự xác nhận, tiền online tiếp tục được tạm giữ
        booking.Status = BookingStatus.Disputed;
        booking.UpdatedAt = now;

        _notifications.Add(dispute.TaskerId, "Đơn của bạn bị khiếu nại",
            $"Khách hàng đã khiếu nại đơn #{booking.Id}. Bạn có {_opt.TaskerResponseHours} giờ để phản hồi kèm ảnh.");
        await _notifications.AddForAdminsAsync("Khiếu nại mới", $"Đơn #{booking.Id} có khiếu nại mới cần xử lý.");
        await _db.SaveChangesAsync();
        return dispute;
    }

    public async Task RespondAsync(int disputeId, int taskerId, TaskerDisputeResponseRequest request)
    {
        var dispute = await _db.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId) ?? throw ApiException.NotFound();
        if (dispute.TaskerId != taskerId) throw ApiException.Forbidden();
        if (dispute.Status != DisputeStatus.Open) throw ApiException.BadRequest("Khiếu nại đã được xử lý.");
        if (DateTime.UtcNow > dispute.CreatedAt.AddHours(_opt.TaskerResponseHours))
            throw ApiException.BadRequest("Đã hết thời hạn phản hồi khiếu nại.");

        dispute.TaskerResponse = request.Response.Trim();
        dispute.TaskerEvidenceUrls = Dispute.JoinUrls(request.EvidenceUrls);
        dispute.TaskerRespondedAt = DateTime.UtcNow;
        await _notifications.AddForAdminsAsync("Tasker đã phản hồi khiếu nại", $"Khiếu nại #{dispute.Id} (đơn #{dispute.BookingId}) đã có phản hồi của Tasker.");
        await _db.SaveChangesAsync();
    }

    public async Task ResolveAsync(int disputeId, int adminId, ResolveDisputeRequest request)
    {
        var now = DateTime.UtcNow;
        var dispute = await _db.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId) ?? throw ApiException.NotFound();
        if (dispute.Status != DisputeStatus.Open) throw ApiException.BadRequest("Khiếu nại đã được xử lý.");

        var booking = await _db.Bookings.Include(b => b.Payment).FirstAsync(b => b.Id == dispute.BookingId);
        var resolution = request.Resolution!.Value;
        if (!Enum.IsDefined(resolution)) throw ApiException.BadRequest("Kết quả xử lý không hợp lệ.");
        var isCash = booking.PaymentMethod == PaymentMethod.Cash;
        var amount = booking.Payment?.Amount ?? booking.TotalPrice;

        // Đơn tiền mặt: hệ thống không giữ tiền nên chỉ bác khiếu nại hoặc yêu cầu làm lại (BR-D01)
        if (isCash && resolution is DisputeResolution.PartialRefund or DisputeResolution.FullRefund)
            throw ApiException.BadRequest("Đơn tiền mặt chỉ có thể bác khiếu nại hoặc yêu cầu làm lại.");

        DateTime? redoTime = null;
        decimal? refund = null;
        switch (resolution)
        {
            case DisputeResolution.PartialRefund:
                if (request.RefundAmount is null || request.RefundAmount <= 0 || request.RefundAmount >= amount
                    || decimal.Truncate(request.RefundAmount.Value) != request.RefundAmount)
                    throw ApiException.BadRequest("Số tiền hoàn không hợp lệ.");
                refund = request.RefundAmount;
                break;
            case DisputeResolution.FullRefund:
                if (request.RefundAmount is not null && request.RefundAmount != amount)
                    throw ApiException.BadRequest("Số tiền hoàn không hợp lệ.");
                refund = amount;
                break;
            case DisputeResolution.Redo:
                if (request.RedoScheduledTime is null) throw ApiException.BadRequest("Thời gian đặt không hợp lệ.");
                redoTime = _bookings.ValidateScheduledTime(request.RedoScheduledTime.Value);
                break;
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        string resultText;
        switch (resolution)
        {
            case DisputeResolution.ReleaseFull:
                if (!isCash) await _settlement.ReleaseOnlineAsync(booking, isAuto: false);
                booking.Status = BookingStatus.Paid;
                resultText = "Khiếu nại bị bác, tiền được chuyển đủ cho Tasker.";
                break;

            case DisputeResolution.PartialRefund:
                booking.Payment!.RefundedAmount = refund!.Value;
                booking.Payment.RefundedAt = now;
                _audit.Add(AuditService.Refund, refund.Value, booking.Id, booking.Payment.Id, "Hoàn một phần do khiếu nại");
                await _settlement.ReleaseOnlineAsync(booking, isAuto: false); // phần còn lại giải ngân sau chiết khấu
                booking.Status = BookingStatus.Paid;
                _notifications.Add(booking.CustomerId, "Đã hoàn tiền",
                    $"Đơn #{booking.Id}: hoàn {MoneyFormat.Vnd(refund.Value)} về phương thức bạn đã thanh toán.");
                resultText = $"Khách được hoàn {MoneyFormat.Vnd(refund.Value)}, phần còn lại chuyển cho Tasker.";
                break;

            case DisputeResolution.FullRefund:
                await _settlement.RefundHeldAsync(booking, 0, "Hoàn toàn bộ do khiếu nại được chấp nhận.");
                booking.Status = BookingStatus.Refunded;
                resultText = "Khách được hoàn toàn bộ tiền.";
                break;

            default: // Redo – làm lại miễn phí, tiền online vẫn tạm giữ
                booking.Status = BookingStatus.Accepted;
                booking.ScheduledTime = redoTime!.Value;
                booking.CompletedAt = null;
                booking.AutoConfirmAt = null;
                booking.AutoConfirmReminderSentAt = null;
                resultText = $"Tasker sẽ làm lại miễn phí lúc {VnTime.Format(redoTime.Value)}.";
                break;
        }
        booking.UpdatedAt = now;

        dispute.Status = DisputeStatus.Resolved;
        dispute.Resolution = resolution;
        dispute.RefundAmount = refund;
        dispute.RedoScheduledTime = redoTime;
        dispute.AdminNote = request.AdminNote.Trim();
        dispute.ResolvedById = adminId;
        dispute.ResolvedAt = now;

        // BR-D02: khiếu nại được chấp nhận (hoàn một phần / toàn bộ / làm lại) → Tasker +1 điểm vi phạm
        if (resolution != DisputeResolution.ReleaseFull)
            await _discipline.AddViolationAsync(dispute.TaskerId, 1, $"khiếu nại đơn #{booking.Id} được chấp nhận");

        _audit.Add(AuditService.DisputeResolved, refund ?? 0, booking.Id, booking.Payment?.Id,
            $"Khiếu nại #{dispute.Id}: {resolution} – {dispute.AdminNote}");
        foreach (var userId in new[] { dispute.CustomerId, dispute.TaskerId })
            _notifications.Add(userId, "Kết quả xử lý khiếu nại", $"Đơn #{booking.Id}: {resultText}");

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public IQueryable<Dispute> QueryWithDetails() => _db.Disputes.AsNoTracking()
        .Include(d => d.Booking).ThenInclude(b => b!.Payment)
        .Include(d => d.Customer)
        .Include(d => d.Tasker);

    public DisputeResponse ToResponse(Dispute d) => new(
        d.Id, d.BookingId, d.Booking?.Payment?.Amount ?? d.Booking?.TotalPrice ?? 0,
        d.Booking?.PaymentMethod ?? PaymentMethod.EWallet,
        d.Customer?.FullName ?? string.Empty, d.Tasker?.FullName ?? string.Empty,
        d.Reason, d.Description, Dispute.SplitUrls(d.EvidenceUrls),
        d.Booking?.CheckInPhotoUrl, d.Booking?.CheckOutPhotoUrl,
        d.TaskerResponse, Dispute.SplitUrls(d.TaskerEvidenceUrls),
        d.Status, d.Resolution, d.RefundAmount, d.AdminNote, d.CreatedAt, d.ResolvedAt,
        d.CustomerId, d.TaskerId, d.RedoScheduledTime, d.CreatedAt.AddHours(_opt.TaskerResponseHours));
}
