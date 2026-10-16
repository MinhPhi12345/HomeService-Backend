using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// US23 – Ví trả trước của Tasker: nạp (mô phỏng cộng ngay), rút (mô phỏng trừ ngay), lịch sử ví (US24)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Tasker)]
public class WalletController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WalletService _wallet;
    private readonly AuditService _audit;

    public WalletController(AppDbContext db, WalletService wallet, AuditService audit)
    {
        _db = db;
        _wallet = wallet;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<WalletResponse>> Get()
    {
        var p = await _db.TaskerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == User.GetUserId());
        if (p is null) return NotFound();
        return Ok(new WalletResponse(p.WalletBalance, p.ReservedAmount, p.AvailableBalance));
    }

    // Nạp tiền: 50.000 đ ≤ amount ≤ 10.000.000 đ (số nguyên VND)
    [HttpPost("topup")]
    public async Task<ActionResult<WalletResponse>> TopUp(TopUpRequest request)
    {
        var amount = request.Amount!.Value;
        if (decimal.Truncate(amount) != amount) throw ApiException.BadRequest("Số tiền nạp không hợp lệ.");

        var tx = await _wallet.ApplyAsync(User.GetUserId(), amount, WalletTransactionType.TopUp, null, "Nạp tiền vào ví (mô phỏng)");
        _audit.Add(AuditService.WalletTopUp, amount, detail: $"Tasker #{User.GetUserId()} nạp ví");
        await _db.SaveChangesAsync();
        return Ok(await GetWalletAsync());
    }

    // Rút tiền: ≥ 50.000 đ và ≤ số dư KHẢ DỤNG (walletBalance − reservedAmount) – BR-P05
    [HttpPost("withdraw")]
    public async Task<ActionResult<WalletResponse>> Withdraw(WithdrawRequest request)
    {
        var amount = request.Amount!.Value;
        if (decimal.Truncate(amount) != amount) throw ApiException.BadRequest("Số tiền rút không hợp lệ.");

        var profile = await _wallet.GetProfileAsync(User.GetUserId());
        if (amount > profile.AvailableBalance) throw ApiException.BadRequest("Số dư khả dụng không đủ.");

        await _wallet.ApplyAsync(profile.UserId, -amount, WalletTransactionType.Withdraw, null, "Rút tiền về tài khoản (mô phỏng)");
        _audit.Add(AuditService.WalletWithdraw, amount, detail: $"Tasker #{profile.UserId} rút ví");
        await _db.SaveChangesAsync();
        return Ok(await GetWalletAsync());
    }

    // US24 – lịch sử ví (nạp, trừ hoa hồng, thu nhập online, rút, phí hủy), mới nhất trước
    [HttpGet("transactions")]
    public async Task<ActionResult<PagedResult<WalletTransactionResponse>>> GetTransactions([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var userId = User.GetUserId();
        var query = _db.WalletTransactions.AsNoTracking().Where(w => w.TaskerId == userId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(w => new WalletTransactionResponse(w.Id, w.Type, w.Amount, w.BalanceAfter, w.BookingId, w.Note, w.CreatedAt))
            .ToListAsync();
        return Ok(new PagedResult<WalletTransactionResponse>(items, p, size, total));
    }

    private async Task<WalletResponse> GetWalletAsync()
    {
        var p = await _db.TaskerProfiles.AsNoTracking().FirstAsync(t => t.UserId == User.GetUserId());
        return new WalletResponse(p.WalletBalance, p.ReservedAmount, p.AvailableBalance);
    }
}
