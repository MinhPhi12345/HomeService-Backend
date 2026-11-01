using HomeService.Api.Common;
using HomeService.Api.Enums;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>EP07 – NotificationService.</summary>
public class NotificationServiceTests : TestBase
{
    [Fact]
    public async Task Add_LuuThongBaoChuaDoc()
    {
        var user = Db.Customer();
        CreateNotifications().Add(user.Id, "Tiêu đề", "Nội dung");
        await Db.SaveChangesAsync();

        var n = await Fresh().Notifications.SingleAsync();
        Assert.Equal(user.Id, n.UserId);
        Assert.Equal("Tiêu đề", n.Title);
        Assert.False(n.IsRead);
    }

    [Fact]
    public async Task Add_CatNganTieuDe150VaNoiDung500()
    {
        var user = Db.Customer();
        CreateNotifications().Add(user.Id, new string('a', 200), new string('b', 800));
        await Db.SaveChangesAsync();

        var n = await Fresh().Notifications.SingleAsync();
        Assert.Equal(150, n.Title.Length);
        Assert.Equal(500, n.Message.Length);
    }

    [Fact]
    public async Task AddForAdmins_ChiGuiAdminDangHoatDong()
    {
        var a1 = Db.Admin();
        Db.Admin(isActive: false);
        Db.Customer();
        await CreateNotifications().AddForAdminsAsync("Khiếu nại mới", "Đơn #1");
        await Db.SaveChangesAsync();

        var n = await Fresh().Notifications.SingleAsync();
        Assert.Equal(a1.Id, n.UserId);
    }
}

/// <summary>FR-16/FR-17 – ví trả trước của Tasker (WalletService).</summary>
public class WalletServiceTests : TestBase
{
    [Fact]
    public async Task Apply_CongTien_GhiLichSuVaSoDuSau()
    {
        var tasker = Db.Tasker(wallet: 100_000m);
        var tx = await CreateWallet().ApplyAsync(tasker.Id, 50_000m, WalletTransactionType.TopUp, null, "Nạp");
        await Db.SaveChangesAsync();

        Assert.Equal(150_000m, tx.BalanceAfter);
        var fresh = Fresh();
        Assert.Equal(150_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        var row = await fresh.WalletTransactions.SingleAsync();
        Assert.Equal(WalletTransactionType.TopUp, row.Type);
        Assert.Equal(50_000m, row.Amount);
    }

    [Fact]
    public async Task Apply_TruTien_DungBangSoDu_VeKhong()
    {
        var tasker = Db.Tasker(wallet: 30_000m);
        var tx = await CreateWallet().ApplyAsync(tasker.Id, -30_000m, WalletTransactionType.Commission, null, null);
        Assert.Equal(0m, tx.BalanceAfter);
    }

    [Fact]
    public async Task Apply_LamViAm_BiTuChoi()
    {
        var tasker = Db.Tasker(wallet: 10_000m);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateWallet().ApplyAsync(tasker.Id, -10_001m, WalletTransactionType.Withdraw, null, null));
        Assert.Equal("Số dư khả dụng không đủ.", ex.Message);
        Assert.Equal(10_000m, (await Fresh().TaskerProfiles.SingleAsync()).WalletBalance);
    }

    [Fact]
    public async Task Apply_GhiChuQuaDai_CatCon255()
    {
        var tasker = Db.Tasker();
        var tx = await CreateWallet().ApplyAsync(tasker.Id, 1m, WalletTransactionType.TopUp, null, new string('x', 300));
        Assert.Equal(255, tx.Note!.Length);
    }

    [Fact]
    public async Task GetProfile_KhongPhaiTasker_BaoLoi()
    {
        var customer = Db.Customer();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateWallet().GetProfileAsync(customer.Id));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task RecalculateReserved_ChiTinhDonTienMatDangLam()
    {
        var tasker = Db.Tasker();
        var customer = Db.Customer();
        var service = Db.Service(price: 200_000m);
        Db.Booking(customer, service, BookingStatus.Accepted, tasker, PaymentMethod.Cash);       // 40.000
        Db.Booking(customer, service, BookingStatus.InProgress, tasker, PaymentMethod.Cash);     // 40.000
        Db.Booking(customer, service, BookingStatus.Accepted, tasker, PaymentMethod.EWallet);    // online: không tính
        Db.Booking(customer, service, BookingStatus.Paid, tasker, PaymentMethod.Cash);           // đã quyết toán
        Db.Booking(customer, service, BookingStatus.Accepted, tasker, PaymentMethod.Cash, b => b.IsTaskerNoShow = true);

        await CreateWallet().RecalculateReservedAsync(tasker.Id);
        await Db.SaveChangesAsync();

        Assert.Equal(80_000m, (await Fresh().TaskerProfiles.SingleAsync()).ReservedAmount);
    }

