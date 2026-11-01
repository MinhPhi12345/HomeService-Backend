using System.Text.Json;
using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Middleware;
using HomeService.Api.Models;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HomeService.Api.Tests.Data;

/// <summary>Ràng buộc dữ liệu khai báo trong AppDbContext (SRS mục 7).</summary>
public class AppDbContextTests : TestBase
{
    [Fact]
    public async Task Email_LaDuyNhat()
    {
        Db.Users.Add(new User { FullName = "A", Email = "trung@test.vn", PasswordHash = "x" });
        Db.Users.Add(new User { FullName = "B", Email = "trung@test.vn", PasswordHash = "x" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Review_MoiNguoiMotDanhGiaMoiDon()
    {
        var c = Db.Customer();
        var t = Db.Tasker();
        var b = Db.Booking(c, Db.Service(), BookingStatus.Paid, t);
        Db.Review(b, c, t, 5);
        Db.Reviews.Add(new Review { BookingId = b.Id, ReviewerId = c.Id, RevieweeId = t.Id, Rating = 4 });
        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Review_SoSaoNgoai1Den5_BiCSDLTuChoi(int rating)
    {
        var c = Db.Customer();
        var t = Db.Tasker();
        var b = Db.Booking(c, Db.Service(), BookingStatus.Paid, t);
        Db.Reviews.Add(new Review { BookingId = b.Id, ReviewerId = c.Id, RevieweeId = t.Id, Rating = rating });
        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task LoiMoi_KhongGuiTrungTaskerTrongCungVong()
    {
        var b = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched);
        var t = Db.Tasker();
        Db.Offer(b, t);
        Db.BookingOffers.Add(new BookingOffer { BookingId = b.Id, TaskerId = t.Id, Round = b.MatchRound, Wave = 2, ExpiresAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThoiGian_DocRaLuonLaUtc()
    {
        var c = Db.Customer();
        var u = await Fresh().Users.SingleAsync(x => x.Id == c.Id);
        Assert.Equal(DateTimeKind.Utc, u.CreatedAt.Kind);
        var b = Db.Booking(c, Db.Service(), configure: x => x.PaymentDueAt = DateTime.UtcNow);
        var saved = await Fresh().Bookings.SingleAsync(x => x.Id == b.Id);
        Assert.Equal(DateTimeKind.Utc, saved.PaymentDueAt!.Value.Kind);
    }

    [Fact]
    public async Task TrangThaiDon_LaConcurrencyToken_ChongGhiDe()
    {
        var b = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Pending);
        using var other = Fresh();
        var copy = await other.Bookings.SingleAsync(x => x.Id == b.Id);

        b.Status = BookingStatus.Cancelled;          // khách hủy
        await Db.SaveChangesAsync();
        copy.Status = BookingStatus.Accepted;        // cùng lúc Tasker nhận
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task SoDuVi_LaConcurrencyToken_ChongGhiDe()
    {
        var t = Db.Tasker(wallet: 100_000m);
        using var other = Fresh();
        var copy = await other.TaskerProfiles.SingleAsync();
        var p = await Db.TaskerProfiles.SingleAsync();

        p.WalletBalance += 50_000m;
        await Db.SaveChangesAsync();
        copy.WalletBalance -= 100_000m;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        Assert.Equal(t.Id, p.UserId);
    }

    [Fact]
    public async Task XoaDon_XoaThanhToanVaLoiMoiTheo()
    {
        var b = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Cancelled);
        Db.HeldPayment(b);
        Db.Offer(b, Db.Tasker());
        Db.Bookings.Remove(b);
        await Db.SaveChangesAsync();
        var fresh = Fresh();
        Assert.Empty(await fresh.Payments.ToListAsync());
        Assert.Empty(await fresh.BookingOffers.ToListAsync());
    }
}

/// <summary>Khởi tạo CSDL: tạo Admin, dịch vụ mẫu, chuẩn hóa link ảnh cũ (DbInitializer).</summary>
public class DbInitializerTests : IDisposable
{
    private readonly TestDb _db = new();

    private ServiceProvider BuildServices(Dictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddScoped(_ => new AppDbContext(_db.Options));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Initialize_TaoAdminVaDichVuMau_ChayLaiKhongTrung()
    {
        using var sp = BuildServices(new() { ["Seed:AdminEmail"] = " Admin@HomeService.VN ", ["Seed:AdminPassword"] = "MatKhau@1" });

        await DbInitializer.InitializeAsync(sp);
        await DbInitializer.InitializeAsync(sp);

        var fresh = _db.Fresh();
        var admin = await fresh.Users.SingleAsync();
        Assert.Equal("admin@homeservice.vn", admin.Email);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(BCrypt.Net.BCrypt.Verify("MatKhau@1", admin.PasswordHash));
        Assert.Equal(5, await fresh.ServiceCategories.CountAsync());
    }

    [Fact]
    public async Task Initialize_TatDuLieuMau_KhongTaoDichVu()
    {
        using var sp = BuildServices(new() { ["Seed:SampleData"] = "false" });
        await DbInitializer.InitializeAsync(sp);
        Assert.Equal(0, await _db.Fresh().ServiceCategories.CountAsync());
    }

    [Fact]
    public async Task Initialize_ChuanHoaDuongDanAnhCu()
    {
        var c = _db.Db.Customer(configure: u => u.AvatarUrl = "http://localhost:8080/uploads/202609/av.png");
        var t = _db.Db.Tasker(configure: p => p.IdCardImageUrl = "http://10.0.2.2:8080/uploads/202609/cccd.png");
        var s = _db.Db.Service();
        s.IconUrl = "http://localhost:8080/uploads/202609/icon.png";
        var b = _db.Db.Booking(c, s, BookingStatus.Paid, t, configure: x =>
        {
            x.CheckInPhotoUrl = "http://localhost:8080/uploads/in.png";
            x.CheckOutPhotoUrl = "/uploads/out.png";
        });
        _db.Db.Dispute(b, d =>
        {
            d.EvidenceUrls = "http://localhost:8080/uploads/e1.png,/uploads/e2.png";
            d.TaskerEvidenceUrls = "http://localhost:8080/uploads/t1.png";
        });
        _db.Db.SaveChanges();

        using var sp = BuildServices();
        await DbInitializer.InitializeAsync(sp);

        var fresh = _db.Fresh();
        Assert.Equal("/uploads/202609/av.png", (await fresh.Users.SingleAsync(u => u.Id == c.Id)).AvatarUrl);
        Assert.Equal("/uploads/202609/cccd.png", (await fresh.TaskerProfiles.SingleAsync()).IdCardImageUrl);
        Assert.Equal("/uploads/202609/icon.png", (await fresh.ServiceCategories.SingleAsync()).IconUrl);
        var booking = await fresh.Bookings.SingleAsync();
        Assert.Equal("/uploads/in.png", booking.CheckInPhotoUrl);
        Assert.Equal("/uploads/out.png", booking.CheckOutPhotoUrl);
        var dispute = await fresh.Disputes.SingleAsync();
        Assert.Equal("/uploads/e1.png,/uploads/e2.png", dispute.EvidenceUrls);
        Assert.Equal("/uploads/t1.png", dispute.TaskerEvidenceUrls);
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>NFR-07 / SRS 8.6 – định dạng lỗi JSON thống nhất (ExceptionHandlingMiddleware).</summary>
public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int Status, string Body, string? ContentType)> Run(Exception? toThrow)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => toThrow is null ? Task.CompletedTask : throw toThrow,
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), context.Response.ContentType);
    }

    private static string Message(string json) => JsonDocument.Parse(json).RootElement.GetProperty("message").GetString()!;

    [Fact]
    public async Task KhongLoi_DiTiep()
    {
        var (status, body, _) = await Run(null);
        Assert.Equal(200, status);
        Assert.Equal("", body);
    }

    [Fact]
    public async Task ApiException400_TraMessageTiengVietCoDau()
    {
        var (status, body, type) = await Run(ApiException.BadRequest("Thời gian đặt không hợp lệ."));
        Assert.Equal(400, status);
        Assert.Equal("Thời gian đặt không hợp lệ.", Message(body));
        Assert.Contains("Thời gian", body);                 // không bị mã hóa \u
        Assert.StartsWith("application/json", type);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    public async Task ApiException403Va404_BodyRong(int code)
    {
        var (status, body, _) = await Run(code == 403 ? ApiException.Forbidden() : ApiException.NotFound());
        Assert.Equal(code, status);
        Assert.Equal("", body);
    }

    [Fact]
    public async Task XungDotDongThoi_409()
    {
        var (status, body, _) = await Run(new DbUpdateConcurrencyException("conflict"));
        Assert.Equal(409, status);
        Assert.Contains("vừa được cập nhật", Message(body));
    }

    [Fact]
    public async Task LoiKhongMongMuon_500_KhongLoStackTrace()
    {
        var (status, body, _) = await Run(new NullReferenceException("chi tiết nội bộ"));
        Assert.Equal(500, status);
        Assert.Equal("Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.", Message(body));
        Assert.DoesNotContain("chi tiết nội bộ", body);
    }
}
