using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HomeService.Api.Tests.Services;

internal static class JwtTestConfig
{
    public const string Key = "khoa-bi-mat-dung-cho-unit-test-dai-hon-32-ky-tu!!";

    public static IConfiguration Create(string? expiry = "60") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = Key,
            ["Jwt:Issuer"] = "HomeService",
            ["Jwt:Audience"] = "HomeServiceClients",
            ["Jwt:AccessTokenExpiryMinutes"] = expiry,
        }).Build();
}

/// <summary>US01 – Đăng ký + xác thực email OTP, US02 – Đăng nhập, quên mật khẩu (AuthService).</summary>
public class AuthServiceTests : TestBase
{
    private AuthService CreateService() => new(Db, new TokenService(JwtTestConfig.Create()), CreateOtp(), CreateVouchers());

    private static RegisterRequest Register(string email = "Khach@Test.vn", UserRole role = UserRole.Customer, string? phone = " 0901234567 ",
        string name = "  Nguyễn Văn A  ")
        => new(name, email, "Matkhau123", phone, role);

    /// <summary>Đăng ký rồi nhập đúng mã OTP trong email → tài khoản đã kích hoạt.</summary>
    private async Task<AuthResponse> RegisterVerified(string email = "khach@test.vn", UserRole role = UserRole.Customer)
    {
        await CreateService().RegisterAsync(Register(email, role));
        return await CreateService().VerifyEmailAsync(new VerifyEmailRequest(email, Emails.LastCode(AuthService.NormalizeEmail(email))));
    }

