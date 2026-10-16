using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// US35 – Khiếu nại & xử lý tranh chấp (khách tạo khiếu nại qua POST /api/bookings/{id}/dispute)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DisputesController : ControllerBase
{
    private readonly DisputeService _disputes;

    public DisputesController(DisputeService disputes)
    {
        _disputes = disputes;
    }

    // Admin – danh sách khiếu nại (lọc ?status=0 Chờ xử lý / 1 Đã xử lý)
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<DisputeResponse>>> GetAll([FromQuery] DisputeStatus? status)
    {
        var query = _disputes.QueryWithDetails();
        if (status is not null) query = query.Where(d => d.Status == status);
        var list = await query.OrderBy(d => d.Status).ThenByDescending(d => d.CreatedAt).ToListAsync();
        return Ok(list.Select(_disputes.ToResponse));
    }

    // Chi tiết – chỉ khách, Tasker của đơn hoặc Admin
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DisputeResponse>> GetById(int id)
    {
        var d = await _disputes.QueryWithDetails().FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound();
        var userId = User.GetUserId();
        if (!User.IsInRole(UserRole.Admin) && d.CustomerId != userId && d.TaskerId != userId) return Forbid();
        return Ok(_disputes.ToResponse(d));
    }

    // Tasker phản hồi trong 24 giờ kèm ảnh
    [HttpPut("{id:int}/respond")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<IActionResult> Respond(int id, TaskerDisputeResponseRequest request)
    {
        await _disputes.RespondAsync(id, User.GetUserId(), request);
        return NoContent();
    }

    // Admin xử lý: 0 bác khiếu nại, 1 hoàn một phần, 2 hoàn toàn bộ, 3 làm lại miễn phí
    [HttpPut("{id:int}/resolve")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Resolve(int id, ResolveDisputeRequest request)
    {
        await _disputes.ResolveAsync(id, User.GetUserId(), request);
        return NoContent();
    }
}
