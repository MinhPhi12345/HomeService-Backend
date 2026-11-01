using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Controllers;

/// <summary>EP08 – ReportsController (US31, US32 và xem chi tiết).</summary>
public class ReportsControllerTests : TestBase
{
    private ReportsController Admin() => new ReportsController(Db).AsUser(1, UserRole.Admin);

    private Payment FinishedPayment(Booking b, PaymentMethod method, PaymentStatus status, decimal amount, decimal commission,
        decimal refunded, DateTime at)
    {
        var p = new Payment
        {
            BookingId = b.Id, Amount = amount, CommissionAmount = commission, RefundedAmount = refunded,
            TaskerAmount = amount - refunded - commission, Method = method, Status = status,
            PaidAt = at, ReleasedAt = status == PaymentStatus.Success ? null : at,
            RefundedAt = refunded > 0 ? at : null,
        };
        Db.Payments.Add(p);
        Db.SaveChanges();
        return p;
    }

    /// <summary>3 giao dịch đã kết thúc + 1 khoản đang tạm giữ + 1 giao dịch ngoài khoảng thời gian.</summary>
    private void SeedPayments(DateTime day)
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var s = Db.Service();
        FinishedPayment(Db.Booking(customer, s, BookingStatus.Paid, tasker, PaymentMethod.Cash), PaymentMethod.Cash, PaymentStatus.Success, 100_000m, 20_000m, 0m, day);
        FinishedPayment(Db.Booking(customer, s, BookingStatus.Paid, tasker), PaymentMethod.EWallet, PaymentStatus.Released, 200_000m, 40_000m, 0m, day.AddHours(1));
        FinishedPayment(Db.Booking(customer, s, BookingStatus.Paid, tasker), PaymentMethod.Card, PaymentStatus.PartiallyRefunded, 300_000m, 40_000m, 100_000m, day.AddDays(1));
        Db.HeldPayment(Db.Booking(customer, s, BookingStatus.Pending));
        FinishedPayment(Db.Booking(customer, s, BookingStatus.Paid, tasker), PaymentMethod.EWallet, PaymentStatus.Released, 999_000m, 1m, 0m, day.AddDays(-60));
    }

    [Fact]
    public async Task Revenue_TongHopDungSoLieu()
    {
        var day = DateTime.UtcNow.AddDays(-5);
        SeedPayments(day);

        var r = (await Admin().GetRevenue(day.AddDays(-1), DateTime.UtcNow)).OkValue();

        Assert.Equal(3, r.TransactionCount);
        Assert.Equal(500_000m, r.TotalRevenue);          // 100k + 200k + (300k − 100k)
        Assert.Equal(100_000m, r.TotalCommission);
        Assert.Equal(100_000m, r.CashRevenue);
        Assert.Equal(400_000m, r.OnlineRevenue);
        Assert.Equal(150_000m, r.HeldAmount);            // khoản đang tạm giữ (chưa là doanh thu)
        Assert.Equal(100_000m, r.RefundedAmount);
        Assert.Equal(400_000m, r.TaskerPayout);
        Assert.Equal("day", r.GroupBy);
        Assert.Equal(r.TotalRevenue, r.Series.Sum(x => x.Revenue));
    }

    [Fact]
    public async Task Revenue_GomTheoThang()
    {
        SeedPayments(DateTime.UtcNow.AddDays(-5));
        var r = (await Admin().GetRevenue(DateTime.UtcNow.AddDays(-90), DateTime.UtcNow, "MONTH")).OkValue();
        Assert.Equal("month", r.GroupBy);
        Assert.All(r.Series, p => Assert.Matches(@"^\d{4}-\d{2}$", p.Period));
        Assert.Equal(4, r.TransactionCount);
    }

    [Fact]
    public async Task Revenue_MacDinh30NgayGanNhat()
    {
        SeedPayments(DateTime.UtcNow.AddDays(-5));
        var r = (await Admin().GetRevenue(null, null)).OkValue();
        Assert.Equal(3, r.TransactionCount);
        Assert.InRange((r.To - r.From).TotalDays, 29.99, 30.01);
    }

    [Fact]
    public async Task Revenue_ThamSoSai_400()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Admin().GetRevenue(null, null, "week"));
        Assert.Equal("groupBy chỉ nhận day, month hoặc year.", ex.Message);
        var range = await Assert.ThrowsAsync<ApiException>(() => Admin().GetRevenue(DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal("Khoảng thời gian không hợp lệ.", range.Message);
    }

    [Fact]
    public async Task BookingsSummary_DemTheoTrangThai()
    {
        var c = Db.Customer();
        var s = Db.Service();
        Db.Booking(c, s, BookingStatus.Pending);
        Db.Booking(c, s, BookingStatus.Pending);
        Db.Booking(c, s, BookingStatus.Cancelled);
        Db.Booking(c, s, BookingStatus.Paid, configure: b => b.CreatedAt = DateTime.UtcNow.AddDays(-100));

        var list = (await Admin().GetBookingsSummary(null, null)).OkValue().ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal(2, list.Single(x => x.Status == BookingStatus.Pending).Count);
        Assert.Equal(BookingStatus.Pending, list[0].Status);   // sắp theo mã trạng thái
    }

    [Fact]
    public async Task TaskerPerformance_ChiSoTungTasker()
    {
        var c = Db.Customer();
        var s = Db.Service();
        var t1 = Db.Tasker("Tasker giỏi", configure: p => { p.RatingAverage = 4.9m; p.ViolationPoints = 1; });
        Db.Tasker("Chưa nộp hồ sơ", status: TaskerVerificationStatus.Pending, configure: p => p.IdCardNumber = "");

        var paid = Db.Booking(c, s, BookingStatus.Paid, t1);
        Db.Payments.Add(new Payment { BookingId = paid.Id, Amount = 100_000m, TaskerAmount = 80_000m, CommissionAmount = 20_000m, Status = PaymentStatus.Released, Method = PaymentMethod.EWallet });
        Db.SaveChanges();
        Db.Booking(c, s, BookingStatus.Completed, t1);
        Db.Booking(c, s, BookingStatus.Cancelled, t1);
        var noShow = Db.Booking(c, s, BookingStatus.Accepted, t1, configure: b => b.IsTaskerNoShow = true);
        Db.Dispute(noShow);
        Db.Offer(paid, t1, OfferStatus.Accepted);
        Db.Offer(noShow, t1, OfferStatus.Rejected);

        var list = (await Admin().GetTaskerPerformance(null, null)).OkValue().ToList();

        var item = Assert.Single(list);
        Assert.Equal("Tasker giỏi", item.TaskerName);
        Assert.Equal(2, item.CompletedCount);
        Assert.Equal(1, item.CancelledCount);
        Assert.Equal(2, item.TotalOffers);
        Assert.Equal(1, item.AcceptedOffers);
        Assert.Equal(0.5m, item.AcceptanceRate);
        Assert.Equal(1, item.NoShowCount);
        Assert.Equal(1, item.DisputeCount);
        Assert.Equal(1, item.ViolationPoints);
        Assert.Equal(80_000m, item.TotalEarnings);
    }

    [Fact]
    public async Task TaskerPerformance_KhoangThoiGianSai_400()
    {
        await Assert.ThrowsAsync<ApiException>(() => Admin().GetTaskerPerformance(DateTime.UtcNow, DateTime.UtcNow.AddDays(-2)));
    }

    [Fact]
    public async Task RevenueTransactions_XemChiTietMotCotBieuDo()
    {
        var day = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 3, 0, 0, DateTimeKind.Utc);
        if (day > DateTime.UtcNow.AddDays(-1)) day = day.AddMonths(-1);
        SeedPayments(day);
        var period = day.AddHours(7).ToString("yyyy-MM-dd");

        var page = (await Admin().GetRevenueTransactions(day.AddDays(-2), DateTime.UtcNow, period, 1, 10)).OkValue();

        Assert.Equal(2, page.TotalCount);                     // 2 giao dịch trong ngày đó
        Assert.All(page.Items, x => Assert.Equal(x.Amount - x.RefundedAmount, x.Revenue));
        Assert.Equal(200_000m, page.Items[0].Amount);         // mới nhất trước
    }

    [Fact]
    public async Task Bookings_XemChiTietTheoTrangThaiTuKhoaVaTasker()
    {
        var c = Db.Customer("Võ Thị Mai");
        var t = Db.Tasker("Tasker X");
        var s = Db.Service("Giặt ủi");
        Db.Booking(c, s, BookingStatus.Paid, t);
        Db.Booking(c, s, BookingStatus.Cancelled);
        Db.Booking(Db.Customer("Người khác"), Db.Service("Nấu ăn"), BookingStatus.Paid, t,
            configure: b => b.CreatedAt = DateTime.UtcNow.AddDays(-200));

        Assert.Single((await Admin().GetBookings(null, null, BookingStatus.Cancelled, null, null, null, null)).OkValue().Items);
        Assert.Equal(2, (await Admin().GetBookings(null, null, null, null, "Mai", null, null)).OkValue().TotalCount);
        var byTasker = (await Admin().GetBookings(null, null, null, t.Id, null, 1, 20)).OkValue();
        Assert.Equal(2, byTasker.TotalCount);                 // lọc theo Tasker không giới hạn ngày tạo
        Assert.All(byTasker.Items, x => Assert.Equal("Tasker X", x.TaskerName));
    }
}

