using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// NFR-16 – Admin xem nhật ký kiểm toán các thao tác tiền & quyết định khiếu nại (chỉ đọc, không có API sửa/xóa)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditLogsController(AppDbContext db) => _db = db;

    /// <summary>
    /// Danh sách nhật ký mới nhất trước. Lọc: ?action= (vd Released, Refund…), ?bookingId=, ?from=&amp;to= (ngày),
    /// phân trang ?page=&amp;pageSize= (tối đa 100). Xuất Excel: gọi lặp các trang hoặc pageSize=100.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogItem>>> GetAll([FromQuery] string? action, [FromQuery] int? bookingId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action.Trim());
        if (bookingId is not null) query = query.Where(a => a.BookingId == bookingId);
        if (from is not null)
        {
            var f = DateTimeHelper.ToUtc(from.Value);
            query = query.Where(a => a.CreatedAt >= f);
        }
        if (to is not null)
        {
            var t = DateTimeHelper.ToUtc(to.Value);
            // "to" chỉ có ngày => lấy hết ngày đó
            if (to.Value.TimeOfDay == TimeSpan.Zero) t = t.AddDays(1).AddTicks(-1);
            query = query.Where(a => a.CreatedAt <= t);
        }
        if (from is not null && to is not null && from > to)
            throw ApiException.BadRequest("Khoảng thời gian không hợp lệ.");

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(a => new
            {
                a.Id, a.Action, a.BookingId, a.PaymentId, a.ActorUserId, a.Amount, a.Detail, a.CreatedAt,
                ActorName = a.ActorUserId == null
                    ? null
                    : _db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync();

        var items = rows.Select(r => new AuditLogItem(r.Id, r.Action, r.BookingId, r.PaymentId, r.ActorUserId,
            r.ActorUserId == null ? "Hệ thống" : r.ActorName ?? $"Người dùng #{r.ActorUserId}",
            r.Amount, r.Detail, r.CreatedAt)).ToList();
        return Ok(new PagedResult<AuditLogItem>(items, p, size, total));
    }
}
