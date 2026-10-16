using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP07 – Thông báo trong ứng dụng (US28, US29). Client tải khi mở app/trang hoặc polling 30 giây.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    // US28/US29 - Danh sách thông báo của người đang đăng nhập (mới nhất trước, có phân trang)
    [HttpGet("my")]
    public async Task<ActionResult<NotificationPage>> GetMine([FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] bool? unreadOnly)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var userId = User.GetUserId();
        var query = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly == true) query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync();
        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        var items = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(n => new NotificationResponse(n.Id, n.Title, n.Message, n.IsRead, n.CreatedAt))
            .ToListAsync();

        return Ok(new NotificationPage(items, p, size, total, unread));
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = User.GetUserId();
        var rows = await _db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return rows == 0 ? NotFound() : NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = User.GetUserId();
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return NoContent();
    }
}
