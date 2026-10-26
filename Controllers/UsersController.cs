using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP01 - US06 (phân quyền) / EP08 - US30 (quản lý KH & Tasker)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// US30 - Danh sách người dùng. ?role= nhận TÊN vai trò (Customer/Tasker/Admin, không phân biệt hoa thường),
    /// field role trong JSON trả về là SỐ. Bổ sung lọc ?keyword= (tên/email/sđt) và ?isActive=.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminUserItem>>> GetAll([FromQuery] string? role,
        [FromQuery] string? keyword, [FromQuery] bool? isActive)
    {
        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!Enum.TryParse<UserRole>(role, true, out var parsedRole) || !Enum.IsDefined(parsedRole))
                throw ApiException.BadRequest("Vai trò không hợp lệ.");
            query = query.Where(u => u.Role == parsedRole);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(kw) || u.Email.Contains(kw)
                                     || (u.PhoneNumber != null && u.PhoneNumber.Contains(kw)));
        }
        if (isActive is not null) query = query.Where(u => u.IsActive == isActive);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new AdminUserItem(
                u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive, u.CreatedAt,
                u.OutstandingAmount, u.IsCashBlocked, u.UnpaidReportCount,
                u.TaskerProfile != null ? u.TaskerProfile.VerificationStatus : null,
                u.TaskerProfile != null ? u.TaskerProfile.RatingAverage : null,
                u.TaskerProfile != null ? u.TaskerProfile.ViolationPoints : null,
                u.TaskerProfile != null ? u.TaskerProfile.SuspendedUntil : null))
            .ToListAsync();

        return Ok(users);
    }

    // US30 - Khóa / mở khóa tài khoản vi phạm. Tài khoản bị khóa không đăng nhập được, token cũ bị từ chối (BR-A03).
    [HttpPut("{id:int}/active-status")]
    public async Task<IActionResult> SetActiveStatus(int id, [FromQuery] bool isActive)
    {
        var user = await _db.Users.Include(u => u.TaskerProfile).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (!isActive && user.Id == User.GetUserId())
            throw ApiException.BadRequest("Không thể tự khóa tài khoản của chính mình.");

        user.IsActive = isActive;
        if (!isActive && user.TaskerProfile is not null) user.TaskerProfile.IsAvailable = false; // ngừng nhận lời mời
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US36 / BR-U01 - Admin mở lại (hoặc chặn) quyền thanh toán tiền mặt của khách
    [HttpPut("{id:int}/cash-blocked")]
    public async Task<IActionResult> SetCashBlocked(int id, [FromQuery] bool isCashBlocked)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.Role != UserRole.Customer) throw ApiException.BadRequest("Chỉ áp dụng cho tài khoản khách hàng.");
        user.IsCashBlocked = isCashBlocked;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
