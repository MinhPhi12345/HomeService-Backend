using HomeService.Api.Enums;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>NFR-17 – các tác vụ tự động theo thời hạn do BookingWorker gọi (BookingJobs).</summary>
public class BookingJobsTests : TestBase
{
    // ---------- Hết hạn thanh toán (BR-P08) ----------

    [Fact]
    public async Task ExpireUnpaid_QuaHan_TuHuyVaThongBao()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.AwaitingPayment,
            configure: b => b.PaymentDueAt = DateTime.UtcNow.AddMinutes(-1));

        await CreateJobs().ExpireUnpaidBookingsAsync();

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Cancelled, (await fresh.Bookings.SingleAsync()).Status);
        Assert.Contains("quá hạn thanh toán", (await fresh.Notifications.SingleAsync()).Message);
    }

    [Fact]
    public async Task ExpireUnpaid_ConHan_GiuNguyen()
    {
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.AwaitingPayment, configure: b => b.PaymentDueAt = DateTime.UtcNow.AddMinutes(5));
        await CreateJobs().ExpireUnpaidBookingsAsync();
        Assert.Equal(BookingStatus.AwaitingPayment, (await Fresh().Bookings.SingleAsync()).Status);
    }

    // ---------- Tự xác nhận hoàn thành (FR-25) ----------

    [Fact]
    public async Task AutoConfirm_NhacKhachTruocHan2Gio_MotLan()
    {
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Completed, Db.Tasker(),
            configure: b => b.AutoConfirmAt = DateTime.UtcNow.AddMinutes(90));

        await CreateJobs().AutoConfirmAsync();
        await CreateJobs().AutoConfirmAsync();   // chạy lại không nhắc lần 2

        var fresh = Fresh();
        Assert.Equal("Sắp tự xác nhận hoàn thành", (await fresh.Notifications.SingleAsync()).Title);
        Assert.NotNull((await fresh.Bookings.SingleAsync()).AutoConfirmReminderSentAt);
    }

    [Fact]
    public async Task AutoConfirm_HetHan_GiaiNganTuDong_MotLanDuyNhat()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 100_000m), BookingStatus.Completed, tasker,
            configure: b => b.AutoConfirmAt = DateTime.UtcNow.AddMinutes(-1));
        Db.HeldPayment(booking);

        await CreateJobs().AutoConfirmAsync();
        await CreateJobs().AutoConfirmAsync();   // idempotent

        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Paid, b.Status);
        Assert.True(b.IsAutoConfirmed);
        Assert.Equal(80_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        Assert.Equal(1, await fresh.WalletTransactions.CountAsync());
    }

    [Fact]
    public async Task AutoConfirm_DonDangKhieuNai_KhongGiaiNgan()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Disputed, Db.Tasker(wallet: 0m),
            configure: b => b.AutoConfirmAt = DateTime.UtcNow.AddMinutes(-1));
        Db.HeldPayment(booking);

        await CreateJobs().AutoConfirmAsync();

        Assert.Equal(PaymentStatus.Held, (await Fresh().Payments.SingleAsync()).Status);
    }

    // ---------- Hết hạn đơn chưa có Tasker (BR-M05) ----------

    [Fact]
    public async Task ExpireNoTaskerFound_Qua24Gio_HetHanVaHoan100()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.NoTaskerFound,
            configure: b => { b.UpdatedAt = DateTime.UtcNow.AddHours(-25); b.ScheduledTime = DateTime.UtcNow.AddDays(2); });
        Db.HeldPayment(booking);

        await CreateJobs().ExpireNoTaskerFoundAsync();

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Expired, (await fresh.Bookings.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Refunded, (await fresh.Payments.SingleAsync()).Status);
    }

    [Fact]
    public async Task ExpireNoTaskerFound_DaQuaGioHen_HetHan()
    {
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.NoTaskerFound,
            configure: b => { b.UpdatedAt = DateTime.UtcNow; b.ScheduledTime = DateTime.UtcNow.AddMinutes(-1); });
        await CreateJobs().ExpireNoTaskerFoundAsync();
        Assert.Equal(BookingStatus.Expired, (await Fresh().Bookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task ExpireNoTaskerFound_ConTrongHan_GiuNguyen()
    {
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.NoTaskerFound,
            configure: b => { b.UpdatedAt = DateTime.UtcNow.AddHours(-2); b.ScheduledTime = DateTime.UtcNow.AddDays(1); });
        await CreateJobs().ExpireNoTaskerFoundAsync();
        Assert.Equal(BookingStatus.NoTaskerFound, (await Fresh().Bookings.SingleAsync()).Status);
    }

    // ---------- Tasker không đến (FR-28) ----------

    [Fact]
    public async Task DetectNoShow_Qua30Phut_DanhDauVaCongViPham()
    {
        var tasker = Db.Tasker(configure: p => p.ReservedAmount = 30_000m);
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, tasker, PaymentMethod.Cash,
            b => b.ScheduledTime = DateTime.UtcNow.AddMinutes(-31));

        await CreateJobs().DetectNoShowAsync();

        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.True(b.IsTaskerNoShow);
        Assert.NotNull(b.NoShowAt);
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(1, p.ViolationPoints);
        Assert.Equal(0m, p.ReservedAmount);
        Assert.Contains(await fresh.Notifications.Select(n => n.Title).ToListAsync(), t => t == "Tasker chưa đến");
    }

    [Fact]
    public async Task DetectNoShow_ChuaDen30Phut_KhongDanhDau()
    {
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, Db.Tasker(), configure: b => b.ScheduledTime = DateTime.UtcNow.AddMinutes(-10));
        await CreateJobs().DetectNoShowAsync();
        Assert.False((await Fresh().Bookings.SingleAsync()).IsTaskerNoShow);
    }

    [Fact]
    public async Task DetectNoShow_KhachKhongChonSau2Gio_TuHuyHoan100()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, Db.Tasker(wallet: 0m), configure: b =>
        {
            b.ScheduledTime = DateTime.UtcNow.AddHours(-3);
            b.IsTaskerNoShow = true;
            b.NoShowAt = DateTime.UtcNow.AddHours(-2).AddMinutes(-1);
        });
        Db.HeldPayment(booking);

        await CreateJobs().DetectNoShowAsync();

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Cancelled, (await fresh.Bookings.SingleAsync()).Status);
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(booking.TotalPrice, p.RefundedAmount);
    }

    // ---------- Nhắc lịch (US29) ----------

    [Fact]
    public async Task Reminders_NhacKhachVaTaskerMotLan()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        Db.Booking(customer, Db.Service(), BookingStatus.Accepted, tasker, configure: b => b.ScheduledTime = DateTime.UtcNow.AddMinutes(45));

        await CreateJobs().SendAppointmentRemindersAsync();
        Db.ChangeTracker.Clear();
        await CreateJobs().SendAppointmentRemindersAsync();

        var receivers = await Fresh().Notifications.Select(n => n.UserId).ToListAsync();
        Assert.Equal(2, receivers.Count);
        Assert.Contains(customer.Id, receivers);
        Assert.Contains(tasker.Id, receivers);
    }

    [Fact]
    public async Task Reminders_ConXaHon60Phut_ChuaNhac()
    {
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, Db.Tasker(), configure: b => b.ScheduledTime = DateTime.UtcNow.AddHours(2));
        await CreateJobs().SendAppointmentRemindersAsync();
        Assert.Empty(await Fresh().Notifications.ToListAsync());
    }
}
