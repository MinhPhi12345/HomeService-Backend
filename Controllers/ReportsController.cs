using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP08 - Quản trị & Báo cáo (US31-US32). from/to: chuỗi ISO UTC; bỏ trống → 30 ngày gần nhất.
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class ReportsController : ControllerBase
{
    private static readonly PaymentStatus[] FinishedStatuses =
        { PaymentStatus.Success, PaymentStatus.Released, PaymentStatus.PartiallyRefunded };

    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    // US31 - Báo cáo doanh thu theo khoảng thời gian, kèm chuỗi số liệu theo ngày/tháng/năm (?groupBy=day|month|year)
    [HttpGet("revenue")]
    public async Task<ActionResult<RevenueReport>> GetRevenue([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? groupBy = "day")
    {
        var (f, t) = Range(from, to);
        var group = (groupBy ?? "day").Trim().ToLowerInvariant();
        if (group is not ("day" or "month" or "year"))
            throw ApiException.BadRequest("groupBy chỉ nhận day, month hoặc year.");

        // Giao dịch đã kết thúc, ghi nhận theo thời điểm tiền được chia (giải ngân / hoàn / thu tiền mặt)
        var finished = await _db.Payments.AsNoTracking()
            .Where(p => FinishedStatuses.Contains(p.Status))
            .Select(p => new
            {
                p.Amount, p.RefundedAmount, p.CommissionAmount, p.TaskerAmount, p.Method,
                At = p.ReleasedAt ?? p.RefundedAt ?? p.PaidAt,
            })
            .ToListAsync();
        var inRange = finished.Where(p => p.At >= f && p.At <= t).ToList();

        var held = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Held && p.PaidAt <= t)
            .Select(p => p.Amount).ToListAsync();
        var refunded = await _db.Payments.AsNoTracking()
            .Where(p => p.RefundedAmount > 0 && p.RefundedAt >= f && p.RefundedAt <= t)
            .Select(p => p.RefundedAmount).ToListAsync();

        var series = inRange
            .GroupBy(p => Period(p.At!.Value, group))
            .OrderBy(g => g.Key)
            .Select(g => new RevenuePoint(g.Key, g.Sum(x => x.Amount - x.RefundedAmount), g.Sum(x => x.CommissionAmount), g.Count()))
            .ToList();

        return Ok(new RevenueReport(
            f, t,
            inRange.Sum(p => p.Amount - p.RefundedAmount),
            inRange.Sum(p => p.CommissionAmount),
            inRange.Count,
            inRange.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount - p.RefundedAmount),
            inRange.Where(p => p.Method != PaymentMethod.Cash).Sum(p => p.Amount - p.RefundedAmount),
            held.Sum(),
            refunded.Sum(),
            inRange.Sum(p => p.TaskerAmount),
            group,
            series));
    }

    // US32 - Số lượng đơn theo trạng thái (status là SỐ) trong khoảng ngày tạo đơn
    [HttpGet("bookings-summary")]
    public async Task<ActionResult<IEnumerable<BookingSummaryItem>>> GetBookingsSummary([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (f, t) = Range(from, to);
        var summary = await _db.Bookings.AsNoTracking()
            .Where(b => b.CreatedAt >= f && b.CreatedAt <= t)
            .GroupBy(b => b.Status)
            .Select(g => new BookingSummaryItem(g.Key, g.Count()))
            .ToListAsync();
        return Ok(summary.OrderBy(x => x.Status));
    }

    // US32 - Hiệu suất từng Tasker (lọc tùy chọn theo ngày hẹn from/to)
    [HttpGet("tasker-performance")]
    public async Task<ActionResult<IEnumerable<TaskerPerformanceItem>>> GetTaskerPerformance([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        DateTime? f = from is null ? null : DateTimeHelper.ToUtc(from.Value);
        DateTime? t = to is null ? null : DateTimeHelper.ToUtc(to.Value);
        if (f is not null && t is not null && f > t) throw ApiException.BadRequest("Khoảng thời gian không hợp lệ.");

        var taskers = await _db.TaskerProfiles.AsNoTracking()
            .Where(p => p.VerificationStatus == TaskerVerificationStatus.Approved || p.IdCardNumber != "")
            .Select(p => new { p.UserId, p.User!.FullName, p.RatingAverage, p.ViolationPoints })
            .ToListAsync();

        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.TaskerId != null && (f == null || b.ScheduledTime >= f) && (t == null || b.ScheduledTime <= t))
            .Select(b => new { TaskerId = b.TaskerId!.Value, b.Status, b.IsTaskerNoShow, HasDispute = b.Dispute != null,
                               TaskerAmount = b.Payment != null ? b.Payment.TaskerAmount : 0m, b.PaymentMethod })
            .ToListAsync();
        var offers = await _db.BookingOffers.AsNoTracking()
            .Where(o => (f == null || o.SentAt >= f) && (t == null || o.SentAt <= t))
            .GroupBy(o => o.TaskerId)
            .Select(g => new { TaskerId = g.Key, Total = g.Count(), Accepted = g.Count(o => o.Status == OfferStatus.Accepted) })
            .ToListAsync();

        var result = taskers.Select(tk =>
        {
            var mine = bookings.Where(b => b.TaskerId == tk.UserId).ToList();
            var of = offers.FirstOrDefault(o => o.TaskerId == tk.UserId);
            return new TaskerPerformanceItem(
                tk.UserId,
                mine.Count(b => b.Status is BookingStatus.Completed or BookingStatus.Paid),
                mine.Count(b => b.Status is BookingStatus.Cancelled or BookingStatus.Rejected),
                tk.FullName,
                tk.RatingAverage,
                of?.Accepted ?? 0,
                of?.Total ?? 0,
                of is null || of.Total == 0 ? null : Math.Round((decimal)of.Accepted / of.Total, 4),
                mine.Count(b => b.IsTaskerNoShow),
                mine.Count(b => b.HasDispute),
                tk.ViolationPoints,
                // Thu nhập: tiền online được giải ngân + phí hủy (tiền mặt Tasker đã giữ, không tính vào đây)
                mine.Where(b => b.PaymentMethod != PaymentMethod.Cash).Sum(b => b.TaskerAmount));
        })
        .OrderByDescending(x => x.CompletedCount).ThenByDescending(x => x.RatingAverage)
        .ToList();

        return Ok(result);
    }

    // Drill-down US31 – các giao dịch tạo nên doanh thu. ?period=2026-10-16 | 2026-10 | 2026 (giờ VN) để xem 1 cột biểu đồ
    [HttpGet("revenue/transactions")]
    public async Task<ActionResult<PagedResult<RevenueTransactionItem>>> GetRevenueTransactions([FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] string? period, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (f, t) = Range(from, to);
        var (p, size) = Paging.Normalize(page, pageSize);
        var rows = await _db.Payments.AsNoTracking()
            .Where(x => FinishedStatuses.Contains(x.Status))
            .Select(x => new
            {
                x.Id, x.BookingId, Service = x.Booking!.ServiceCategory!.Name, Customer = x.Booking.Customer!.FullName,
                Tasker = x.Booking.Tasker != null ? x.Booking.Tasker.FullName : null,
                x.Method, x.Status, x.Amount, x.RefundedAmount, x.CommissionAmount, x.TaskerAmount,
                At = x.ReleasedAt ?? x.RefundedAt ?? x.PaidAt,
            })
            .ToListAsync();

        var filtered = rows.Where(x => x.At >= f && x.At <= t);
        if (!string.IsNullOrWhiteSpace(period))
        {
            var key = period.Trim();
            var group = key.Length switch { 4 => "year", 7 => "month", _ => "day" };
            filtered = filtered.Where(x => Period(x.At!.Value, group) == key);
        }
        var list = filtered.OrderByDescending(x => x.At).ToList();
        var items = list.Skip((p - 1) * size).Take(size)
            .Select(x => new RevenueTransactionItem(x.Id, x.BookingId, x.Service, x.Customer, x.Tasker, x.Method, x.Status,
                x.Amount, x.RefundedAmount, x.Amount - x.RefundedAmount, x.CommissionAmount, x.TaskerAmount, x.At!.Value))
            .ToList();
        return Ok(new PagedResult<RevenueTransactionItem>(items, p, size, list.Count));
    }

    // Drill-down US32 – danh sách đơn theo ngày tạo, lọc ?status= (số) hoặc ?taskerId= (đơn của 1 Tasker theo ngày hẹn)
    [HttpGet("bookings")]
    public async Task<ActionResult<PagedResult<ReportBookingItem>>> GetBookings([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] BookingStatus? status, [FromQuery] int? taskerId, [FromQuery] string? keyword,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var query = _db.Bookings.AsNoTracking().AsQueryable();
        if (taskerId is not null)
        {
            // Hiệu suất Tasker không giới hạn ngày khi không truyền from/to
            query = query.Where(b => b.TaskerId == taskerId);
            if (from is not null) { var f0 = DateTimeHelper.ToUtc(from.Value); query = query.Where(b => b.ScheduledTime >= f0); }
            if (to is not null) { var t0 = DateTimeHelper.ToUtc(to.Value); query = query.Where(b => b.ScheduledTime <= t0); }
        }
        else
        {
            var (f, t) = Range(from, to);
            query = query.Where(b => b.CreatedAt >= f && b.CreatedAt <= t);
        }
        if (status is not null) query = query.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(b => b.Customer!.FullName.Contains(kw) || b.Address.Contains(kw)
                || b.ServiceCategory!.Name.Contains(kw) || (b.Tasker != null && b.Tasker.FullName.Contains(kw)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(b => new ReportBookingItem(b.Id, b.ServiceCategory!.Name, b.Customer!.FullName,
                b.Tasker != null ? b.Tasker.FullName : null, b.Status, b.PaymentMethod, b.TotalPrice,
                b.ScheduledTime, b.CreatedAt, b.Address))
            .ToListAsync();
        return Ok(new PagedResult<ReportBookingItem>(items, p, size, total));
    }

    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var t = to is null ? DateTime.UtcNow : DateTimeHelper.ToUtc(to.Value);
        var f = from is null ? t.AddDays(-30) : DateTimeHelper.ToUtc(from.Value);
        if (f > t) throw ApiException.BadRequest("Khoảng thời gian không hợp lệ.");
        return (f, t);
    }

    /// <summary>Nhóm theo giờ Việt Nam (UTC+7): "2026-10-16", "2026-10", "2026".</summary>
    private static string Period(DateTime utc, string group)
    {
        var vn = utc.AddHours(7);
        return group switch
        {
            "year" => vn.ToString("yyyy"),
            "month" => vn.ToString("yyyy-MM"),
            _ => vn.ToString("yyyy-MM-dd"),
        };
    }
}
