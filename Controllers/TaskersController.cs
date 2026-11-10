using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using HomeService.Api.Options;
using HomeService.Api.Services;
using HomeService.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly BusinessOptions _business;

    public TaskersController(AppDbContext db, INotificationService notifications, IOptions<BusinessOptions> business)
    {
        _db = db;
        _notifications = notifications;
        _business = business.Value;
    }

    // US04 - Tasker gửi / cập nhật hồ sơ (CCCD, kỹ năng, điểm làm việc, bán kính phục vụ; khu vực tự lấy từ địa chỉ)
    [HttpPost("profile")]
    [Authorize(Roles = Roles.Tasker)]
    public async Task<ActionResult<TaskerProfileResponse>> SubmitProfile(TaskerProfileRequest request)
    {
        if (!AllowedRadiusKm.Contains(request.ServiceRadiusKm!.Value))
            throw ApiException.BadRequest("Bán kính phục vụ không hợp lệ.");

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
        profile.IdCardImageUrl = UploadUrl.Normalize(request.IdCardImageUrl);
        profile.Skills = string.IsNullOrWhiteSpace(request.Skills) ? null : request.Skills.Trim();
        profile.BaseAddress = request.BaseAddress.Trim();
        // SRS 7.9: khu vực (nhãn hiển thị) lấy tự động từ địa chỉ điểm làm việc; app cũ còn gửi workingArea thì dùng làm dự phòng
        profile.WorkingArea = WorkingAreas.FromAddress(profile.BaseAddress)
                              ?? (string.IsNullOrWhiteSpace(request.WorkingArea) ? null : request.WorkingArea.Trim());
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
        // FR-31: chiết khấu ưu đãi đang hiệu lực + huy hiệu Tasker xuất sắc tháng
        var preferential = await TaskerAwardService.ActivePreferentialAsync(_db, userId, DateTime.UtcNow);
        var badges = await TaskerAwardService.BadgesAsync(_db, new[] { userId });
        return Ok(ToResponse(profile) with
        {
            CommissionRate = preferential is { } p && p.Rate < _business.CommissionRate ? p.Rate : _business.CommissionRate,
            PreferentialCommissionRate = preferential?.Rate,
            PreferentialUntil = preferential?.Until,
            Badges = badges.GetValueOrDefault(userId) ?? new List<AwardBadgeResponse>(),
        });
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
    public async Task<IActionResult> UpdateAvailability(UpdateAvailabilityRequest request,
        [FromServices] Microsoft.Extensions.Options.IOptions<HomeService.Api.Options.MatchingOptions> matching,
        [FromServices] MatchingService? matcher = null)
    {
        var userId = User.GetUserId();
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == userId);
        if (profile is null) return NotFound();

        // US30: tài khoản đang chờ khóa chỉ được hoàn thành các đơn đã nhận
        if (request.IsAvailable == true && await _db.Users.AnyAsync(u => u.Id == userId && u.LockPendingAt != null))
            throw ApiException.BadRequest("Tài khoản của bạn đang chờ khóa: bạn chỉ hoàn thành các đơn đã nhận, không bật nhận đơn mới được.");

        // Ký quỹ: chỉ bật nhận đơn khi số dư khả dụng đạt mức tối thiểu
        var minDeposit = matching.Value.MinWalletDeposit;
        if (request.IsAvailable == true && !profile.IsAvailable && profile.AvailableBalance < minDeposit)
            throw ApiException.BadRequest(
                $"Cần ký quỹ tối thiểu {MoneyFormat.Vnd(minDeposit)} trong ví để bật nhận đơn. Số dư khả dụng hiện là {MoneyFormat.Vnd(profile.AvailableBalance)}.");

        var turnedOn = request.IsAvailable!.Value && !profile.IsAvailable;
        var hasLocation = request.Latitude is not null && request.Longitude is not null;
        profile.IsAvailable = request.IsAvailable!.Value;
        if (hasLocation)
        {
            profile.LastLatitude = request.Latitude;
            profile.LastLongitude = request.Longitude;
            profile.LastLocationAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        // BR-M08: vừa bật nhận đơn / gửi vị trí mới → đơn đang chờ quét lại ở gần được quét ngay
        if (matcher is not null && profile.IsAvailable && (turnedOn || hasLocation))
            await matcher.WakeNearbyBookingsAsync(userId);
        return NoContent();
    }

    // US16 - Danh sách Tasker đã duyệt, lọc theo nhãn khu vực (ghép nối tự động KHÔNG dùng khu vực mà dùng tọa độ – MatchingService)
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
