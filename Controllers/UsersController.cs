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

// EP01 - US06 (phân quyền) / EP08 - US30 (quản lý KH & Tasker)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AccountLockService _locks;

    public UsersController(AppDbContext db, AccountLockService locks)
    {
        _db = db;
        _locks = locks;
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
                u.TaskerProfile != null ? u.TaskerProfile.SuspendedUntil : null,
                u.LockReason, u.LockDetail, u.LockedAt, u.LockPendingAt,
                _db.AccountAppeals.Count(a => a.UserId == u.Id && a.Status == AppealStatus.Pending)))
            .ToListAsync();

        return Ok(users);
    }

    // US30 (SRS v1.8) - Xem trước khi khóa: các đơn đang dở + nội dung email sẽ gửi
    [HttpPost("{id:int}/lock-preview")]
    public async Task<ActionResult<LockPreviewResponse>> LockPreview(int id, LockAccountRequest request) =>
        Ok(await _locks.PreviewAsync(id, User.GetUserId(), request));

    // US30 (SRS v1.8) - Khóa tài khoản có lý do. Mode 0: khóa sau khi hoàn tất đơn đang dở; 1: khóa ngay (tự xử lý đơn dở).
    // Tài khoản bị khóa không đăng nhập được, token cũ bị từ chối (BR-A03); người dùng nhận email kèm đường dẫn kháng cáo.
    [HttpPost("{id:int}/lock")]
    public async Task<ActionResult<LockResultResponse>> Lock(int id, LockAccountRequest request) =>
        Ok(await _locks.LockAsync(id, User.GetUserId(), request));

    // US30 - Mở khóa (hoặc hủy lệnh chờ khóa), ghi chú tùy chọn được gửi trong email
    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id, UnlockAccountRequest? request = null)
    {
        await _locks.UnlockAsync(id, User.GetUserId(), request?.Note);
        return NoContent();
    }

    // US30 - API cũ (giữ để tương thích): isActive=false → khóa sau khi hoàn tất đơn với lý do "Khác"; true → mở khóa.
    [HttpPut("{id:int}/active-status")]
    public async Task<IActionResult> SetActiveStatus(int id, [FromQuery] bool isActive)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (isActive)
        {
            if (!user.IsActive || user.LockPendingAt is not null) await _locks.UnlockAsync(id, User.GetUserId(), null);
            return NoContent();
        }
        if (!user.IsActive) return NoContent();
        await _locks.LockAsync(id, User.GetUserId(), new LockAccountRequest
        {
            Reason = AccountLockReason.Other,
            Detail = "Tài khoản bị khóa bởi quản trị viên.",
            Mode = AccountLockMode.AfterOrders,
        });
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
