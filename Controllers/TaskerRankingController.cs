using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

// EP09 – Tasker xem xếp hạng Tasker xuất sắc tháng của mình (hạng tạm tính tháng đang diễn ra, quy tắc, phần thưởng, lịch sử).
[ApiController]
[Route("api/tasker-ranking")]
[Authorize(Roles = Roles.Tasker)]
public class TaskerRankingController : ControllerBase
{
    private readonly TaskerAwardService _awards;

    public TaskerRankingController(TaskerAwardService awards) => _awards = awards;

    /// <summary>
    /// Hạng tạm tính tháng này (giờ Việt Nam) của Tasker đang đăng nhập, điều kiện còn thiếu, điểm từng tiêu chí, top N
    /// (người khác chỉ hiện tên gọi), quy tắc – phần thưởng và các tháng đã nhận thưởng. Số liệu được làm mới mỗi 2 phút.
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<MyRankingResponse>> Me() => Ok(await _awards.MyRankingAsync(User.GetUserId()));
}
