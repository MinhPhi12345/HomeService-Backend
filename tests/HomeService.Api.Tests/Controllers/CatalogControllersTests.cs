using MsOptions = Microsoft.Extensions.Options.Options;
using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Controllers;

/// <summary>EP02 – ServiceCategoriesController (US07 → US11).</summary>
public class ServiceCategoriesControllerTests : TestBase
{
    private ServiceCategoriesController AsAdmin() => new ServiceCategoriesController(Db).AsUser(1, UserRole.Admin);
    private ServiceCategoriesController AsGuest() => new ServiceCategoriesController(Db).AsAnonymous();

    private static UpsertServiceCategoryRequest Upsert(string name = "Vệ sinh máy lạnh", decimal price = 250_000m, string? icon = null) =>
        new(name, "  Mô tả  ", price, "lần", icon);

    [Fact]
    public async Task GetAll_KhachChiThayDichVuDangHoatDong_SapXepTheoTen()
    {
        Db.Service("Giặt ủi");
        Db.Service("Dọn dẹp nhà");
        Db.Service("Đã ngừng", isActive: false);

        var list = (await AsGuest().GetAll(null)).OkValue().ToList();

        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(list, s => s.Name == "Đã ngừng");
    }

    [Fact]
    public async Task GetAll_TimTheoTuKhoa()
    {
        Db.Service("Giặt ủi");
        Db.Service("Dọn dẹp nhà");
        var list = (await AsGuest().GetAll("GIẶT")).OkValue().ToList();
        Assert.Equal("Giặt ủi", Assert.Single(list).Name);
    }

    [Fact]
    public async Task GetAll_IncludeInactive_ChiCoTacDungVoiAdmin()
    {
        Db.Service("Đang bán");
        Db.Service("Đã ngừng", isActive: false);
        Assert.Single((await AsGuest().GetAll(null, includeInactive: true)).OkValue());
        Assert.Equal(2, (await AsAdmin().GetAll(null, includeInactive: true)).OkValue().Count());
    }

    [Fact]
    public async Task GetById_200_VaKhongTonTai_404()
    {
        var s = Db.Service();
        Assert.Equal(s.Name, (await AsGuest().GetById(s.Id)).OkValue().Name);
        Assert.IsType<NotFoundResult>((await AsGuest().GetById(999)).Result);
    }

    [Fact]
    public async Task Create_201_ChuanHoaDuongDanAnh()
    {
        var result = await AsAdmin().Create(Upsert(icon: "https://api.homeservice.id.vn/uploads/202610/icon.png"));
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var body = Assert.IsType<ServiceCategoryResponse>(created.Value);
        Assert.Equal("/uploads/202610/icon.png", body.IconUrl);
        Assert.Equal("Mô tả", body.Description);
        Assert.True(body.IsActive);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100000001")]
    [InlineData("150000.5")]
    public async Task Create_GiaKhongHopLe_400(string price)
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin().Create(Upsert(price: decimal.Parse(price))));
        Assert.Equal("Giá phải lớn hơn 0.", ex.Message);
    }

    [Fact]
    public async Task Create_TrungTenDichVuDangHoatDong_409()
    {
        Db.Service("Giặt ủi");
        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin().Create(Upsert(" giặt ủi ")));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Create_TrungTenDichVuDaXoa_DuocPhep()
    {
        Db.Service("Giặt ủi", isActive: false);
        Assert.IsType<CreatedAtActionResult>((await AsAdmin().Create(Upsert("Giặt ủi"))).Result);
    }

    [Fact]
    public async Task Update_GhiDeToanBo_DonCuGiuGia()
    {
        var s = Db.Service(price: 100_000m);
        var booking = Db.Booking(Db.Customer(), s);

        Assert.IsType<NoContentResult>(await AsAdmin().Update(s.Id, Upsert("Tên mới", 120_000m)));

        var fresh = Fresh();
        var saved = await fresh.ServiceCategories.SingleAsync();
        Assert.Equal("Tên mới", saved.Name);
        Assert.Equal(120_000m, saved.BasePrice);
        Assert.Equal("lần", saved.Unit);
        Assert.Equal(100_000m, (await fresh.Bookings.SingleAsync(b => b.Id == booking.Id)).TotalPrice);
    }

    [Fact]
    public async Task Update_GiuTenCuCuaChinhNo_KhongBaoTrung()
    {
        var s = Db.Service("Giặt ủi");
        Assert.IsType<NoContentResult>(await AsAdmin().Update(s.Id, Upsert("Giặt ủi", 90_000m)));
    }

    [Fact]
    public async Task Update_KhongTonTai_404()
    {
        Assert.IsType<NotFoundResult>(await AsAdmin().Update(999, Upsert()));
    }

    [Fact]
    public async Task Delete_XoaMem_LanHaiBao404()
    {
        var s = Db.Service();
        Assert.IsType<NoContentResult>(await AsAdmin().Delete(s.Id));
        Assert.False((await Fresh().ServiceCategories.SingleAsync()).IsActive);
        Db.ChangeTracker.Clear();
        Assert.IsType<NotFoundResult>(await AsAdmin().Delete(s.Id));
    }

    [Fact]
    public async Task Restore_KhoiPhucDichVuDaXoa()
    {
        var s = Db.Service(isActive: false);
        Assert.IsType<NoContentResult>(await AsAdmin().Restore(s.Id));
        Assert.True((await Fresh().ServiceCategories.SingleAsync()).IsActive);
        Assert.IsType<NotFoundResult>(await AsAdmin().Restore(999));
    }

    [Fact]
    public async Task Restore_TrungTenDichVuDangBan_409()
    {
        var deleted = Db.Service("Giặt ủi", isActive: false);
        Db.Service("Giặt ủi");
        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin().Restore(deleted.Id));
        Assert.Equal("Tên dịch vụ đã tồn tại.", ex.Message);
    }
}

