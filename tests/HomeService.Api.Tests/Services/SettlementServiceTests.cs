using HomeService.Api.Enums;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>FR-16 / FR-25 – dòng tiền: giải ngân online, hoàn tiền, quyết toán tiền mặt (SettlementService).</summary>
public class SettlementServiceTests : TestBase
{
    [Theory]
    [InlineData("150000", "0", "0.2", "30000", "120000")]
    [InlineData("150000", "50000", "0.2", "20000", "80000")]
    [InlineData("99999", "0", "0.2", "20000", "79999")]
    [InlineData("100000", "100000", "0.2", "0", "0")]
    public void SplitOnline_ChietKhauTrenPhanConLai(string amount, string refunded, string rate, string commission, string tasker)
    {
        var (c, t) = SettlementService.SplitOnline(decimal.Parse(amount), decimal.Parse(refunded), decimal.Parse(rate));
        Assert.Equal(decimal.Parse(commission), c);
        Assert.Equal(decimal.Parse(tasker), t);
        Assert.Equal(decimal.Parse(amount) - decimal.Parse(refunded), c + t);
    }

    [Fact]
    public async Task ReleaseOnline_CongViTaskerVaGhiNhatKy()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 200_000m), BookingStatus.Completed, tasker);
        Db.HeldPayment(booking);

        var released = await CreateSettlement().ReleaseOnlineAsync(booking, isAuto: false);
        await Db.SaveChangesAsync();

        Assert.True(released);
        var fresh = Fresh();
        var payment = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Released, payment.Status);
        Assert.Equal(40_000m, payment.CommissionAmount);
        Assert.Equal(160_000m, payment.TaskerAmount);
        Assert.NotNull(payment.ReleasedAt);
        Assert.Equal(160_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        var tx = await fresh.WalletTransactions.SingleAsync();
        Assert.Equal(WalletTransactionType.OnlineEarning, tx.Type);
        Assert.Equal(AuditService.Released, (await fresh.AuditLogs.SingleAsync()).Action);
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
        Assert.False((await fresh.Bookings.SingleAsync()).IsAutoConfirmed);
    }

    [Fact]
    public async Task ReleaseOnline_DungTyLeDaChotTrenDon()
    {
        BusinessOptions.CommissionRate = 0.5m; // cấu hình mới không ảnh hưởng đơn cũ
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 100_000m), BookingStatus.Completed, tasker,
            configure: b => b.CommissionRate = 0.1m);
        Db.HeldPayment(booking);

        await CreateSettlement().ReleaseOnlineAsync(booking, isAuto: true);
        await Db.SaveChangesAsync();

        Assert.Equal(10_000m, (await Fresh().Payments.SingleAsync()).CommissionAmount);
        Assert.True((await Fresh().Bookings.SingleAsync()).IsAutoConfirmed);
    }

    [Fact]
    public async Task ReleaseOnline_DaHoanMotPhan_TrangThaiPartiallyRefunded()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 200_000m), BookingStatus.Disputed, tasker);
        var payment = Db.HeldPayment(booking);
        payment.RefundedAmount = 50_000m;
        Db.SaveChanges();

        await CreateSettlement().ReleaseOnlineAsync(booking, false);
        await Db.SaveChangesAsync();

        var p = await Fresh().Payments.SingleAsync();
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(30_000m, p.CommissionAmount);
        Assert.Equal(120_000m, p.TaskerAmount);
        Assert.Equal(p.Amount, p.RefundedAmount + p.TaskerAmount + p.CommissionAmount); // bất biến tiền
    }

    [Fact]
    public async Task ReleaseOnline_GoiLanHai_KhongGiaiNganLai()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Completed, tasker);
        Db.HeldPayment(booking);
        var settlement = CreateSettlement();

        Assert.True(await settlement.ReleaseOnlineAsync(booking, false));
        await Db.SaveChangesAsync();
        Assert.False(await settlement.ReleaseOnlineAsync(booking, false));
        await Db.SaveChangesAsync();

        Assert.Equal(1, await Fresh().WalletTransactions.CountAsync());
    }

    [Fact]
    public async Task ReleaseOnline_KhongCoThanhToan_TraFalse()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Completed, Db.Tasker());
        Assert.False(await CreateSettlement().ReleaseOnlineAsync(booking, false));
    }

    [Fact]
    public async Task RefundHeld_HoanToanBo()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 150_000m), BookingStatus.Cancelled);
        Db.HeldPayment(booking);

        Assert.True(await CreateSettlement().RefundHeldAsync(booking, 0, "Khách hủy"));
        await Db.SaveChangesAsync();

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(150_000m, p.RefundedAmount);
        Assert.Equal(0m, p.TaskerAmount);
        Assert.Equal(0m, p.CommissionAmount);
        Assert.Contains("150.000 đ", (await fresh.Notifications.SingleAsync()).Message);
    }

    [Fact]
    public async Task RefundHeld_CoPhiHuy_ChuyenChoTaskerKhongChietKhau()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 200_000m), BookingStatus.Cancelled, tasker);
        Db.HeldPayment(booking);

        await CreateSettlement().RefundHeldAsync(booking, 40_000m, "Hủy muộn");
        await Db.SaveChangesAsync();

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(160_000m, p.RefundedAmount);
        Assert.Equal(40_000m, p.TaskerAmount);
        Assert.Equal(40_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        Assert.Equal(WalletTransactionType.CancellationFee, (await fresh.WalletTransactions.SingleAsync()).Type);
        Assert.Equal(2, await fresh.AuditLogs.CountAsync()); // Refund + CancellationFee
    }

    [Fact]
    public async Task RefundHeld_PhiLonHonSoTien_BiGioiHan()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 100_000m), BookingStatus.Cancelled, tasker);
        Db.HeldPayment(booking);

        await CreateSettlement().RefundHeldAsync(booking, 999_999m, "x");
        await Db.SaveChangesAsync();

        var p = await Fresh().Payments.SingleAsync();
        Assert.Equal(0m, p.RefundedAmount);
        Assert.Equal(100_000m, p.TaskerAmount);
    }

    [Fact]
    public async Task RefundHeld_KhongCoTienTamGiu_TraFalse()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Cancelled);
        Assert.False(await CreateSettlement().RefundHeldAsync(booking, 0, "x"));
    }

    [Fact]
    public async Task SettleCash_TruChietKhauTuViTasker()
    {
        var tasker = Db.Tasker(wallet: 500_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 300_000m), BookingStatus.InProgress, tasker, PaymentMethod.Cash);

        await CreateSettlement().SettleCashAsync(booking);
        await Db.SaveChangesAsync();

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Success, p.Status);
        Assert.Equal(PaymentMethod.Cash, p.Method);
        Assert.Equal(60_000m, p.CommissionAmount);
        Assert.Equal(240_000m, p.TaskerAmount);
        Assert.Equal(440_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        var tx = await fresh.WalletTransactions.SingleAsync();
        Assert.Equal(WalletTransactionType.Commission, tx.Type);
        Assert.Equal(-60_000m, tx.Amount);
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task SettleCash_SoDuThap_GuiCanhBao()
    {
        var tasker = Db.Tasker(wallet: 70_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 150_000m), BookingStatus.InProgress, tasker, PaymentMethod.Cash);

        await CreateSettlement().SettleCashAsync(booking);
        await Db.SaveChangesAsync();

        Assert.Contains(await Fresh().Notifications.Select(n => n.Title).ToListAsync(), t => t == "Số dư ví thấp");
    }

    [Fact]
    public async Task SettleCash_ViKhongDu_KhongChoPhepTruMotPhan_BaoLoi()
    {
        var tasker = Db.Tasker(wallet: 10_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 300_000m), BookingStatus.InProgress, tasker, PaymentMethod.Cash);
        await Assert.ThrowsAsync<HomeService.Api.Common.ApiException>(() => CreateSettlement().SettleCashAsync(booking));
    }

    [Fact]
    public async Task SettleCash_ChoPhepTruMotPhan_TruToiDaSoDuVaGhiNhatKy()
    {
        var tasker = Db.Tasker(wallet: 10_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 300_000m), BookingStatus.PaymentOverdue, tasker, PaymentMethod.Cash);

        await CreateSettlement().SettleCashAsync(booking, allowPartialCommission: true);
        await Db.SaveChangesAsync();

        var fresh = Fresh();
        Assert.Equal(0m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(10_000m, p.CommissionAmount);
        Assert.Equal(290_000m, p.TaskerAmount);
        Assert.Contains(await fresh.AuditLogs.Select(a => a.Detail!).ToListAsync(), d => d.Contains("còn thiếu 50000"));
    }
}
