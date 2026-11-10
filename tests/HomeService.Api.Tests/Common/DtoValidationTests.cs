using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using Xunit;

namespace HomeService.Api.Tests.Common;

/// <summary>
/// Quy tắc kiểm tra dữ liệu đầu vào (SRS mục 6.1) khai báo bằng DataAnnotations trên DTO.
/// - DTO dạng class: kiểm tra bằng Validator (giống ASP.NET Core model validation).
/// - DTO dạng record: attribute nằm trên tham số constructor nên đọc trực tiếp attribute của tham số.
/// </summary>
public class DtoValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    /// <summary>Kiểm tra 1 giá trị với mọi ValidationAttribute của tham số record.</summary>
    private static bool ParamValid<TRecord>(string parameter, object? value)
    {
        var ctor = typeof(TRecord).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var p = ctor.GetParameters().Single(x => x.Name == parameter);
        return p.GetCustomAttributes<ValidationAttribute>().All(a => a.IsValid(value));
    }

    private static CreateBookingRequest ValidBooking() => new()
    {
        ServiceCategoryId = 1,
        ScheduledTime = DateTime.UtcNow.AddHours(3),
        Address = "1 Lê Lợi, Quận 1",
        Latitude = 10.77,
        Longitude = 106.69,
        Quantity = 2,
    };

    [Fact]
    public void CreateBookingRequest_HopLe()
    {
        Assert.Empty(Validate(ValidBooking()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void CreateBookingRequest_SoLuongNgoai1Den50_Loi(int quantity)
    {
        // Giới hạn chi tiết theo từng dịch vụ (min–max) do BookingService kiểm tra; DTO chỉ chặn giá trị vô lý
        var req = ValidBooking();
        req.Quantity = quantity;
        Assert.Contains(Validate(req), r => r.ErrorMessage == "Số lượng không hợp lệ.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void CreateBookingRequest_DiaChiQuaNgan_Loi(string address)
    {
        var req = ValidBooking();
        req.Address = address;
        Assert.Contains(Validate(req), r => r.ErrorMessage == "Vui lòng nhập địa chỉ.");
    }

    [Theory]
    [InlineData("0901234567", true)]
    [InlineData("901234567", false)]
    [InlineData("09012345678", false)]
    [InlineData("09a1234567", false)]
    public void CreateBookingRequest_SoDienThoaiLienHe(string phone, bool valid)
    {
        var req = ValidBooking();
        req.ContactPhone = phone;
        Assert.Equal(valid, Validate(req).Count == 0);
    }

    [Fact]
    public void CreateBookingRequest_ThieuDichVuVaThoiGian_Loi()
    {
        var req = ValidBooking();
        req.ServiceCategoryId = null;
        req.ScheduledTime = null;
        var errors = Validate(req).Select(r => r.ErrorMessage).ToList();
        Assert.Contains("Dịch vụ không tồn tại.", errors);
        Assert.Contains("Thời gian đặt không hợp lệ.", errors);
    }

    [Theory]
    [InlineData(49_999.0, false)]
    [InlineData(50_000.0, true)]
    [InlineData(10_000_000.0, true)]
    [InlineData(10_000_001.0, false)]
    public void TopUpRequest_SoTienNap(double amount, bool valid)
    {
        Assert.Equal(valid, Validate(new TopUpRequest { Amount = (decimal)amount }).Count == 0);
    }

    [Theory]
    [InlineData(49_999.0, false)]
    [InlineData(50_000.0, true)]
    public void WithdrawRequest_SoTienRutToiThieu(double amount, bool valid)
    {
        Assert.Equal(valid, Validate(new WithdrawRequest { Amount = (decimal)amount, OtpCode = "123456" }).Count == 0);
        Assert.Equal(valid, Validate(new WithdrawOtpRequest { Amount = (decimal)amount }).Count == 0);
    }

    [Theory]
    [InlineData("123456", true)]
    [InlineData("000000", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("12a456", false)]
    [InlineData(null, false)]
    public void MaOtp_Gom6ChuSo(string? code, bool valid)
    {
        Assert.Equal(valid, Validate(new WithdrawRequest { Amount = 100_000m, OtpCode = code }).Count == 0);
        Assert.Equal(valid, ParamValid<VerifyEmailRequest>("Code", code));
        Assert.Equal(valid, ParamValid<ResetPasswordRequest>("Code", code));
        Assert.Equal(valid, ParamValid<ChangePasswordRequest>("Code", code));
    }

    [Theory]
    [InlineData("Matkhau123", true)]
    [InlineData("matkhau", false)]
    [InlineData("12345678", false)]
    public void MatKhauMoi_QuenVaDoiMatKhau_CungQuyTacDangKy(string password, bool valid)
    {
        Assert.Equal(valid, ParamValid<ResetPasswordRequest>("NewPassword", password));
        Assert.Equal(valid, ParamValid<ChangePasswordRequest>("NewPassword", password));
    }

    [Fact]
    public void CreateDisputeRequest_PhaiCo1Den5Anh_VaMoTaToiThieu10KyTu()
    {
        var ok = new CreateDisputeRequest { Reason = DisputeReason.PoorQuality, Description = "Làm chưa sạch sẽ", EvidenceUrls = new() { "/uploads/a.png" } };
        Assert.Empty(Validate(ok));

        var noPhoto = new CreateDisputeRequest { Reason = DisputeReason.PoorQuality, Description = "Làm chưa sạch sẽ", EvidenceUrls = new() };
        Assert.NotEmpty(Validate(noPhoto));

        var sixPhotos = new CreateDisputeRequest
        {
            Reason = DisputeReason.Other, Description = "Làm chưa sạch sẽ",
            EvidenceUrls = Enumerable.Range(1, 6).Select(i => $"/uploads/{i}.png").ToList(),
        };
        Assert.NotEmpty(Validate(sixPhotos));

        var shortText = new CreateDisputeRequest { Reason = DisputeReason.Other, Description = "ngắn", EvidenceUrls = new() { "/uploads/a.png" } };
        Assert.Contains(Validate(shortText), r => r.ErrorMessage!.Contains("ít nhất 10 ký tự"));
    }

    [Fact]
    public void ResolveDisputeRequest_BatBuocKetQuaVaGhiChu()
    {
        var errors = Validate(new ResolveDisputeRequest { AdminNote = "ngắn" }).Select(r => r.ErrorMessage).ToList();
        Assert.Contains("Vui lòng chọn kết quả xử lý.", errors);
        Assert.Contains("Vui lòng ghi lý do quyết định.", errors);
        Assert.Empty(Validate(new ResolveDisputeRequest { Resolution = DisputeResolution.ReleaseFull, AdminNote = "Đã xem đủ ảnh hai bên" }));
    }

    [Fact]
    public void ReportUnpaidRequest_BatBuocGhiChuVaAnh()
    {
        Assert.NotEmpty(Validate(new ReportUnpaidRequest { Note = "ngắn", PhotoUrl = "" }));
        Assert.Empty(Validate(new ReportUnpaidRequest { Note = "Khách đi vắng, không trả tiền", PhotoUrl = "/uploads/a.png" }));
    }

    [Theory]
    [InlineData("Abcdefg1", true)]
    [InlineData("12345678", false)]   // thiếu chữ
    [InlineData("abcdefgh", false)]   // thiếu số
    [InlineData("Abc12", false)]      // dưới 8 ký tự
    public void RegisterRequest_MatKhauTu8KyTuGomChuVaSo(string password, bool valid)
    {
        Assert.Equal(valid, ParamValid<RegisterRequest>("Password", password));
    }

    [Theory]
    [InlineData("a@b.vn", true)]
    [InlineData("khong-phai-email", false)]
    [InlineData("", false)]
    public void RegisterRequest_Email(string email, bool valid)
    {
        Assert.Equal(valid, ParamValid<RegisterRequest>("Email", email));
    }

    [Theory]
    [InlineData("A", false)]
    [InlineData("An", true)]
    public void RegisterRequest_HoTenTu2KyTu(string name, bool valid)
    {
        Assert.Equal(valid, ParamValid<RegisterRequest>("FullName", name));
    }

    [Theory]
    [InlineData("giờ", true)]
    [InlineData("lần", true)]
    [InlineData("m²", true)]
    [InlineData("buổi", true)]
    [InlineData("ngày", false)]
    public void UpsertServiceCategoryRequest_DonViTinh(string unit, bool valid)
    {
        Assert.Equal(valid, ParamValid<UpsertServiceCategoryRequest>("Unit", unit));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void CreateReviewRequest_SoSaoTu1Den5(int rating, bool valid)
    {
        Assert.Equal(valid, ParamValid<CreateReviewRequest>("Rating", rating));
    }

    [Theory]
    [InlineData("0912345678", true)]
    [InlineData("+84912345678", false)]
    public void UpdateProfileRequest_SoDienThoai(string phone, bool valid)
    {
        Assert.Equal(valid, ParamValid<UpdateProfileRequest>("PhoneNumber", phone));
    }
}
