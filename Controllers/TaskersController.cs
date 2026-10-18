using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP01 - US04 (gửi & xác thực hồ sơ Tasker) / EP04 - US16 (ghép nối)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TaskersController : ControllerBase
{
    private static readonly int[] AllowedRadiusKm = { 3, 5, 8 };

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public TaskersController(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    // US04 - Tasker gửi / cập nhật hồ sơ (CCCD, kỹ năng, khu vực, điểm làm việc, bán kính phục vụ)
    [HttpPost("profile")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<ActionResult<TaskerProfileResponse>> SubmitProfile(TaskerProfileRequest request)
    {
        if (!AllowedRadiusKm.Contains(request.ServiceRadiusKm!.Value))
            throw ApiException.BadRequest("Bán kính phục vụ không hợp lệ.");
        if (!WorkingAreas.IsValid(request.WorkingArea))
            throw ApiException.BadRequest("Khu vực làm việc không hợp lệ.");

        var userId = User.GetUserId();
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == userId);
        if (profile is null)
        {
            profile = new TaskerProfile { UserId = userId };
            _db.TaskerProfiles.Add(profile);
        }

        var idCardChanged = profile.IdCardNumber != request.IdCardNumber
                            || profile.IdCardImageUrl != request.IdCardImageUrl;

        profile.IdCardNumber = request.IdCardNumber;
        profile.IdCardImageUrl = request.IdCardImageUrl;
        profile.Skills = string.IsNullOrWhiteSpace(request.Skills) ? null : request.Skills.Trim();
        profile.WorkingArea = request.WorkingArea.Trim();
        profile.BaseAddress = request.BaseAddress.Trim();
        profile.BaseLatitude = request.BaseLatitude!.Value;
        profile.BaseLongitude = request.BaseLongitude!.Value;
        profile.ServiceRadiusKm = request.ServiceRadiusKm.Value;
        profile.SubmittedAt = DateTime.UtcNow;

        // FR-04: đổi điểm làm việc/bán kính không cần duyệt lại nếu CCCD không đổi.
        // Hồ sơ bị từ chối gửi lại, hoặc đã duyệt mà đổi CCCD → quay về chờ duyệt.
        var needReview = profile.VerificationStatus == TaskerVerificationStatus.Rejected
                         || (profile.VerificationStatus == TaskerVerificationStatus.Approved && idCardChanged);
        if (needReview) profile.VerificationStatus = TaskerVerificationStatus.Pending;

        if (profile.VerificationStatus == TaskerVerificationStatus.Pending)
            await _notifications.AddForAdminsAsync("Hồ sơ Tasker chờ duyệt",
                $"Tasker {User.FindFirst("fullName")?.Value} vừa gửi hồ sơ xác thực.");

        await _db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status201Created, ToResponse(profile));
    }

    // US04 - Tasker xem hồ sơ & trạng thái duyệt của mình
    [HttpGet("profile")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<ActionResult<TaskerProfileResponse>> GetMyProfile()
    {
        var userId = User.GetUserId();
        var profile = await _db.TaskerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId);
        if (profile is null) return NotFound();
        return Ok(ToResponse(profile));
    }

    // US04 - Admin xem danh sách hồ sơ Tasker chờ duyệt (chỉ hồ sơ đã gửi CCCD)
    [HttpGet("pending-verification")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<PendingTaskerItem>>> GetPendingVerification()
    {
        var pending = await _db.TaskerProfiles
            .AsNoTracking()
            .Where(t => t.VerificationStatus == TaskerVerificationStatus.Pending && t.IdCardNumber != "")
            .OrderBy(t => t.SubmittedAt)
            .Select(t => new PendingTaskerItem(
                t.Id, t.UserId, t.User!.FullName, t.IdCardNumber, t.Skills, t.WorkingArea,
                t.User.Email, t.User.PhoneNumber, t.IdCardImageUrl,
                t.BaseAddress, t.BaseLatitude, t.BaseLongitude, t.ServiceRadiusKm, t.SubmittedAt))
            .ToListAsync();

        return Ok(pending);
    }

    // US04 - Admin duyệt/từ chối hồ sơ Tasker (tham số là taskerProfileId, KHÔNG phải userId)
    [HttpPut("{taskerProfileId:int}/verification")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetVerification(int taskerProfileId, [FromQuery] bool approve)
    {
        var profile = await _db.TaskerProfiles.FindAsync(taskerProfileId);
        if (profile is null) return NotFound();
        if (string.IsNullOrEmpty(profile.IdCardNumber))
            throw ApiException.BadRequest("Tasker chưa gửi hồ sơ xác thực.");

        profile.VerificationStatus = approve ? TaskerVerificationStatus.Approved : TaskerVerificationStatus.Rejected;

        _notifications.Add(profile.UserId,
            approve ? "Hồ sơ đã được duyệt" : "Hồ sơ bị từ chối",
            approve
                ? "Chúc mừng! Hồ sơ Tasker của bạn đã được duyệt, bạn có thể bắt đầu nhận đơn."
                : "Hồ sơ Tasker của bạn chưa đạt yêu cầu. Vui lòng kiểm tra lại thông tin và gửi lại.");

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US16 - Tasker bật/tắt nhận đơn; app gửi vị trí hiện tại 1 lần khi bật (không theo dõi GPS chạy ngầm – BR-M06)
    [HttpPut("availability")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<IActionResult> UpdateAvailability(UpdateAvailabilityRequest request)
    {
        var userId = User.GetUserId();
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == userId);
        if (profile is null) return NotFound();

        profile.IsAvailable = request.IsAvailable!.Value;
        if (request.Latitude is not null && request.Longitude is not null)
        {
            profile.LastLatitude = request.Latitude;
            profile.LastLongitude = request.Longitude;
            profile.LastLocationAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US16 - Gợi ý danh sách Tasker đã duyệt theo khu vực (ghép nối tự động dùng tọa độ – MatchingService)
    [HttpGet("available")]
    public async Task<ActionResult<IEnumerable<AvailableTaskerItem>>> GetAvailable([FromQuery] string? area)
    {
        var query = _db.TaskerProfiles
            .AsNoTracking()
            .Where(t => t.VerificationStatus == TaskerVerificationStatus.Approved
                        && t.IsAvailable && t.User!.IsActive);

        if (!string.IsNullOrWhiteSpace(area))
            query = query.Where(t => t.WorkingArea == area.Trim());

        var result = await query
            .OrderByDescending(t => t.RatingAverage)
            .Select(t => new AvailableTaskerItem(t.UserId, t.User!.FullName, t.RatingAverage, t.WorkingArea))
            .ToListAsync();

        return Ok(result);
    }

    private static TaskerProfileResponse ToResponse(TaskerProfile t) => new(
        t.Id, t.UserId, t.IdCardNumber, t.IdCardImageUrl, t.Skills, t.WorkingArea,
        t.VerificationStatus, t.RatingAverage, t.IsAvailable,
        t.BaseAddress, t.BaseLatitude, t.BaseLongitude, t.ServiceRadiusKm);
}
