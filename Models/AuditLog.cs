namespace HomeService.Api.Models;

// NFR-16 – nhật ký kiểm toán mọi thao tác tiền & quyết định khiếu nại (ai, khi nào, số tiền). Không có API xóa.
public class AuditLog
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;   // vd: PaymentHeld, Released, CashCommission, Refund...
    public int? BookingId { get; set; }
    public int? PaymentId { get; set; }
    public int? ActorUserId { get; set; }                // null = Hệ thống (BackgroundService)
    public decimal Amount { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