    [Fact]
    public async Task RecalculateReserved_KhongConDon_VeKhong()
    {
        var tasker = Db.Tasker(configure: p => p.ReservedAmount = 50_000m);
        await CreateWallet().RecalculateReservedAsync(tasker.Id);
        await Db.SaveChangesAsync();
        Assert.Equal(0m, (await Fresh().TaskerProfiles.SingleAsync()).ReservedAmount);
    }
}

/// <summary>NFR-16 – nhật ký kiểm toán (AuditService).</summary>
public class AuditServiceTests : TestBase
{
    [Fact]
    public async Task Add_KhongCoNguoiDung_GhiLaHeThong()
    {
        CreateAudit().Add(AuditService.Released, 120_000m, 5, 6, "Giải ngân");
        await Db.SaveChangesAsync();

        var log = await Fresh().AuditLogs.SingleAsync();
        Assert.Equal("Released", log.Action);
        Assert.Equal(120_000m, log.Amount);
        Assert.Equal(5, log.BookingId);
        Assert.Equal(6, log.PaymentId);
        Assert.Null(log.ActorUserId);
    }

    [Fact]
    public async Task Add_LayNguoiThucHienTuToken()
    {
        HttpContextAccessor.HttpContext = new DefaultHttpContext { User = ControllerExtensions.Principal(9, UserRole.Admin) };
        CreateAudit().Add(AuditService.DisputeResolved, 0m);
        await Db.SaveChangesAsync();
        Assert.Equal(9, (await Fresh().AuditLogs.SingleAsync()).ActorUserId);
    }

    [Fact]
    public async Task Add_ChiTietQuaDai_CatCon500()
    {
        CreateAudit().Add(AuditService.Refund, 1m, detail: new string('d', 600));
        await Db.SaveChangesAsync();
        Assert.Equal(500, (await Fresh().AuditLogs.SingleAsync()).Detail!.Length);
    }
}

/// <summary>BR-D02 – điểm vi phạm Tasker (TaskerDisciplineService).</summary>
public class TaskerDisciplineServiceTests : TestBase
{
    [Fact]
    public async Task AddViolation_CongDiemVaThongBao()
    {
        var tasker = Db.Tasker();
        await CreateDiscipline().AddViolationAsync(tasker.Id, 1, "không đến");
        await Db.SaveChangesAsync();

        var fresh = Fresh();
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(1, p.ViolationPoints);
        Assert.Null(p.SuspendedUntil);
        Assert.Contains("1/3", (await fresh.Notifications.SingleAsync()).Message);
    }

    [Fact]
    public async Task AddViolation_Du3Diem_TamKhoa7NgayVaVeKhong()
    {
        var tasker = Db.Tasker(configure: p => p.ViolationPoints = 2);
        await CreateDiscipline().AddViolationAsync(tasker.Id, 1, "khiếu nại");
        await Db.SaveChangesAsync();

        var fresh = Fresh();
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(0, p.ViolationPoints);
        Assert.NotNull(p.SuspendedUntil);
        Assert.InRange(p.SuspendedUntil!.Value, DateTime.UtcNow.AddDays(6.9), DateTime.UtcNow.AddDays(7.1));
        Assert.Equal(2, await fresh.Notifications.CountAsync());
    }

    [Fact]
    public async Task AddViolation_BaoSaiCong2Diem()
    {
        var tasker = Db.Tasker(configure: p => p.ViolationPoints = 1);
        await CreateDiscipline().AddViolationAsync(tasker.Id, 2, "báo sai");
        await Db.SaveChangesAsync();
        Assert.NotNull((await Fresh().TaskerProfiles.SingleAsync()).SuspendedUntil);
    }

    [Fact]
    public async Task AddViolation_KhongCoHoSo_BoQua()
    {
        var customer = Db.Customer();
        await CreateDiscipline().AddViolationAsync(customer.Id, 1, "x");
        await Db.SaveChangesAsync();
        Assert.Equal(0, await Fresh().Notifications.CountAsync());
    }
}
