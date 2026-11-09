using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomeService.Api.Tests.Data;

/// <summary>Bộ dữ liệu demo (Seed:DemoData): đủ trạng thái, chạy một lần, số liệu tiền / ví / đánh giá nhất quán.</summary>
public class DemoDataSeederTests : TestBase
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:DemoPassword"] = "Demo@1234" }).Build();

    private async Task<bool> SeedAsync(string? uploads = null)
    {
        if (!await Db.Users.AnyAsync(u => u.Role == UserRole.Admin))
        {
            Db.Users.Add(new User { FullName = "Admin", Email = "admin@test.vn", PasswordHash = "x", Role = UserRole.Admin });
            await Db.SaveChangesAsync();
        }
        return await DemoDataSeeder.SeedAsync(Db, Config(), uploads, NullLogger.Instance);
    }

    [Fact]
    public async Task Seed_TaoDuTaiKhoanVaMoiTrangThaiDon()
    {
        Assert.True(await SeedAsync());
        await using var db = Fresh();
        Assert.Equal(6, await db.Users.CountAsync(u => u.Email.EndsWith("@demo.vn") && u.Role == UserRole.Customer));
        Assert.Equal(4, await db.TaskerProfiles.CountAsync(t => t.VerificationStatus == TaskerVerificationStatus.Approved));
        Assert.Equal(2, await db.TaskerProfiles.CountAsync(t => t.VerificationStatus == TaskerVerificationStatus.Pending));
        var statuses = await db.Bookings.Select(b => b.Status).Distinct().ToListAsync();
        foreach (var s in new[] { BookingStatus.Accepted, BookingStatus.InProgress, BookingStatus.Completed, BookingStatus.Paid,
                     BookingStatus.Cancelled, BookingStatus.Expired, BookingStatus.Disputed })
            Assert.Contains(s, statuses);
        Assert.True(await db.Bookings.CountAsync(b => b.Status == BookingStatus.Paid) >= 60);
        Assert.Equal(1, await db.Disputes.CountAsync(d => d.Status == DisputeStatus.Open));
        Assert.Equal(2, await db.Reviews.CountAsync(r => r.IsFlagged));
        Assert.True(BCrypt.Net.BCrypt.Verify("Demo@1234", (await db.Users.FirstAsync(u => u.Email == "minhanh@demo.vn")).PasswordHash));
    }

    [Fact]
    public async Task Seed_ChayLaiKhongTaoThem()
    {
        Assert.True(await SeedAsync());
        var n = await Db.Bookings.CountAsync();
        Assert.False(await SeedAsync());
        Assert.Equal(n, await Fresh().Bookings.CountAsync());
    }

    [Fact]
    public async Task Seed_ThanhToanKhopBatBien()
    {
        await SeedAsync();
        await using var db = Fresh();
        var settled = await db.Payments.Where(p => p.Status == PaymentStatus.Released || p.Status == PaymentStatus.PartiallyRefunded).ToListAsync();
        Assert.NotEmpty(settled);
        foreach (var p in settled)
            Assert.Equal(p.Amount + p.PromoSubsidy, p.RefundedAmount + p.TaskerAmount + p.CommissionAmount);
        foreach (var p in await db.Payments.Where(p => p.Status == PaymentStatus.Refunded).ToListAsync())
            Assert.Equal(p.Amount, p.RefundedAmount);
        // Mỗi đơn có mã: tiền khách trả = giá gốc − giảm; số lượt dùng khớp
        foreach (var b in await db.Bookings.Include(b => b.Payment).Where(b => b.VoucherId != null).ToListAsync())
            Assert.Equal(b.TotalPrice - b.DiscountAmount, b.Payment!.Amount);
        foreach (var v in await db.Vouchers.Include(v => v.Redemptions).ToListAsync())
            Assert.Equal(v.Redemptions.Count, v.UsedCount);
    }

    [Fact]
    public async Task Seed_ViVaDiemDanhGiaNhatQuan()
    {
        await SeedAsync();
        await using var db = Fresh();
        foreach (var t in await db.TaskerProfiles.ToListAsync())
        {
            var tx = await db.WalletTransactions.Where(w => w.TaskerId == t.UserId).OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).ToListAsync();
            decimal bal = 0;
            foreach (var w in tx) { bal += w.Amount; Assert.Equal(bal, w.BalanceAfter); Assert.True(bal >= 0); }
            Assert.Equal(bal, t.WalletBalance);
            Assert.True(t.WalletBalance >= t.ReservedAmount);
            var ratings = await db.Reviews.Where(r => r.RevieweeId == t.UserId && !r.IsFlagged).Select(r => r.Rating).ToListAsync();
            Assert.Equal(ratings.Count == 0 ? 5m : Math.Round((decimal)ratings.Average(), 2, MidpointRounding.AwayFromZero), t.RatingAverage);
        }
    }

    [Fact]
    public async Task Seed_ThangTruocDuDieuKienXetTaskerXuatSac()
    {
        await SeedAsync();
        var prev = DateTime.UtcNow.AddHours(7).AddMonths(-1);
        var ranked = CreateAwards().Rank(await CreateAwards().CollectMetricsAsync(prev.Year, prev.Month));
        Assert.Equal(4, ranked.Count);
    }

    [Fact]
    public async Task Seed_ChepAnhMinhHoaVaoThuMucUploads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "demo-seed-" + Guid.NewGuid().ToString("N"));
        try
        {
            await SeedAsync(dir);
            Assert.True(File.Exists(Path.Combine(dir, "demo", "checkin.jpg")));
            var b = await Fresh().Bookings.FirstAsync(x => x.Status == BookingStatus.InProgress);
            Assert.Equal("/uploads/demo/checkin.jpg", b.CheckInPhotoUrl);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
