using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>FR-10 – phí hủy (BR-C01) và hủy đơn (CancellationService).</summary>
public class CancellationServiceTests : TestBase
{
    private Booking AcceptedBooking(User tasker, double hoursUntil, PaymentMethod method = PaymentMethod.EWallet, decimal price = 200_000m) =>
        Db.Booking(Db.Customer(), Db.Service(price: price), BookingStatus.Accepted, tasker, method,
            b => b.ScheduledTime = DateTime.UtcNow.AddHours(hoursUntil));

    [Theory]
    [InlineData(BookingStatus.AwaitingPayment)]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Matched)]
    [InlineData(BookingStatus.NoTaskerFound)]
    public async Task PhiHuy_ChuaCoTaskerNhan_MienPhi(BookingStatus status)
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), status);
        var (fee, reason) = await CreateCancellation().CalculateFeeAsync(booking);
        Assert.Equal(0m, fee);
        Assert.Contains("miễn phí", reason);
    }

    [Fact]
    public async Task PhiHuy_ConTu2GioTroLen_MienPhi()
    {
        var booking = AcceptedBooking(Db.Tasker(), 5);
        Assert.Equal(0m, (await CreateCancellation().CalculateFeeAsync(booking)).Fee);
    }

    [Fact]
    public async Task PhiHuy_ConDuoi2Gio_20PhanTram()
    {
        var booking = AcceptedBooking(Db.Tasker(), 1.5);
        var (fee, reason) = await CreateCancellation().CalculateFeeAsync(booking);
        Assert.Equal(40_000m, fee);
        Assert.Contains("20%", reason);
    }

    [Fact]
    public async Task PhiHuy_TaskerDaDenNoi_50PhanTram()
    {
        var tasker = Db.Tasker(configure: p =>
        {
            p.LastLatitude = Seed.BenThanhLat + 0.0005;   // ~55 m
            p.LastLongitude = Seed.BenThanhLng;
            p.LastLocationAt = DateTime.UtcNow.AddMinutes(-2);
        });
        var booking = AcceptedBooking(tasker, 5);
        var (fee, reason) = await CreateCancellation().CalculateFeeAsync(booking);
        Assert.Equal(100_000m, fee);
        Assert.Contains("50%", reason);
    }

    [Fact]
    public async Task PhiHuy_ViTriTaskerDaCu_KhongTinhLaDaDen()
    {
        var tasker = Db.Tasker(configure: p =>
        {
            p.LastLatitude = Seed.BenThanhLat;
            p.LastLongitude = Seed.BenThanhLng;
            p.LastLocationAt = DateTime.UtcNow.AddMinutes(-45);   // quá 30 phút
        });
        var booking = AcceptedBooking(tasker, 5);
        Assert.Equal(0m, (await CreateCancellation().CalculateFeeAsync(booking)).Fee);
    }

    [Fact]
    public async Task PhiHuy_TaskerConXa_KhongTinhLaDaDen()
    {
        var tasker = Db.Tasker(configure: p =>
        {
            p.LastLatitude = Seed.Q3Lat;
            p.LastLongitude = Seed.Q3Lng;
            p.LastLocationAt = DateTime.UtcNow;
        });
        var booking = AcceptedBooking(tasker, 1);
        Assert.Equal(40_000m, (await CreateCancellation().CalculateFeeAsync(booking)).Fee); // chỉ phí hủy muộn 20%
    }

    [Fact]
    public async Task PhiHuy_TaskerKhongDen_MienPhi()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, Db.Tasker(), configure: b =>
        {
            b.IsTaskerNoShow = true;
            b.ScheduledTime = DateTime.UtcNow.AddMinutes(-40);
        });
        var (fee, reason) = await CreateCancellation().CalculateFeeAsync(booking);
        Assert.Equal(0m, fee);
        Assert.Contains("không đến", reason);
    }

    [Fact]
    public void RefundIfCancelled_ChiHoanKhiDangTamGiu()
    {
        var held = new Booking { Payment = new Payment { Amount = 200_000m, Status = PaymentStatus.Held } };
        Assert.Equal(160_000m, CancellationService.RefundIfCancelled(held, 40_000m));
        Assert.Equal(0m, CancellationService.RefundIfCancelled(held, 500_000m));
        Assert.Equal(0m, CancellationService.RefundIfCancelled(new Booking(), 0m));
        var released = new Booking { Payment = new Payment { Amount = 200_000m, Status = PaymentStatus.Released } };
        Assert.Equal(0m, CancellationService.RefundIfCancelled(released, 0m));
    }

    [Fact]
    public void CancellableStatuses_DungTheoBRB04()
    {
        Assert.Equal(5, CancellationService.CancellableStatuses.Length);
        Assert.DoesNotContain(BookingStatus.InProgress, CancellationService.CancellableStatuses);
        Assert.DoesNotContain(BookingStatus.Completed, CancellationService.CancellableStatuses);
    }

    [Fact]
    public async Task Cancel_DonOnlineChuaCoTasker_HoanToanBoVaDongLoiMoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(price: 150_000m), BookingStatus.Matched);
        Db.HeldPayment(booking);
        var offer = Db.Offer(booking, Db.Tasker());

        await CreateCancellation().CancelAsync(booking, "Khách hàng đã hủy đơn.");

        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(0m, b.CancellationFee);
        Assert.Equal(PaymentStatus.Refunded, (await fresh.Payments.SingleAsync()).Status);
        Assert.Equal(OfferStatus.Closed, (await fresh.BookingOffers.SingleAsync(o => o.Id == offer.Id)).Status);
    }

    [Fact]
    public async Task Cancel_DonOnlineHuyMuon_TaskerNhanPhiHuy()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = AcceptedBooking(tasker, 1);
        Db.HeldPayment(booking);

        await CreateCancellation().CancelAsync(booking, "Khách hủy");

        var fresh = Fresh();
        Assert.Equal(40_000m, (await fresh.Bookings.SingleAsync()).CancellationFee);
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(160_000m, p.RefundedAmount);
        Assert.Equal(40_000m, p.TaskerAmount);
        Assert.Equal(40_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        Assert.Contains(await fresh.Notifications.Where(n => n.UserId == tasker.Id).Select(n => n.Title).ToListAsync(), t => t == "Đơn đã bị hủy");
    }

    [Fact]
    public async Task Cancel_DonTienMatHuyMuon_GhiNoPhiHuyVaGiaiPhongTamGiu()
    {
        var tasker = Db.Tasker(configure: p => p.ReservedAmount = 40_000m);
        var booking = AcceptedBooking(tasker, 1, PaymentMethod.Cash);

        await CreateCancellation().CancelAsync(booking, "Khách hủy");

        var fresh = Fresh();
        Assert.Equal(40_000m, (await fresh.Users.SingleAsync(u => u.Id == booking.CustomerId)).OutstandingAmount);
        Assert.Equal(0m, (await fresh.TaskerProfiles.SingleAsync()).ReservedAmount);
        Assert.Equal(AuditService.Debt, (await fresh.AuditLogs.SingleAsync()).Action);
        Assert.Empty(await fresh.Payments.ToListAsync());
    }

    [Fact]
    public async Task Cancel_DoHeThong_ThongBaoChoKhach()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Pending, method: PaymentMethod.Cash);
        await CreateCancellation().CancelAsync(booking, "Hệ thống tự hủy.", bySystem: true);
        var n = await Fresh().Notifications.SingleAsync();
        Assert.Equal(booking.CustomerId, n.UserId);
        Assert.Contains("Hệ thống tự hủy.", n.Message);
    }
}
