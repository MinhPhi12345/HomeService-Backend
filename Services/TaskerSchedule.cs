using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

/// <summary>
/// BR-B02 (SRS v1.8) – chống trùng lịch Tasker theo thời lượng thật:
/// mỗi đơn chiếm [giờ hẹn, giờ hẹn + thời lượng ước tính + phút di chuyển); hai đơn của cùng Tasker không được chồng nhau.
/// </summary>
public static class TaskerSchedule
{
    /// <summary>Thời lượng tối đa của một đơn (giới hạn khoảng truy vấn).</summary>
    public const int MaxEstimatedMinutes = 24 * 60;

    public static DateTime EndOf(DateTime start, int estimatedMinutes, int travelMinutes) =>
        start.AddMinutes(Math.Max(0, estimatedMinutes) + Math.Max(0, travelMinutes));

    public static bool Overlaps(DateTime aStart, int aMinutes, DateTime bStart, int bMinutes, int travelMinutes) =>
        aStart < EndOf(bStart, bMinutes, travelMinutes) && bStart < EndOf(aStart, aMinutes, travelMinutes);

    public sealed record Conflict(int TaskerId, int BookingId, DateTime ScheduledTime, int EstimatedMinutes);

    /// <summary>Các đơn Accepted/InProgress của những Tasker này chồng lịch với đơn đang xét.</summary>
    public static async Task<List<Conflict>> FindConflictsAsync(AppDbContext db, IReadOnlyCollection<int> taskerIds,
        Booking booking, int travelMinutes, CancellationToken ct = default)
    {
        if (taskerIds.Count == 0) return new();
        var start = booking.ScheduledTime;
        var end = EndOf(start, booking.EstimatedMinutes, travelMinutes);
        var earliest = start.AddMinutes(-(MaxEstimatedMinutes + travelMinutes)); // lọc thô trong SQL, so khớp chính xác ở bộ nhớ
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.TaskerId != null && taskerIds.Contains(b.TaskerId.Value) && b.Id != booking.Id
                        && (b.Status == BookingStatus.Accepted || b.Status == BookingStatus.InProgress)
                        && b.ScheduledTime < end && b.ScheduledTime > earliest)
            .Select(b => new Conflict(b.TaskerId!.Value, b.Id, b.ScheduledTime, b.EstimatedMinutes))
            .ToListAsync(ct);
        return rows.Where(r => Overlaps(start, booking.EstimatedMinutes, r.ScheduledTime, r.EstimatedMinutes, travelMinutes)).ToList();
    }
}
