using System.Security.Claims;
using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using HomeService.Api.Options;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HomeService.Api.Tests.Common;

/// <summary>Các hàm tiện ích dùng chung (thư mục Common, Extensions, Options) – không cần CSDL.</summary>
public class MoneyTests
{
    [Theory]
    [InlineData("150000", "0.2", "30000")]
    [InlineData("99999", "0.2", "20000")]     // 19999.8 → 20000
    [InlineData("12345", "0.2", "2469")]      // 2469.0
    [InlineData("2", "0.25", "1")]            // 0.5 → làm tròn lên (AwayFromZero)
    [InlineData("0", "0.2", "0")]
    public void Commission_LamTronDenDong(string amount, string rate, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Money.Commission(decimal.Parse(amount), decimal.Parse(rate)));
    }

    [Theory]
    [InlineData("10.5", "11")]
    [InlineData("10.4", "10")]
    [InlineData("-10.5", "-11")]
    public void Round_MidpointAwayFromZero(string value, string expected)
    {
        Assert.Equal(decimal.Parse(expected), Money.Round(decimal.Parse(value)));
    }

    [Theory]
    [InlineData("450000", "450.000 đ")]
    [InlineData("0", "0 đ")]
    [InlineData("1234567", "1.234.567 đ")]
    [InlineData("999", "999 đ")]
    public void MoneyFormat_Vnd_DungDinhDangViet(string amount, string expected)
    {
        Assert.Equal(expected, MoneyFormat.Vnd(decimal.Parse(amount)));
    }
}

public class TimeHelperTests
{
    [Fact]
    public void VnTime_Format_CongThem7Gio()
    {
        var utc = new DateTime(2026, 10, 28, 17, 30, 0, DateTimeKind.Utc);
        Assert.Equal("29/10/2026 00:30", VnTime.Format(utc));
    }

    [Fact]
    public void ToUtc_GiuNguyenKhiDaLaUtc()
    {
        var utc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, DateTimeHelper.ToUtc(utc));
        Assert.Equal(DateTimeKind.Utc, DateTimeHelper.ToUtc(utc).Kind);
    }

    [Fact]
    public void ToUtc_KhongCoKind_HieuLaUtc()
    {
        var unspecified = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var result = DateTimeHelper.ToUtc(unspecified);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(8, result.Hour);
    }

    [Fact]
    public void ToUtc_GioLocal_DoiSangUtc()
    {
        var local = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime(), DateTimeHelper.ToUtc(local));
    }
}

public class GeoHelperTests
{
    [Fact]
    public void DistanceKm_CungMotDiem_BangKhong()
    {
        Assert.Equal(0, GeoHelper.DistanceKm(10.7725, 106.698, 10.7725, 106.698), 6);
    }

    [Fact]
    public void DistanceKm_BenThanhDenQuan3_KhoangHaiKm()
    {
        var d = GeoHelper.DistanceKm(10.7725, 106.6980, 10.7843, 106.6844);
        Assert.InRange(d, 1.8, 2.2);
    }

    [Fact]
    public void DistanceKm_MotDoKinhTuyen_Khoang111Km()
    {
        Assert.InRange(GeoHelper.DistanceKm(10, 106, 11, 106), 110.5, 111.8);
    }

    [Fact]
    public void DistanceKm_DoiXung()
    {
        Assert.Equal(GeoHelper.DistanceKm(10.77, 106.69, 10.85, 106.75), GeoHelper.DistanceKm(10.85, 106.75, 10.77, 106.69), 9);
    }
}

public class SkillMatcherTests
{
    [Theory]
    [InlineData("Dọn dẹp, Sửa điện nước", "Dọn dẹp nhà", true)]
    [InlineData("dọn dẹp", "Dọn dẹp nhà", true)]            // không phân biệt hoa thường
    [InlineData("Sửa điện nước", "Sửa điện nước", true)]
    [InlineData("Giặt ủi tận nơi", "Giặt ủi", true)]          // kỹ năng chứa tên dịch vụ
    [InlineData("Nấu ăn", "Dọn dẹp nhà", false)]
    [InlineData("", "Dọn dẹp nhà", false)]
    [InlineData(null, "Dọn dẹp nhà", false)]
    [InlineData(" , ", "Dọn dẹp nhà", false)]
    public void Matches(string? skills, string service, bool expected)
    {
        Assert.Equal(expected, SkillMatcher.Matches(skills, service));
    }
}

