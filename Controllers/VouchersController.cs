using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

// EP10 – Khách hàng: kiểm tra mã giảm giá trước khi đặt đơn và xem "Ưu đãi của tôi" (FR-32, FR-33)
[ApiController]
[Route("api/vouchers")]
[Authorize(Roles = Roles.Customer)]
public class VouchersController : ControllerBase
{
    private readonly PromotionService _promotions;

    public VouchersController(PromotionService promotions) => _promotions = promotions;

    /// <summary>Xem trước số tiền giảm của mã cho đơn sắp đặt (không giữ lượt – chỉ giữ khi tạo đơn).</summary>
    [HttpPost("check")]
    public async Task<ActionResult<VoucherCheckResponse>> Check(CheckVoucherRequest request)
        => Ok(await _promotions.CheckAsync(User.GetUserId(), request));

    /// <summary>Quà tặng riêng còn hạn và các chương trình công khai đang chạy mà khách còn lượt dùng.</summary>
    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<MyVoucherResponse>>> GetMine()
        => Ok(await _promotions.GetMineAsync(User.GetUserId()));
}

// EP10 – Admin: quản lý chương trình khuyến mãi, thống kê, cài đặt quà tặng tự động
[ApiController]
[Route("api/promotions")]
[Authorize(Roles = Roles.Admin)]
public class PromotionsController : ControllerBase
{
    private readonly PromotionService _promotions;

    public PromotionsController(PromotionService promotions) => _promotions = promotions;

    [HttpGet("vouchers")]
    public async Task<ActionResult<IEnumerable<VoucherAdminResponse>>> GetCampaigns()
        => Ok(await _promotions.GetCampaignsAsync());

    [HttpPost("vouchers")]
    public async Task<ActionResult<object>> Create(SaveVoucherRequest request)
        => StatusCode(StatusCodes.Status201Created, new { id = await _promotions.CreateCampaignAsync(request, User.GetUserId()) });

    [HttpPut("vouchers/{id:int}")]
    public async Task<IActionResult> Update(int id, SaveVoucherRequest request)
    {
        await _promotions.UpdateCampaignAsync(id, request);
        return NoContent();
    }

    /// <summary>action: pause (tạm dừng) | resume (tiếp tục) | end (kết thúc ngay).</summary>
    [HttpPut("vouchers/{id:int}/status")]
    public async Task<IActionResult> ChangeStatus(int id, VoucherStatusRequest request)
    {
        await _promotions.ChangeStatusAsync(id, request.Action);
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PromotionSummaryResponse>> GetSummary() => Ok(await _promotions.GetSummaryAsync());

    [HttpGet("gift-rules")]
    public async Task<ActionResult<IEnumerable<GiftRuleResponse>>> GetGiftRules() => Ok(await _promotions.GetGiftRulesAsync());

    /// <summary>kind: 1 Quà chào mừng, 2 Quà khách thân thiết, 3 Quà xin lỗi.</summary>
    [HttpPut("gift-rules/{kind:int}")]
    public async Task<ActionResult<GiftRuleResponse>> SaveGiftRule(int kind, SaveGiftRuleRequest request)
        => Ok(await _promotions.SaveGiftRuleAsync((VoucherSource)kind, request, User.GetUserId()));
}