/// <summary>US04 / US16 – TaskersController.</summary>
public class TaskersControllerTests : TestBase
{
    private TaskersController Controller() => new(Db, CreateNotifications());

    private static TaskerProfileRequest ProfileRequest(string idCard = "079123456789", int radius = 5, string? area = "Quận 1") => new()
    {
        IdCardNumber = idCard,
        IdCardImageUrl = "http://localhost:8080/uploads/202610/cccd.png",
        Skills = "  Dọn dẹp, Giặt ủi  ",
        WorkingArea = area,
        BaseAddress = "  Chợ Bến Thành  ",
        BaseLatitude = Seed.BenThanhLat,
        BaseLongitude = Seed.BenThanhLng,
        ServiceRadiusKm = radius,
    };

    [Fact]
    public async Task SubmitProfile_LanDau_ChoDuyetVaBaoAdmin()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker(status: TaskerVerificationStatus.Pending, configure: p => p.IdCardNumber = "");

        var result = await Controller().AsUser(tasker.Id, UserRole.Tasker, "Tasker Mới").SubmitProfile(ProfileRequest());

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var fresh = Fresh();
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal("079123456789", p.IdCardNumber);
        Assert.Equal("/uploads/202610/cccd.png", p.IdCardImageUrl);
        Assert.Equal("Dọn dẹp, Giặt ủi", p.Skills);
        Assert.Equal("Chợ Bến Thành", p.BaseAddress);
        Assert.NotNull(p.SubmittedAt);
        var n = await fresh.Notifications.SingleAsync();
        Assert.Equal(admin.Id, n.UserId);
        Assert.Contains("Tasker Mới", n.Message);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public async Task SubmitProfile_BanKinhKhongHopLe_400(int radius)
    {
        var tasker = Db.Tasker();
        var ex = await Assert.ThrowsAsync<ApiException>(() => Controller().AsUser(tasker.Id, UserRole.Tasker).SubmitProfile(ProfileRequest(radius: radius)));
        Assert.Equal("Bán kính phục vụ không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task SubmitProfile_KhuVucTuLayTuDiaChiDiemLamViec()
    {
        var tasker = Db.Tasker();
        var req = ProfileRequest(area: "Gò Vấp"); // app cũ còn gửi khu vực chọn tay – bị thay bằng khu vực theo địa chỉ
        req.BaseAddress = "45 Lê Văn Khương, Phường Thới An, Quận 12, Thành phố Hồ Chí Minh";

        var result = await Controller().AsUser(tasker.Id, UserRole.Tasker).SubmitProfile(req);

        var body = Assert.IsType<TaskerProfileResponse>(Assert.IsAssignableFrom<ObjectResult>(result.Result).Value);
        Assert.Equal("Quận 12", body.WorkingArea);
        Assert.Equal("Quận 12", (await Fresh().TaskerProfiles.SingleAsync()).WorkingArea);
    }

    [Theory]
    [InlineData(" Quận 1 ", "Quận 1")]
    [InlineData(null, null)]
    public async Task SubmitProfile_DiaChiKhongCoKhuVuc_DungKhuVucGuiKemHoacDeTrong(string? sent, string? expected)
    {
        var tasker = Db.Tasker();
        var req = ProfileRequest(area: sent); // BaseAddress = "Chợ Bến Thành" – không suy ra được khu vực
        await Controller().AsUser(tasker.Id, UserRole.Tasker).SubmitProfile(req);
        Assert.Equal(expected, (await Fresh().TaskerProfiles.SingleAsync()).WorkingArea);
    }

    [Fact]
    public async Task SubmitProfile_DaDuyet_DoiDiemLamViec_KhongCanDuyetLai()
    {
        var tasker = Db.Tasker(configure: p => p.IdCardImageUrl = "/uploads/202610/cccd.png");
        var req = ProfileRequest();
        req.IdCardImageUrl = "/uploads/202610/cccd.png";
        await Controller().AsUser(tasker.Id, UserRole.Tasker).SubmitProfile(req);
        Assert.Equal(TaskerVerificationStatus.Approved, (await Fresh().TaskerProfiles.SingleAsync()).VerificationStatus);
    }

    [Fact]
    public async Task SubmitProfile_DaDuyet_DoiCccd_QuayVeChoDuyet()
    {
        var tasker = Db.Tasker();
        await Controller().AsUser(tasker.Id, UserRole.Tasker).SubmitProfile(ProfileRequest("079999999999"));
        Assert.Equal(TaskerVerificationStatus.Pending, (await Fresh().TaskerProfiles.SingleAsync()).VerificationStatus);
    }

    [Fact]
    public async Task SubmitProfile_BiTuChoi_GuiLai_ChoDuyet()
    {
        var tasker = Db.Tasker(status: TaskerVerificationStatus.Rejected);
        await Controller().AsUser(tasker.Id, UserRole.Tasker).SubmitProfile(ProfileRequest());
        Assert.Equal(TaskerVerificationStatus.Pending, (await Fresh().TaskerProfiles.SingleAsync()).VerificationStatus);
    }

    [Fact]
    public async Task GetMyProfile_200_VaChuaCoHoSo_404()
    {
        var tasker = Db.Tasker();
        Assert.Equal(tasker.Id, (await Controller().AsUser(tasker.Id, UserRole.Tasker).GetMyProfile()).OkValue().UserId);
        Assert.IsType<NotFoundResult>((await Controller().AsUser(Db.Customer().Id, UserRole.Tasker).GetMyProfile()).Result);
    }

    [Fact]
    public async Task PendingVerification_ChiHoSoDaGuiCccd()
    {
        var submitted = Db.Tasker("Đã gửi", status: TaskerVerificationStatus.Pending);
        Db.Tasker("Chưa gửi", status: TaskerVerificationStatus.Pending, configure: p => p.IdCardNumber = "");
        Db.Tasker("Đã duyệt");

        var list = (await Controller().AsUser(1, UserRole.Admin).GetPendingVerification()).OkValue().ToList();

        var item = Assert.Single(list);
        Assert.Equal(submitted.Id, item.UserId);
        Assert.Equal("Đã gửi", item.FullName);
        Assert.Equal(submitted.Email, item.Email);
    }

    [Theory]
    [InlineData(true, TaskerVerificationStatus.Approved, "Hồ sơ đã được duyệt")]
    [InlineData(false, TaskerVerificationStatus.Rejected, "Hồ sơ bị từ chối")]
    public async Task SetVerification_DuyetHoacTuChoi_VaThongBao(bool approve, TaskerVerificationStatus expected, string title)
    {
        var tasker = Db.Tasker(status: TaskerVerificationStatus.Pending);
        var profileId = (await Db.TaskerProfiles.SingleAsync()).Id;

        Assert.IsType<NoContentResult>(await Controller().AsUser(1, UserRole.Admin).SetVerification(profileId, approve));

        var fresh = Fresh();
        Assert.Equal(expected, (await fresh.TaskerProfiles.SingleAsync()).VerificationStatus);
        var n = await fresh.Notifications.SingleAsync();
        Assert.Equal(tasker.Id, n.UserId);
        Assert.Equal(title, n.Title);
    }

    [Fact]
    public async Task SetVerification_ChuaGuiHoSo_400_VaKhongTonTai_404()
    {
        Db.Tasker(status: TaskerVerificationStatus.Pending, configure: p => p.IdCardNumber = "");
        var profileId = (await Db.TaskerProfiles.SingleAsync()).Id;
        var ex = await Assert.ThrowsAsync<ApiException>(() => Controller().AsUser(1, UserRole.Admin).SetVerification(profileId, true));
        Assert.Equal("Tasker chưa gửi hồ sơ xác thực.", ex.Message);
        Assert.IsType<NotFoundResult>(await Controller().AsUser(1, UserRole.Admin).SetVerification(999, true));
    }

    [Fact]
    public async Task UpdateAvailability_TatNhanDon_VaGuiViTri()
    {
        var tasker = Db.Tasker();
        await Controller().AsUser(tasker.Id, UserRole.Tasker).UpdateAvailability(new UpdateAvailabilityRequest
        {
            IsAvailable = true, Latitude = Seed.Q3Lat, Longitude = Seed.Q3Lng,
        }, MsOptions.Create(MatchingOptions));
        var p = await Fresh().TaskerProfiles.SingleAsync();
        Assert.Equal(Seed.Q3Lat, p.LastLatitude);
        Assert.NotNull(p.LastLocationAt);

        await Controller().AsUser(tasker.Id, UserRole.Tasker).UpdateAvailability(new UpdateAvailabilityRequest { IsAvailable = false }, MsOptions.Create(MatchingOptions));
        var p2 = await Fresh().TaskerProfiles.SingleAsync();
        Assert.False(p2.IsAvailable);
        Assert.Equal(Seed.Q3Lat, p2.LastLatitude);   // không gửi vị trí → giữ vị trí cũ
    }

    [Fact]
    public async Task UpdateAvailability_ViChuaDuKyQuy_KhongBatDuoc_TatVanDuoc()
    {
        var tasker = Db.Tasker(wallet: 150_000m, configure: p => { p.IsAvailable = false; p.ReservedAmount = 60_000m; });
        var ctrl = Controller().AsUser(tasker.Id, UserRole.Tasker);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            ctrl.UpdateAvailability(new UpdateAvailabilityRequest { IsAvailable = true }, MsOptions.Create(MatchingOptions)));
        Assert.Contains("ký quỹ tối thiểu 100.000 đ", ex.Message);
        Assert.False((await Fresh().TaskerProfiles.SingleAsync()).IsAvailable);

        await Db.TaskerProfiles.ExecuteUpdateAsync(s => s.SetProperty(p => p.ReservedAmount, 0m));
        Db.ChangeTracker.Clear();
        Assert.IsType<NoContentResult>(await Controller().AsUser(tasker.Id, UserRole.Tasker)
            .UpdateAvailability(new UpdateAvailabilityRequest { IsAvailable = true }, MsOptions.Create(MatchingOptions)));
        Assert.True((await Fresh().TaskerProfiles.SingleAsync()).IsAvailable);
    }

    [Fact]
    public async Task GetAvailable_LocKhuVuc_SapXepTheoDiem()
    {
        Db.Tasker("A", configure: p => { p.RatingAverage = 4m; p.WorkingArea = "Quận 3"; });
        Db.Tasker("B", configure: p => p.RatingAverage = 4.8m);
        Db.Tasker("C", configure: p => p.IsAvailable = false);
        Db.Tasker("D", status: TaskerVerificationStatus.Pending);

        var all = (await Controller().AsUser(1, UserRole.Customer).GetAvailable(null)).OkValue().ToList();
        Assert.Equal(new[] { "B", "A" }, all.Select(x => x.FullName).ToArray());
        var q3 = (await Controller().AsUser(1, UserRole.Customer).GetAvailable(" Quận 3 ")).OkValue();
        Assert.Equal("A", Assert.Single(q3).FullName);
    }
}
