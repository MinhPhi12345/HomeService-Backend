using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Services;
using HomeService.Api.Services.Interfaces;
using HomeService.Api.Tests.Infrastructure;
using HomeService.Api.Tests.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Controllers;

/// <summary>AuthController – mã HTTP trả về cho FE.</summary>
public class AuthControllerTests : TestBase
{
    private AuthController Create() => new(new AuthService(Db, new TokenService(JwtTestConfig.Create()), CreateOtp(), CreateVouchers()));

    private static RegisterRequest Register(string email) => new("Nguyễn Văn A", email, "Matkhau123", null, UserRole.Customer);

    private async Task Verified(string email)
    {
        await Create().Register(Register(email));
        (await Create().VerifyEmail(new VerifyEmailRequest(email, Emails.LastCode(email)))).OkValue();
    }

    [Fact]
    public async Task Register_200_TraThongTinGuiOtp()
    {
        var result = (await Create().Register(Register("a@test.vn"))).OkValue();
        Assert.Equal("a@test.vn", result.Email);
        Assert.Equal("a***@test.vn", result.MaskedEmail);
        Assert.Contains("Đã gửi mã OTP", result.Message);
    }

    [Fact]
    public async Task Register_EmailTrung_409()
    {
        await Verified("a@test.vn");
        var result = await Create().Register(Register("a@test.vn"));
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_200_VaSaiMatKhau_401()
    {
        await Verified("b@test.vn");
        Assert.False(string.IsNullOrEmpty((await Create().Login(new LoginRequest("b@test.vn", "Matkhau123"))).OkValue().AccessToken));
        Assert.IsType<UnauthorizedObjectResult>((await Create().Login(new LoginRequest("b@test.vn", "Sai12345"))).Result);
    }

    [Fact]
    public async Task Login_ChuaXacThuc_403_KemMaLoiDeMoManNhapOtp()
    {
        await Create().Register(Register("c@test.vn"));
        var result = Assert.IsType<ObjectResult>((await Create().Login(new LoginRequest("c@test.vn", "Matkhau123"))).Result);
        Assert.Equal(403, result.StatusCode);
        var body = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(AuthController.EmailNotVerifiedCode, body.GetProperty("code").GetString());
        Assert.Equal("c@test.vn", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task QuenVaDatLaiMatKhau_200()
    {
        await Verified("d@test.vn");
        Assert.Equal("d***@test.vn", (await Create().ForgotPassword(new EmailRequest("d@test.vn"))).OkValue().MaskedEmail);
        var reset = await Create().ResetPassword(new ResetPasswordRequest("d@test.vn", Emails.LastCode("d@test.vn"), "MatKhauMoi9"));
        Assert.IsType<OkObjectResult>(reset);
        Assert.IsType<UnauthorizedObjectResult>((await Create().Login(new LoginRequest("d@test.vn", "Matkhau123"))).Result);
    }

    [Fact]
    public void Logout_200()
    {
        Assert.IsType<OkObjectResult>(Create().Logout());
    }
}

/// <summary>US05 – ProfileController.</summary>
public class ProfileControllerTests : TestBase
{
    [Fact]
    public async Task Get_TraHoSoCuaNguoiDangNhap()
    {
        var user = Db.Customer(configure: u => { u.OutstandingAmount = 50_000m; u.IsCashBlocked = true; });
        var profile = (await new ProfileController(Db).AsUser(user.Id, UserRole.Customer).Get()).OkValue();
        Assert.Equal(user.Email, profile.Email);
        Assert.Equal(50_000m, profile.OutstandingAmount);
        Assert.True(profile.IsCashBlocked);
    }

    [Fact]
    public async Task Get_KhongTonTai_404()
    {
        Assert.IsType<NotFoundResult>((await new ProfileController(Db).AsUser(999, UserRole.Customer).Get()).Result);
    }

    [Fact]
    public async Task Update_TrimVaChuanHoaAnhDaiDien()
    {
        var user = Db.Customer();
        var result = await new ProfileController(Db).AsUser(user.Id, UserRole.Customer)
            .Update(new UpdateProfileRequest("  Tên Mới  ", "  ", "  12 Lê Lợi ", "http://localhost:8080/uploads/202610/av.png"));

        var p = result.OkValue();
        Assert.Equal("Tên Mới", p.FullName);
        Assert.Null(p.PhoneNumber);
        Assert.Equal("12 Lê Lợi", p.Address);
        Assert.Equal("/uploads/202610/av.png", p.AvatarUrl);
        Assert.Equal("Tên Mới", (await Fresh().Users.SingleAsync()).FullName);
    }

    [Fact]
    public async Task DoiMatKhau_CanMatKhauHienTaiVaOtpQuaEmail()
    {
        var user = Db.Customer(configure: u => u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Matkhau123"));
        ProfileController Ctrl() => new ProfileController(Db).AsUser(user.Id, UserRole.Customer);

        var sent = (await Ctrl().SendChangePasswordOtp(CreateOtp())).OkValue();
        Assert.Equal(60, sent.ResendAfterSeconds);
        Assert.Equal(user.Email, Emails.Messages.Single().To);
        Assert.Contains("đổi mật khẩu", Emails.Messages.Single().Subject);
        var code = Emails.LastCode();

        var wrongPw = await Assert.ThrowsAsync<ApiException>(() =>
            Ctrl().ChangePassword(new ChangePasswordRequest("SaiMatKhau1", code, "MatKhauMoi9"), CreateOtp()));
        Assert.Equal("Mật khẩu hiện tại không đúng.", wrongPw.Message);

        var same = await Assert.ThrowsAsync<ApiException>(() =>
            Ctrl().ChangePassword(new ChangePasswordRequest("Matkhau123", code, "Matkhau123"), CreateOtp()));
        Assert.Equal("Mật khẩu mới phải khác mật khẩu hiện tại.", same.Message);

        var wrongCode = await Assert.ThrowsAsync<ApiException>(() =>
            Ctrl().ChangePassword(new ChangePasswordRequest("Matkhau123", code == "000000" ? "111111" : "000000", "MatKhauMoi9"), CreateOtp()));
        Assert.Contains("Mã OTP không đúng", wrongCode.Message);

        Assert.IsType<OkObjectResult>(await Ctrl().ChangePassword(new ChangePasswordRequest("Matkhau123", code, "MatKhauMoi9"), CreateOtp()));
        Assert.True(BCrypt.Net.BCrypt.Verify("MatKhauMoi9", (await Fresh().Users.SingleAsync()).PasswordHash));
    }

    [Fact]
    public async Task DoiMatKhau_MaOtpQuenMatKhauKhongDungChoDoiMatKhau()
    {
        var user = Db.Customer(configure: u => { u.Email = "cheo@test.vn"; u.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Matkhau123"); });
        await new AuthService(Db, new TokenService(JwtTestConfig.Create()), CreateOtp(), CreateVouchers()).ForgotPasswordAsync("cheo@test.vn");

        var ex = await Assert.ThrowsAsync<ApiException>(() => new ProfileController(Db).AsUser(user.Id, UserRole.Customer)
            .ChangePassword(new ChangePasswordRequest("Matkhau123", Emails.LastCode(), "MatKhauMoi9"), CreateOtp()));
        Assert.Contains("chưa được gửi", ex.Message);    // mỗi mục đích có mã riêng
    }
}

/// <summary>US30 / US06 – UsersController (Admin).</summary>
public class UsersControllerTests : TestBase
{
    private UsersController AsAdmin(int adminId) => new UsersController(Db, CreateLocks()).AsUser(adminId, UserRole.Admin);

    [Fact]
    public async Task GetAll_LocTheoVaiTro_KhongPhanBietHoaThuong()
    {
        var admin = Db.Admin();
        Db.Customer();
        var tasker = Db.Tasker(configure: p => { p.ViolationPoints = 2; p.RatingAverage = 4.5m; });

        var list = (await AsAdmin(admin.Id).GetAll("tasker", null, null)).OkValue().ToList();

        var item = Assert.Single(list);
        Assert.Equal(tasker.Id, item.Id);
        Assert.Equal(TaskerVerificationStatus.Approved, item.VerificationStatus);
        Assert.Equal(2, item.ViolationPoints);
        Assert.Equal(4.5m, item.RatingAverage);
    }

    [Fact]
    public async Task GetAll_VaiTroKhongHopLe_400()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin(1).GetAll("Boss", null, null));
        Assert.Equal("Vai trò không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task GetAll_TimTheoTenEmailSdt_VaTrangThai()
    {
        var admin = Db.Admin();
        Db.Customer("Lê Thị Hoa", "0911000111");
        var locked = Db.Customer("Phạm Văn Bình", "0922000222", u => u.IsActive = false);

        Assert.Single((await AsAdmin(admin.Id).GetAll(null, "hoa", null)).OkValue());
        Assert.Single((await AsAdmin(admin.Id).GetAll(null, "0922", null)).OkValue());
        var inactive = (await AsAdmin(admin.Id).GetAll(null, null, false)).OkValue();
        Assert.Equal(locked.Id, Assert.Single(inactive).Id);
        Assert.Equal(3, (await AsAdmin(admin.Id).GetAll(null, null, null)).OkValue().Count());
    }

    [Fact]
    public async Task SetActiveStatus_KhoaTasker_NgungNhanDon()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();

        Assert.IsType<NoContentResult>(await AsAdmin(admin.Id).SetActiveStatus(tasker.Id, false));

        var fresh = Fresh();
        Assert.False((await fresh.Users.SingleAsync(u => u.Id == tasker.Id)).IsActive);
        Assert.False((await fresh.TaskerProfiles.SingleAsync()).IsAvailable);
    }

    [Fact]
    public async Task SetActiveStatus_TuKhoaChinhMinh_400()
    {
        var admin = Db.Admin();
        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin(admin.Id).SetActiveStatus(admin.Id, false));
        Assert.Equal("Không thể tự khóa tài khoản của chính mình.", ex.Message);
    }

    [Fact]
    public async Task SetActiveStatus_MoKhoa_VaKhongTonTai_404()
    {
        var admin = Db.Admin();
        var c = Db.Customer(configure: u => u.IsActive = false);
        Assert.IsType<NoContentResult>(await AsAdmin(admin.Id).SetActiveStatus(c.Id, true));
        Assert.True((await Fresh().Users.SingleAsync(u => u.Id == c.Id)).IsActive);
        Assert.IsType<NotFoundResult>(await AsAdmin(admin.Id).SetActiveStatus(999, true));
    }

    [Fact]
    public async Task SetCashBlocked_ChiApDungKhachHang()
    {
        var admin = Db.Admin();
        var c = Db.Customer(configure: u => u.IsCashBlocked = true);
        Assert.IsType<NoContentResult>(await AsAdmin(admin.Id).SetCashBlocked(c.Id, false));
        Assert.False((await Fresh().Users.SingleAsync(u => u.Id == c.Id)).IsCashBlocked);

        var tasker = Db.Tasker();
        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin(admin.Id).SetCashBlocked(tasker.Id, true));
        Assert.Equal("Chỉ áp dụng cho tài khoản khách hàng.", ex.Message);
        Assert.IsType<NotFoundResult>(await AsAdmin(admin.Id).SetCashBlocked(999, true));
    }
}

/// <summary>EP07 – NotificationsController.</summary>
public class NotificationsControllerTests : TestBase
{
    [Fact]
    public async Task GetMine_PhanTrangMoiNhatTruocVaDemChuaDoc()
    {
        var user = Db.Customer();
        var other = Db.Customer("Khác");
        var notifications = CreateNotifications();
        for (var i = 1; i <= 25; i++) notifications.Add(user.Id, $"TB {i}", "x");
        notifications.Add(other.Id, "Của người khác", "x");
        await Db.SaveChangesAsync();
        var first = await Db.Notifications.FirstAsync(n => n.Title == "TB 1");
        first.IsRead = true;
        await Db.SaveChangesAsync();

        var page1 = (await new NotificationsController(Db).AsUser(user.Id, UserRole.Customer).GetMine(1, 10, null)).OkValue();
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(24, page1.UnreadCount);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal("TB 25", page1.Items[0].Title);

        var unread = (await new NotificationsController(Db).AsUser(user.Id, UserRole.Customer).GetMine(null, 100, true)).OkValue();
        Assert.Equal(24, unread.TotalCount);
    }

    [Fact]
    public async Task MarkRead_ChiDanhDauCuaMinh()
    {
        var user = Db.Customer();
        var other = Db.Customer("Khác");
        CreateNotifications().Add(other.Id, "A", "B");
        await Db.SaveChangesAsync();
        var id = (await Db.Notifications.SingleAsync()).Id;

        Assert.IsType<NotFoundResult>(await new NotificationsController(Db).AsUser(user.Id, UserRole.Customer).MarkRead(id));
        Assert.IsType<NoContentResult>(await new NotificationsController(Db).AsUser(other.Id, UserRole.Customer).MarkRead(id));
        Assert.True((await Fresh().Notifications.SingleAsync()).IsRead);
    }

    [Fact]
    public async Task MarkAllRead_DanhDauTatCa()
    {
        var user = Db.Customer();
        var n = CreateNotifications();
        n.Add(user.Id, "A", "1");
        n.Add(user.Id, "B", "2");
        await Db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await new NotificationsController(Db).AsUser(user.Id, UserRole.Customer).MarkAllRead());
        Assert.All(await Fresh().Notifications.ToListAsync(), x => Assert.True(x.IsRead));
    }
}