    /// <summary>Lùi thời điểm tạo các mã OTP để "qua" thời gian chờ gửi lại.</summary>
    private async Task AgeOtps(int seconds = 61)
    {
        foreach (var o in await Db.OtpCodes.ToListAsync()) o.CreatedAt = o.CreatedAt.AddSeconds(-seconds);
        await Db.SaveChangesAsync();
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    [Fact]
    public async Task Register_KhachHang_TaoTaiKhoanChuaKichHoat_VaGuiOtpQuaEmail()
    {
        var result = await CreateService().RegisterAsync(Register());

        Assert.Equal("khach@test.vn", result.Email);            // email chuẩn hóa chữ thường
        Assert.Equal("kh***@test.vn", result.MaskedEmail);
        Assert.Equal(60, result.ResendAfterSeconds);
        Assert.InRange((result.ExpiresAt - DateTime.UtcNow).TotalMinutes, 4.9, 5.1);

        var user = await Fresh().Users.SingleAsync();
        Assert.False(user.EmailVerified);                      // chưa nhập OTP thì chưa kích hoạt
        Assert.Equal("Nguyễn Văn A", user.FullName);
        Assert.Equal("0901234567", user.PhoneNumber);           // đã trim
        Assert.True(BCrypt.Net.BCrypt.Verify("Matkhau123", user.PasswordHash));
        Assert.Null(await Fresh().TaskerProfiles.FirstOrDefaultAsync());

        var mail = Emails.Messages.Single();
        Assert.Equal("khach@test.vn", mail.To);
        Assert.Contains("Mã xác thực đăng ký", mail.Subject);
        var code = Emails.LastCode();
        Assert.Matches(@"^\d{6}$", code);
        var otp = await Fresh().OtpCodes.SingleAsync();
        Assert.Equal(OtpPurpose.VerifyEmail, otp.Purpose);
        Assert.DoesNotContain(code, otp.CodeHash);              // chỉ lưu bản băm, không lưu mã gốc
    }

    [Fact]
    public async Task Register_Tasker_TaoSanHoSoChoDuyet()
    {
        await CreateService().RegisterAsync(Register(role: UserRole.Tasker));
        var user = await Fresh().Users.SingleAsync();
        var profile = await Fresh().TaskerProfiles.SingleAsync();
        Assert.Equal(user.Id, profile.UserId);
        Assert.Equal(TaskerVerificationStatus.Pending, profile.VerificationStatus);
    }

    [Fact]
    public async Task Register_SoDienThoaiRong_LuuNull()
    {
        await CreateService().RegisterAsync(Register(phone: "   "));
        Assert.Null((await Fresh().Users.SingleAsync()).PhoneNumber);
    }

    [Fact]
    public async Task Register_VaiTroAdmin_BiTuChoi()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().RegisterAsync(Register(role: UserRole.Admin)));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Vai trò không hợp lệ.", ex.Message);
        Assert.Empty(Emails.Messages);
    }

    [Fact]
    public async Task Register_EmailTrungKhongPhanBietHoaThuong_BiTuChoi()
    {
        await RegisterVerified("a@test.vn");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().RegisterAsync(Register("A@TEST.VN")));
        Assert.Equal("Email đã được sử dụng.", ex.Message);
    }

    [Fact]
    public async Task Register_EmailChuaXacThuc_DangKyLaiGhiDeThongTinVaGuiMaMoi()
    {
        await CreateService().RegisterAsync(Register("lai@test.vn", UserRole.Tasker));
        await AgeOtps();
        await CreateService().RegisterAsync(Register("lai@test.vn", UserRole.Customer, name: "Tên Mới"));

        var user = await Fresh().Users.SingleAsync();
        Assert.Equal("Tên Mới", user.FullName);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.Null(await Fresh().TaskerProfiles.FirstOrDefaultAsync());   // đổi vai trò thì bỏ hồ sơ Tasker cũ
        Assert.Equal(2, Emails.Messages.Count);
    }

    [Fact]
    public async Task VerifyEmail_DungMa_KichHoatVaDangNhapLuon()
    {
        await CreateService().RegisterAsync(Register("xacthuc@test.vn"));
        var auth = await CreateService().VerifyEmailAsync(new VerifyEmailRequest(" XacThuc@Test.vn ", Emails.LastCode()));

        Assert.Equal("xacthuc@test.vn", auth.Email);
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.True((await Fresh().Users.SingleAsync()).EmailVerified);
        Assert.NotNull((await Fresh().OtpCodes.SingleAsync()).UsedAt);

        var again = await Assert.ThrowsAsync<ApiException>(() =>
            CreateService().VerifyEmailAsync(new VerifyEmailRequest("xacthuc@test.vn", Emails.LastCode())));
        Assert.Equal("Email đã được xác thực. Vui lòng đăng nhập.", again.Message);
    }

    [Fact]
    public async Task VerifyEmail_SaiMa5Lan_KhoaMa_PhaiGuiMaMoi()
    {
        await CreateService().RegisterAsync(Register("sai@test.vn"));
        var code = Emails.LastCode();

        for (var left = 4; left >= 0; left--)
        {
            var ex = await Assert.ThrowsAsync<ApiException>(() =>
                CreateService().VerifyEmailAsync(new VerifyEmailRequest("sai@test.vn", Wrong(code))));
            Assert.Contains(left > 0 ? $"còn {left} lần thử" : "hết lượt thử", ex.Message);
        }
        Assert.Equal(5, (await Fresh().OtpCodes.SingleAsync()).Attempts);

        // Đã khóa: nhập đúng cũng không được nữa
        var locked = await Assert.ThrowsAsync<ApiException>(() =>
            CreateService().VerifyEmailAsync(new VerifyEmailRequest("sai@test.vn", code)));
        Assert.Contains("quá số lần cho phép", locked.Message);

        await AgeOtps();
        await CreateService().ResendVerificationAsync("sai@test.vn");
        Assert.False(string.IsNullOrEmpty((await CreateService().VerifyEmailAsync(
            new VerifyEmailRequest("sai@test.vn", Emails.LastCode()))).AccessToken));
    }

    [Fact]
    public async Task VerifyEmail_MaHetHan_BiTuChoi()
    {
        await CreateService().RegisterAsync(Register("hethan@test.vn"));
        var otp = await Db.OtpCodes.SingleAsync();
        otp.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateService().VerifyEmailAsync(new VerifyEmailRequest("hethan@test.vn", Emails.LastCode())));
        Assert.Contains("hết hạn", ex.Message);
        Assert.False((await Fresh().Users.SingleAsync()).EmailVerified);
    }

    [Fact]
    public async Task ResendVerification_ChoDu60Giay_VaToiDa5MaMoiGio()
    {
        await CreateService().RegisterAsync(Register("guilai@test.vn"));

        var wait = await Assert.ThrowsAsync<ApiException>(() => CreateService().ResendVerificationAsync("guilai@test.vn"));
        Assert.Equal(429, wait.StatusCode);
        Assert.Contains("Vui lòng đợi", wait.Message);

        for (var i = 0; i < 4; i++)
        {
            await AgeOtps();
            await CreateService().ResendVerificationAsync("guilai@test.vn");
        }
        Assert.Equal(5, Emails.Messages.Count);
        // Mã cũ hết hiệu lực ngay khi có mã mới
        Assert.Single(await Fresh().OtpCodes.Where(o => o.ExpiresAt > DateTime.UtcNow).ToListAsync());

        await AgeOtps();
        var limit = await Assert.ThrowsAsync<ApiException>(() => CreateService().ResendVerificationAsync("guilai@test.vn"));
        Assert.Equal(429, limit.StatusCode);
        Assert.Contains("quá nhiều mã OTP", limit.Message);
    }

    [Fact]
    public async Task Login_DungThongTin_TraToken()
    {
        await RegisterVerified("dangnhap@test.vn");
        var result = await CreateService().LoginAsync(new LoginRequest("  DangNhap@Test.vn ", "Matkhau123"));
        Assert.Equal("dangnhap@test.vn", result.Email);
        Assert.False(string.IsNullOrEmpty(result.AccessToken));
    }

    [Fact]
    public async Task Login_ChuaXacThucEmail_TuGuiMaMoi_VaChuaChoDangNhap()
    {
        await CreateService().RegisterAsync(Register("chuaxt@test.vn"));

        // Vừa gửi mã lúc đăng ký → trong thời gian chờ thì không gửi thêm
        var first = await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            CreateService().LoginAsync(new LoginRequest("chuaxt@test.vn", "Matkhau123")));
        Assert.Null(first.Otp);
        Assert.Contains("ch****@test.vn", first.Message);
        Assert.Single(Emails.Messages);

        await AgeOtps();
        var second = await Assert.ThrowsAsync<EmailNotVerifiedException>(() =>
            CreateService().LoginAsync(new LoginRequest("chuaxt@test.vn", "Matkhau123")));
        Assert.NotNull(second.Otp);
        Assert.Equal(2, Emails.Messages.Count);

        // Sai mật khẩu thì vẫn báo lỗi chung, không gửi mã
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().LoginAsync(new LoginRequest("chuaxt@test.vn", "Sai123456")));
        Assert.Equal(2, Emails.Messages.Count);
    }

    [Fact]
    public async Task Login_SaiMatKhau_BaoLoiChung()
    {
        await RegisterVerified("a@test.vn");
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().LoginAsync(new LoginRequest("a@test.vn", "SaiMatKhau1")));
        Assert.Equal("Email hoặc mật khẩu không đúng.", ex.Message);
    }

    [Fact]
    public async Task Login_EmailKhongTonTai_BaoLoiChung()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().LoginAsync(new LoginRequest("khongco@test.vn", "Matkhau123")));
        Assert.Equal("Email hoặc mật khẩu không đúng.", ex.Message);
    }

    [Fact]
    public async Task Login_TaiKhoanBiKhoa_BiTuChoi()
    {
        await RegisterVerified("khoa@test.vn");
        var user = await Db.Users.SingleAsync();
        user.IsActive = false;
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().LoginAsync(new LoginRequest("khoa@test.vn", "Matkhau123")));
        Assert.Equal("Tài khoản đã bị khóa.", ex.Message);
    }

    [Fact]
    public async Task QuenMatKhau_DatLaiBangOtp_MaChiDungMotLan()
    {
        await RegisterVerified("quen@test.vn");
        var sent = await CreateService().ForgotPasswordAsync(" Quen@Test.vn ");
        Assert.Equal("qu***@test.vn", sent.MaskedEmail);
        Assert.Contains("đặt lại mật khẩu", Emails.Messages.Last().Subject);
        var code = Emails.LastCode();

        await CreateService().ResetPasswordAsync(new ResetPasswordRequest("quen@test.vn", code, "MatKhauMoi9"));
        Assert.False(string.IsNullOrEmpty((await CreateService().LoginAsync(new LoginRequest("quen@test.vn", "MatKhauMoi9"))).AccessToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CreateService().LoginAsync(new LoginRequest("quen@test.vn", "Matkhau123")));

        var reuse = await Assert.ThrowsAsync<ApiException>(() =>
            CreateService().ResetPasswordAsync(new ResetPasswordRequest("quen@test.vn", code, "LanNua123")));
        Assert.Contains("hết hạn hoặc chưa được gửi", reuse.Message);
    }

    [Fact]
    public async Task QuenMatKhau_EmailKhongTonTaiHoacBiKhoa_TraKetQuaChung_KhongGuiMail()
    {
        var sent = await CreateService().ForgotPasswordAsync("khongco@test.vn");
        Assert.Equal("kh*****@test.vn", sent.MaskedEmail);
        Assert.Equal(60, sent.ResendAfterSeconds);

        Db.Customer(configure: u => { u.Email = "bikhoa@test.vn"; u.IsActive = false; });
        await CreateService().ForgotPasswordAsync("bikhoa@test.vn");
        Assert.Empty(Emails.Messages);
        Assert.Empty(await Fresh().OtpCodes.ToListAsync());
    }

    [Fact]
    public async Task QuenMatKhau_GuiMaMoi_MaCuHetHieuLuc()
    {
        await RegisterVerified("macu@test.vn");
        await AgeOtps();
        await CreateService().ForgotPasswordAsync("macu@test.vn");
        var oldCode = Emails.LastCode();
        await AgeOtps();
        await CreateService().ForgotPasswordAsync("macu@test.vn");
        var newCode = Emails.LastCode();

        if (oldCode != newCode)
            await Assert.ThrowsAsync<ApiException>(() =>
                CreateService().ResetPasswordAsync(new ResetPasswordRequest("macu@test.vn", oldCode, "MatKhauMoi9")));
        await CreateService().ResetPasswordAsync(new ResetPasswordRequest("macu@test.vn", newCode, "MatKhauMoi9"));
        Assert.True(BCrypt.Net.BCrypt.Verify("MatKhauMoi9", (await Fresh().Users.SingleAsync()).PasswordHash));
    }

    [Fact]
    public async Task QuenMatKhau_TaiKhoanChuaXacThuc_DatLaiXongCoiNhuDaXacThuc()
    {
        await CreateService().RegisterAsync(Register("chuaxt2@test.vn"));
        await AgeOtps();
        await CreateService().ForgotPasswordAsync("chuaxt2@test.vn");
        await CreateService().ResetPasswordAsync(new ResetPasswordRequest("chuaxt2@test.vn", Emails.LastCode(), "MatKhauMoi9"));
        Assert.True((await Fresh().Users.SingleAsync()).EmailVerified);
    }

    [Fact]
    public async Task GuiOtp_EmailLoi_503_KhongLuuMa_GuiLaiDuocNgay()
    {
        await RegisterVerified("smtp@test.vn");
        Emails.Fail = true;
        await AgeOtps();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateService().ForgotPasswordAsync("smtp@test.vn"));
        Assert.Equal(503, ex.StatusCode);
        Assert.Contains("Không gửi được email", ex.Message);
        Assert.Empty(await Fresh().OtpCodes.Where(o => o.Purpose == OtpPurpose.ResetPassword).ToListAsync());

        Emails.Fail = false;   // không phải đợi 60 giây vì lần trước gửi lỗi
        await CreateService().ForgotPasswordAsync("smtp@test.vn");
        Assert.Single(await Fresh().OtpCodes.Where(o => o.Purpose == OtpPurpose.ResetPassword).ToListAsync());
    }

    [Theory]
    [InlineData("minhanh@demo.vn", "mi*****@demo.vn")]
    [InlineData("ab@x.vn", "a***@x.vn")]
    [InlineData("a@x.vn", "a***@x.vn")]
    public void MaskEmail_CheBotTenHopThu(string email, string expected)
    {
        Assert.Equal(expected, OtpService.MaskEmail(email));
    }

    [Fact]
    public void OtpEmail_NoiDungTheoMucDich_CoMaVaThoiHan()
    {
        var (subject, html, text) = OtpEmail.Build("Lan <b>", "042517", OtpPurpose.Withdraw, 5, 250_000m);
        Assert.Equal("[HomeService] Mã xác nhận rút tiền ví HomeService", subject);
        Assert.Contains("042517", html);
        Assert.Contains("250.000 đ", text);
        Assert.Contains("5 phút", text);
        Assert.Contains("Lan &lt;b&gt;", html);                // tên người dùng được mã hóa HTML
        Assert.Contains("đặt lại mật khẩu", OtpEmail.Build("A", "1", OtpPurpose.ResetPassword, 5).Text);
    }

    [Theory]
    [InlineData("  A@B.VN ", "a@b.vn")]
    [InlineData("x@y.com", "x@y.com")]
    public void NormalizeEmail(string input, string expected)
    {
        Assert.Equal(expected, AuthService.NormalizeEmail(input));
    }
}

