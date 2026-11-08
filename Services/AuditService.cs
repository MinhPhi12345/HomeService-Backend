using HomeService.Api.Data;
using HomeService.Api.Models;

namespace HomeService.Api.Services;

/// <summary>NFR-16 – ghi log kiểm toán trong cùng transaction với thao tác tiền (người gọi SaveChanges).</summary>
public class AuditService
{
    public const string PaymentHeld = "PaymentHeld";
    public const string Released = "Released";
    public const string CashCommission = "CashCommission";
    public const string Refund = "Refund";
    public const string CancellationFee = "CancellationFee";
    public const string Debt = "Debt";
    public const string DebtPaid = "DebtPaid";
    public const string DisputeResolved = "DisputeResolved";
    public const string WalletTopUp = "WalletTopUp";
    public const string WalletWithdraw = "WalletWithdraw";
    public const string ReviewKept = "ReviewKept";       // US27 – Admin giữ lại đánh giá bị báo cáo
    public const string ReviewRemoved = "ReviewRemoved"; // US27 – Admin gỡ đánh giá bị báo cáo
    public const string TaskerAwardApproved = "TaskerAwardApproved";           // FR-31 – duyệt Tasker xuất sắc tháng
    public const string TaskerBonus = "TaskerBonus";                           // FR-31 – cộng tiền thưởng vào ví
    public const string TaskerAwardGiftEmail = "TaskerAwardGiftEmail";         // FR-31 – gửi email mời nhận quà
    public const string TaskerAwardGiftDelivered = "TaskerAwardGiftDelivered"; // FR-31 – đã trao quà
    public const string TaskerAwardExport = "TaskerAwardExport";               // FR-31 – xuất Excel (có email, SĐT)
    public const string VoucherCreated = "VoucherCreated";                     // FR-32 – tạo chương trình
    public const string VoucherUpdated = "VoucherUpdated";                     // FR-32 – sửa / tạm dừng / kết thúc
    public const string GiftRuleUpdated = "GiftRuleUpdated";                   // FR-33 – đổi cài đặt quà tặng

    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public void Add(string action, decimal amount, int? bookingId = null, int? paymentId = null, string? detail = null)
    {
        int? actor = null;
        var sub = _http.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? _http.HttpContext?.User.FindFirst("sub")?.Value;
        if (int.TryParse(sub, out var id)) actor = id;

        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            Amount = amount,
            BookingId = bookingId,
            PaymentId = paymentId,
            ActorUserId = actor,
            Detail = detail is { Length: > 500 } ? detail[..500] : detail,
        });
    }
}
