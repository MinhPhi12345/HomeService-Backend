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

// US23 – Ví trả trước của Tasker: nạp (mô phỏng cộng ngay), rút (xác nhận OTP qua email rồi mô phỏng trừ ngay), lịch sử ví (US24)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Tasker)]
public class WalletController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WalletService _wallet;
    private readonly AuditService _audit;

    private readonly decimal _minDeposit;

    public WalletController(AppDbContext db, WalletService wallet, AuditService audit,
        Microsoft.Extensions.Options.IOptions<HomeService.Api.Options.MatchingOptions> matching)
    {
        _db = db;
        _wallet = wallet;
        _audit = audit;
        _minDeposit = matching.Value.MinWalletDeposit;
    }

    [HttpGet]
    public async Task<ActionResult<WalletResponse>> Get()
    {
        var p = await _db.TaskerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == User.GetUserId());
        if (p is null) return NotFound();
        return Ok(ToResponse(p));
    }

    // Nạp tiền: 50.000 đ ≤ amount ≤ 10.000.000 đ (số nguyên VND)
    [HttpPost("topup")]
    public async Task<ActionResult<WalletResponse>> TopUp(TopUpRequest request)
    {
        var amount = request.Amount!.Value;
        if (decimal.Truncate(amount) != amount) throw ApiException.BadRequest("Số tiền nạp không hợp lệ.");

        var channel = request.Channel switch
        {
            "EWallet" => "ví điện tử",
            "Card" => "thẻ ngân hàng",
            "BankTransfer" => "chuyển khoản",
            _ => null,
        };
        var note = channel is null ? "Nạp tiền vào ví (mô phỏng)" : $"Nạp tiền qua {channel} (mô phỏng)";
        await _wallet.ApplyAsync(User.GetUserId(), amount, WalletTransactionType.TopUp, null, note);
        _audit.Add(AuditService.WalletTopUp, amount, detail: $"Tasker #{User.GetUserId()} nạp ví");
        await _db.SaveChangesAsync();
        return Ok(await GetWalletAsync());
    }

    // Rút tiền – bước 1: kiểm tra số tiền rồi gửi mã OTP xác nhận về email Tasker (mã gắn với đúng số tiền)
    [HttpPost("withdraw/otp")]
    public async Task<ActionResult<OtpSentResponse>> SendWithdrawOtp(WithdrawOtpRequest request, [FromServices] OtpService otp)
    {
        var amount = request.Amount!.Value;
        var profile = await _wallet.GetProfileAsync(User.GetUserId());
        EnsureCanWithdraw(profile, amount);
        var user = await _db.Users.FirstAsync(u => u.Id == profile.UserId);
        return Ok(await otp.SendAsync(user, OtpPurpose.Withdraw, amount));
    }

    // Rút tiền – bước 2: ≥ 50.000 đ, ≤ số dư KHẢ DỤNG (BR-P05), giữ ký quỹ khi đang nhận đơn, kèm mã OTP
    [HttpPost("withdraw")]
    public async Task<ActionResult<WalletResponse>> Withdraw(WithdrawRequest request, [FromServices] OtpService otp)
    {
        var amount = request.Amount!.Value;
        var profile = await _wallet.GetProfileAsync(User.GetUserId());
        EnsureCanWithdraw(profile, amount);
        await otp.VerifyAsync(profile.UserId, OtpPurpose.Withdraw, request.OtpCode, amount);

        await _wallet.ApplyAsync(profile.UserId, -amount, WalletTransactionType.Withdraw, null, "Rút tiền về tài khoản (mô phỏng)");
        _audit.Add(AuditService.WalletWithdraw, amount, detail: $"Tasker #{profile.UserId} rút ví (xác nhận OTP)");
        await _db.SaveChangesAsync();
        return Ok(await GetWalletAsync());
    }

    private void EnsureCanWithdraw(Models.TaskerProfile profile, decimal amount)
    {
        if (decimal.Truncate(amount) != amount) throw ApiException.BadRequest("Số tiền rút không hợp lệ.");
        if (amount > profile.AvailableBalance) throw ApiException.BadRequest("Số dư khả dụng không đủ.");
        // Ký quỹ: đang bật nhận đơn thì phải giữ lại mức tối thiểu trong ví
        if (profile.IsAvailable && profile.AvailableBalance - amount < _minDeposit)
            throw ApiException.BadRequest(
                $"Bạn đang bật nhận đơn nên ví phải còn tối thiểu {MoneyFormat.Vnd(_minDeposit)} ký quỹ. " +
                $"Có thể rút tối đa {MoneyFormat.Vnd(Math.Max(0, profile.AvailableBalance - _minDeposit))}, hoặc tắt nhận đơn để rút hết.");
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
        return ToResponse(p);
    }

    private WalletResponse ToResponse(Models.TaskerProfile p) =>
        new(p.WalletBalance, p.ReservedAmount, p.AvailableBalance, _minDeposit, p.AvailableBalance >= _minDeposit);
}