/// <summary>JWT do TokenService phát hành.</summary>
public class TokenServiceTests
{
    private static User SampleUser() => new() { Id = 15, FullName = "Đặng Phi", Email = "phi@test.vn", Role = UserRole.Admin };

    [Fact]
    public void GenerateAccessToken_ChuaDuClaim()
    {
        var (token, _) = new TokenService(JwtTestConfig.Create()).GenerateAccessToken(SampleUser());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("15", jwt.Subject);
        Assert.Equal("phi@test.vn", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Admin", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("Đặng Phi", jwt.Claims.First(c => c.Type == "fullName").Value);
        Assert.Equal("HomeService", jwt.Issuer);
        Assert.Contains("HomeServiceClients", jwt.Audiences);
    }

    [Fact]
    public void GenerateAccessToken_HetHanTheoCauHinh()
    {
        var before = DateTime.UtcNow;
        var (_, expires) = new TokenService(JwtTestConfig.Create("30")).GenerateAccessToken(SampleUser());
        Assert.InRange(expires, before.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public void GenerateAccessToken_KhongCauHinhHan_MacDinh60Phut()
    {
        var before = DateTime.UtcNow;
        var (_, expires) = new TokenService(JwtTestConfig.Create(null)).GenerateAccessToken(SampleUser());
        Assert.InRange(expires, before.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void GenerateAccessToken_ChuKyHopLe()
    {
        var (token, _) = new TokenService(JwtTestConfig.Create()).GenerateAccessToken(SampleUser());
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = "HomeService",
            ValidAudience = "HomeServiceClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtTestConfig.Key)),
        }, out _);
        Assert.NotNull(principal);
    }

    [Fact]
    public void GenerateAccessToken_SaiKhoa_KhongXacThucDuoc()
    {
        var (token, _) = new TokenService(JwtTestConfig.Create()).GenerateAccessToken(SampleUser());
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = "HomeService",
            ValidAudience = "HomeServiceClients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("mot-khoa-khac-hoan-toan-dai-hon-32-ky-tu!!")),
        };
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
    }
}
