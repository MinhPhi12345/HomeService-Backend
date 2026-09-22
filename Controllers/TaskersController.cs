using HomeService.Api.Data;
using HomeService.Api.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP01 - US04 (xác thực hồ sơ) / EP04 - US16 (ghép nối, khung sườn)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TaskersController : ControllerBase
{
    private readonly AppDbContext _db;

    public TaskersController(AppDbContext db)
    {
        _db = db;
    }

    // US04 - Admin xem danh sách hồ sơ Tasker chờ duyệt
    [HttpGet("pending-verification")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetPendingVerification()
    {
        var pending = await _db.TaskerProfiles
            .Include(t => t.User)
            .Where(t => t.VerificationStatus == TaskerVerificationStatus.Pending)
            .Select(t => new { t.Id, t.UserId, t.User!.FullName, t.IdCardNumber, t.Skills, t.WorkingArea })
            .ToListAsync();

        return Ok(pending);
    }

    // US04 - Admin duyệt/từ chối hồ sơ Tasker
    [HttpPut("{taskerProfileId:int}/verification")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetVerification(int taskerProfileId, [FromQuery] bool approve)
    {
        var profile = await _db.TaskerProfiles.FindAsync(taskerProfileId);
        if (profile is null) return NotFound();

        profile.VerificationStatus = approve ? TaskerVerificationStatus.Approved : TaskerVerificationStatus.Rejected;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US16 - Gợi ý danh sách Tasker phù hợp cho một khu vực (nền cho matching tự động)
    // TODO: nâng cấp thành MatchingService độc lập, xét thêm lịch rảnh + rating khi mở rộng
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable([FromQuery] string? area)
    {
        var query = _db.TaskerProfiles
            .Include(t => t.User)
            .Where(t => t.VerificationStatus == TaskerVerificationStatus.Approved);

        if (!string.IsNullOrWhiteSpace(area))
            query = query.Where(t => t.WorkingArea == area);

        var result = await query
            .OrderByDescending(t => t.RatingAverage)
            .Select(t => new { t.UserId, t.User!.FullName, t.RatingAverage, t.WorkingArea })
            .ToListAsync();

        return Ok(result);
    }
}
