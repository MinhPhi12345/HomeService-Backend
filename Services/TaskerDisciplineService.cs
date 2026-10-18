using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Options;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// BR-D02 – điểm vi phạm Tasker: không đến +1, khiếu nại được chấp nhận +1, báo sai "khách không thanh toán" +2.
/// Đủ 3 điểm → tạm khóa nhận đơn 7 ngày (SuspendedUntil) và điểm về 0.
/// </summary>
public class TaskerDisciplineService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly SettlementOptions _opt;

    public TaskerDisciplineService(AppDbContext db, INotificationService notifications, IOptions<SettlementOptions> options)
    {
        _db = db;
        _notifications = notifications;
        _opt = options.Value;
    }

    public async Task AddViolationAsync(int taskerId, int points, string reason)
    {
        var profile = await _db.TaskerProfiles.FirstOrDefaultAsync(t => t.UserId == taskerId);
        if (profile is null) return;

        profile.ViolationPoints += points;
        _notifications.Add(taskerId, "Bạn bị cộng điểm vi phạm",
            $"+{points} điểm vi phạm: {reason}. Điểm hiện tại: {profile.ViolationPoints}/{_opt.ViolationPointsToSuspend}.");

        if (profile.ViolationPoints >= _opt.ViolationPointsToSuspend)
        {
            var until = DateTime.UtcNow.AddDays(_opt.SuspendDays);
            profile.SuspendedUntil = until;
            profile.ViolationPoints = 0;
            _notifications.Add(taskerId, "Tài khoản bị tạm khóa nhận đơn",
                $"Bạn đã đủ {_opt.ViolationPointsToSuspend} điểm vi phạm nên bị tạm khóa nhận đơn đến {VnTime.Format(until)}.");
        }
    }
}