public class UploadUrlTests
{
    [Theory]
    [InlineData("http://localhost:8080/uploads/202610/a.png", "/uploads/202610/a.png")]
    [InlineData("https://api.homeservice.id.vn/uploads/202610/b.jpg", "/uploads/202610/b.jpg")]
    [InlineData("HTTP://10.0.2.2:8080/uploads/x.png", "/uploads/x.png")]
    [InlineData("/uploads/202610/c.png", "/uploads/202610/c.png")]
    [InlineData("  /uploads/d.png  ", "/uploads/d.png")]
    [InlineData("https://images.example.com/photo.png", "https://images.example.com/photo.png")] // ảnh ngoài giữ nguyên
    public void Normalize_BoTenMayChu(string input, string expected)
    {
        Assert.Equal(expected, UploadUrl.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RongTraVeNull(string? input)
    {
        Assert.Null(UploadUrl.Normalize(input));
    }

    [Fact]
    public void NormalizeJoined_ChuanHoaTungPhanTu()
    {
        var result = UploadUrl.NormalizeJoined("http://localhost:8080/uploads/a.png,/uploads/b.png");
        Assert.Equal("/uploads/a.png,/uploads/b.png", result);
    }

    [Fact]
    public void NormalizeJoined_RongGiuNguyen()
    {
        Assert.Null(UploadUrl.NormalizeJoined(null));
        Assert.Equal("", UploadUrl.NormalizeJoined(""));
    }

    [Theory]
    [InlineData("http://localhost:8080/uploads/a.png", true)]
    [InlineData("/uploads/a.png", false)]
    [InlineData("https://cdn.example.com/a.png", false)]
    [InlineData(null, false)]
    public void NeedsNormalize(string? url, bool expected)
    {
        Assert.Equal(expected, UploadUrl.NeedsNormalize(url));
    }
}

public class WorkingAreasTests
{
    // SRS 7.9 – khu vực là nhãn lấy tự động từ địa chỉ điểm làm việc (định dạng của GeoService.FormatAddress)
    [Theory]
    [InlineData("12 Nguyễn Huệ, Phường Bến Nghé, Quận 1, Thành phố Hồ Chí Minh", "Quận 1")]
    [InlineData("45 Lê Văn Khương, Phường Thới An, Quận 12, Thành phố Hồ Chí Minh, Việt Nam", "Quận 12")]
    [InlineData("8 Trưng Nữ Vương, Xã Thới Tam Thôn, Huyện Hóc Môn, Thành phố Hồ Chí Minh", "Huyện Hóc Môn")]
    [InlineData("1 Võ Văn Ngân, Phường Linh Chiểu, Thành phố Thủ Đức, Thành phố Hồ Chí Minh", "Thành phố Thủ Đức")]
    [InlineData("135 Nam Kỳ Khởi Nghĩa, Phường Sài Gòn, Thành phố Hồ Chí Minh", "Phường Sài Gòn")]
    [InlineData("Đường Tôn Đức Thắng, Đặc khu Côn Đảo, Thành phố Hồ Chí Minh", "Đặc khu Côn Đảo")]
    [InlineData("12 Nguyễn Huệ,  Bến Nghé , TP.HCM, 700000", "Bến Nghé")]
    [InlineData("Chợ Bến Thành", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void FromAddress_LayNhanKhuVucTuDiaChi(string? address, string? expected)
    {
        Assert.Equal(expected, WorkingAreas.FromAddress(address));
    }

    [Fact]
    public void FromAddress_CatToiDa100KyTu()
    {
        var area = WorkingAreas.FromAddress("1 Đường A, Phường " + new string('X', 200) + ", Thành phố Hồ Chí Minh");
        Assert.NotNull(area);
        Assert.Equal(WorkingAreas.MaxLength, area!.Length);
        Assert.StartsWith("Phường X", area);
    }
}

public class ApiExceptionTests
{
    [Fact]
    public void Factory_GanDungMaHttp()
    {
        Assert.Equal(400, ApiException.BadRequest("x").StatusCode);
        Assert.Equal(409, ApiException.Conflict("x").StatusCode);
        Assert.Equal(403, ApiException.Forbidden().StatusCode);
        Assert.Equal(404, ApiException.NotFound().StatusCode);
        Assert.Equal("Lỗi", ApiException.BadRequest("Lỗi").Message);
        Assert.Equal(string.Empty, ApiException.NotFound().Message);
    }
}

public class PagingTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 500, 1, 100)]
    [InlineData(3, 50, 3, 50)]
    public void Normalize_GioiHanTrangVaKichThuoc(int? page, int? size, int expectedPage, int expectedSize)
    {
        var (p, s) = Paging.Normalize(page, size);
        Assert.Equal(expectedPage, p);
        Assert.Equal(expectedSize, s);
    }
}

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetUserId_DocTuNameIdentifier()
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "42") }));
        Assert.Equal(42, p.GetUserId());
    }

    [Fact]
    public void GetUserId_DocTuSub()
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "7") }));
        Assert.Equal(7, p.GetUserId());
    }

    [Fact]
    public void GetUserId_KhongHopLe_TraVe0()
    {
        Assert.Equal(0, new ClaimsPrincipal(new ClaimsIdentity()).GetUserId());
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "abc") }));
        Assert.Equal(0, p.GetUserId());
    }

    [Fact]
    public void GetRole_VaIsInRole()
    {
        var p = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Tasker") }));
        Assert.Equal(UserRole.Tasker, p.GetRole());
        Assert.True(p.IsInRole(UserRole.Tasker));
        Assert.False(p.IsInRole(UserRole.Admin));
        Assert.Null(new ClaimsPrincipal(new ClaimsIdentity()).GetRole());
    }
}

