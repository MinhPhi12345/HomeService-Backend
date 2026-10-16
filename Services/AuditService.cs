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
