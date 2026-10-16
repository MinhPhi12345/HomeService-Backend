using HomeService.Api.Enums;

namespace HomeService.Api.Models;

// BR-W01 – mọi thay đổi số dư ví Tasker đều ghi 1 dòng (SRS 7.11)
public class WalletTransaction
{
    public int Id { get; set; }

    public int TaskerId { get; set; }
    public User? Tasker { get; set; }

    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public WalletTransactionType Type { get; set; }
    public decimal Amount { get; set; }       // có dấu: + vào ví, − ra khỏi ví
    public decimal BalanceAfter { get; set; } // số dư sau giao dịch (≥ 0)
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