public class OptionsAndModelTests
{
    [Fact]
    public void MatchingOptions_MacDinh3Dot()
    {
        var o = new MatchingOptions();
        Assert.Equal(new double[] { 3, 5, 8 }, o.Radii);
        Assert.Equal(new[] { 45, 45, 60 }, o.Timeouts);
        Assert.Equal(5, o.MaxRounds);
    }

    [Fact]
    public void MatchingOptions_DungCauHinhNeuCo()
    {
        var o = new MatchingOptions { WaveRadiusKm = new double[] { 2, 4 }, WaveTimeoutSeconds = new[] { 30, 30 } };
        Assert.Equal(new double[] { 2, 4 }, o.Radii);
        Assert.Equal(new[] { 30, 30 }, o.Timeouts);
    }

    [Fact]
    public void SettlementOptions_GiaTriMacDinhTheoSrs()
    {
        var o = new SettlementOptions();
        Assert.Equal(15, o.PaymentTimeoutMinutes);
        Assert.Equal(3, o.CashMinCompletedOrders);
        Assert.Equal(1_000_000m, o.CashMaxOrderValue);
        Assert.Equal(24, o.AutoConfirmHours);
        Assert.Equal(20m, o.LateCancelFeePercent);
        Assert.Equal(50m, o.ArrivedCancelFeePercent);
        Assert.Equal(3, o.ViolationPointsToSuspend);
    }

    [Fact]
    public void BusinessOptions_ChietKhau20PhanTram()
    {
        Assert.Equal(0.2m, new BusinessOptions().CommissionRate);
    }

    [Fact]
    public void ResolveUploadsPath_MacDinhWwwrootUploads()
    {
        var config = new ConfigurationBuilder().Build();
        var root = Path.Combine(Path.GetTempPath(), "app");
        Assert.Equal(Path.Combine(root, "wwwroot", "uploads"), StorageOptions.ResolveUploadsPath(config, root));
    }

    [Fact]
    public void ResolveUploadsPath_DuongDanTuongDoiVaTuyetDoi()
    {
        var root = Path.Combine(Path.GetTempPath(), "app");
        var relative = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsPath"] = "data/up" }).Build();
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "data/up")), StorageOptions.ResolveUploadsPath(relative, root));

        var absPath = Path.Combine(Path.GetTempPath(), "uploads-abs");
        var absolute = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsPath"] = absPath }).Build();
        Assert.Equal(Path.GetFullPath(absPath), StorageOptions.ResolveUploadsPath(absolute, root));
    }

    [Fact]
    public void TaskerProfile_AvailableBalance_BangSoDuTruTamGiu()
    {
        var p = new TaskerProfile { WalletBalance = 200_000m, ReservedAmount = 30_000m };
        Assert.Equal(170_000m, p.AvailableBalance);
    }

    [Fact]
    public void Dispute_JoinVaSplitUrls()
    {
        var joined = Dispute.JoinUrls(new[] { "http://localhost:8080/uploads/a.png", "", "/uploads/b.png" });
        Assert.Equal("/uploads/a.png,/uploads/b.png", joined);
        Assert.Equal(new List<string> { "/uploads/a.png", "/uploads/b.png" }, Dispute.SplitUrls(joined));
        Assert.Empty(Dispute.SplitUrls(null));
        Assert.Equal("", Dispute.JoinUrls(null));
    }

    [Fact]
    public void Roles_KhopTenEnum()
    {
        Assert.Equal(Roles.Customer, UserRole.Customer.ToString());
        Assert.Equal(Roles.Tasker, UserRole.Tasker.ToString());
        Assert.Equal(Roles.Admin, UserRole.Admin.ToString());
        Assert.Equal("Customer,Tasker", Roles.CustomerOrTasker);
    }
}
