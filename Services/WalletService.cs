using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>
/// Ví trả trước của Tasker (FR-16, FR-17). Mọi thay đổi số dư ghi 1 WalletTransaction (BR-W01);
/// ví không bao giờ âm (BR-P04). Người gọi tự SaveChanges trong cùng transaction.
/// </summary>
public class WalletService
{
    private readonly AppDbContext _db;

    public WalletService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<TaskerProfile> GetProfileAsync(int taskerId)
        => await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == taskerId)
           ?? throw ApiException.BadRequest("Không tìm thấy hồ sơ Tasker.");

    /// <summary>Cộng (amount &gt; 0) hoặc trừ (amount &lt; 0) ví Tasker và ghi lịch sử.</summary>
    public async Task<WalletTransaction> ApplyAsync(int taskerId, decimal amount, WalletTransactionType type,
        int? bookingId, string? note)
    {
        var profile = await GetProfileAsync(taskerId);
        var newBalance = profile.WalletBalance + amount;
        if (newBalance < 0)
            throw ApiException.BadRequest("Số dư khả dụng không đủ.");

        profile.WalletBalance = newBalance;
        var tx = new WalletTransaction
        {
            TaskerId = taskerId,
            BookingId = bookingId,
            Type = type,
            Amount = amount,
            BalanceAfter = newBalance,
            Note = note is { Length: > 255 } ? note[..255] : note,
        };
        _db.WalletTransactions.Add(tx);
        return tx;
    }

    /// <summary>
    /// BR-P05: reservedAmount = tổng hoa hồng ước tính của các đơn TIỀN MẶT đang Accepted/InProgress của Tasker
    /// (chưa quyết toán). Tính lại từ dữ liệu đơn đã lưu nên luôn đúng sau nhận/hủy/hoàn thành/vắng mặt.
    /// Gọi SAU khi đã SaveChanges thay đổi trạng thái đơn.
    /// </summary>
    public async Task RecalculateReservedAsync(int taskerId)
    {
        var profile = await GetProfileAsync(taskerId);
        var bookings = await _db.Bookings
            .Where(b => b.TaskerId == taskerId
                        && b.PaymentMethod == PaymentMethod.Cash
                        && (b.Status == BookingStatus.Accepted || b.Status == BookingStatus.InProgress)
                        && b.Payment == null)
            .Select(b => new { b.TotalPrice, b.CommissionRate })
            .ToListAsync();
        profile.ReservedAmount = bookings.Sum(b => Money.Commission(b.TotalPrice, b.CommissionRate));
    }
}