/// <summary>NFR-16 – AuditLogsController.</summary>
public class AuditLogsControllerTests : TestBase
{
    private AuditLogsController Admin() => new AuditLogsController(Db).AsUser(1, UserRole.Admin);

    [Fact]
    public async Task GetAll_LocVaHienTenNguoiThucHien()
    {
        var admin = Db.Admin("Admin Phi");
        Db.AuditLogs.AddRange(
            new AuditLog { Action = AuditService.Released, Amount = 100m, BookingId = 1 },
            new AuditLog { Action = AuditService.Refund, Amount = 50m, BookingId = 2, ActorUserId = admin.Id },
            new AuditLog { Action = AuditService.Refund, Amount = 70m, BookingId = 3, ActorUserId = 9999 });
        Db.SaveChanges();

        var all = (await Admin().GetAll(null, null, null, null, null, null)).OkValue();
        Assert.Equal(3, all.TotalCount);
        Assert.Contains(all.Items, x => x.ActorName == "Hệ thống");
        Assert.Contains(all.Items, x => x.ActorName == "Admin Phi");
        Assert.Contains(all.Items, x => x.ActorName == "Người dùng #9999");

        Assert.Equal(2, (await Admin().GetAll(" Refund ", null, null, null, null, null)).OkValue().TotalCount);
        Assert.Equal(1, (await Admin().GetAll(null, 2, null, null, null, null)).OkValue().TotalCount);
    }

    [Fact]
    public async Task GetAll_DenNgayChiCoNgay_LayHetNgayDo()
    {
        var today = DateTime.UtcNow.Date;
        Db.AuditLogs.Add(new AuditLog { Action = "X", Amount = 1m, CreatedAt = today.AddHours(23) });
        Db.AuditLogs.Add(new AuditLog { Action = "X", Amount = 1m, CreatedAt = today.AddDays(-3) });
        Db.SaveChanges();

        var page = (await Admin().GetAll(null, null, today, DateTime.SpecifyKind(today, DateTimeKind.Utc), null, null)).OkValue();
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetAll_TuNgaySauDenNgay_400()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Admin().GetAll(null, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), null, null));
        Assert.Equal("Khoảng thời gian không hợp lệ.", ex.Message);
    }
}
