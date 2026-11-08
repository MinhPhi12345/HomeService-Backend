using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

// EP09 – Tasker xuất sắc tháng (FR-31). Chỉ Admin. Tháng tính theo giờ Việt Nam.
[ApiController]
[Route("api/tasker-awards")]
[Authorize(Roles = Roles.Admin)]
public class TaskerAwardsController : ControllerBase
{
    private readonly TaskerAwardService _awards;

    public TaskerAwardsController(TaskerAwardService awards) => _awards = awards;

    /// <summary>Bảng xếp hạng của tháng (mặc định tháng trước). State: NotComputed | Proposed | Approved.</summary>
    [HttpGet]
    public async Task<ActionResult<TaskerAwardMonthResponse>> GetMonth([FromQuery] int? year, [FromQuery] int? month)
    {
        var (y, m) = TaskerAwardService.PreviousMonth(DateTime.UtcNow);
        return Ok(await _awards.GetMonthAsync(year ?? y, month ?? m));
    }

    /// <summary>Chốt / tính lại bảng xếp hạng (chỉ tháng đã kết thúc và chưa duyệt).</summary>
    [HttpPost("compute")]
    public async Task<ActionResult<TaskerAwardMonthResponse>> Compute(AwardMonthRequest request)
        => Ok(await _awards.ComputeAsync(request.Year, request.Month));

    /// <summary>Chỉnh phần thưởng của 1 Tasker trước khi duyệt.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateReward(int id, UpdateTaskerAwardRequest request)
    {
        await _awards.UpdateRewardAsync(id, request);
        return NoContent();
    }

    [HttpPost("{id:int}/exclude")]
    public async Task<IActionResult> Exclude(int id, ExcludeTaskerAwardRequest request)
    {
        await _awards.ExcludeAsync(id, request.Reason);
        return NoContent();
    }

    [HttpPost("{id:int}/restore")]
    public async Task<IActionResult> Restore(int id)
    {
        await _awards.RestoreAsync(id);
        return NoContent();
    }

    /// <summary>Duyệt cả tháng (1 lần): cộng thưởng vào ví, áp chiết khấu ưu đãi, gắn huy hiệu, thông báo.</summary>
    [HttpPost("approve")]
    public async Task<ActionResult<TaskerAwardMonthResponse>> Approve(AwardMonthRequest request)
        => Ok(await _awards.ApproveAsync(request.Year, request.Month, User.GetUserId()));

    [HttpPost("{id:int}/gift-email")]
    public async Task<IActionResult> SendGiftEmail(int id, GiftInvitationRequest request)
    {
        await _awards.SendGiftEmailAsync(id, request);
        return NoContent();
    }

    [HttpPost("{id:int}/gift-delivered")]
    public async Task<IActionResult> GiftDelivered(int id)
    {
        await _awards.MarkGiftDeliveredAsync(id);
        return NoContent();
    }

    /// <summary>Dữ liệu để FE xuất Excel (có email, SĐT); mỗi lần gọi ghi nhật ký kiểm toán.</summary>
    [HttpPost("export")]
    public async Task<ActionResult<TaskerAwardMonthResponse>> Export(AwardMonthRequest request)
        => Ok(await _awards.ExportAsync(request.Year, request.Month));
}
