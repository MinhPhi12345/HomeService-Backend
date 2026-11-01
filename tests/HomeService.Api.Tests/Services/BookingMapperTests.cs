using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>Ánh xạ Booking → BookingResponse, ẩn/hiện số điện thoại theo người xem (BookingMapper).</summary>
public class BookingMapperTests
{
    private static Booking Sample(BookingStatus status) => new()
    {
        Id = 10,
        CustomerId = 1,
        Customer = new User { Id = 1, FullName = "Khách", PhoneNumber = "0901111111" },
        TaskerId = 2,
        Tasker = new User { Id = 2, FullName = "Tasker", PhoneNumber = "0902222222" },
        ServiceCategory = new ServiceCategory { Name = "Dọn dẹp nhà" },
        ServiceCategoryId = 3,
        Status = status,
        TotalPrice = 150_000m,
        Quantity = 1,
        Address = "1 Lê Lợi",
    };

    [Fact]
    public void ToResponse_AnhXaTruongCoBan()
    {
        var r = BookingMapper.ToResponse(Sample(BookingStatus.Pending), DateTime.UtcNow);
        Assert.Equal(10, r.Id);
        Assert.Equal("Dọn dẹp nhà", r.ServiceCategoryName);
        Assert.Equal("Khách", r.CustomerName);
        Assert.Equal("Tasker", r.TaskerName);
        Assert.Equal(150_000m, r.TotalPrice);
        Assert.Null(r.DisputeId);
        Assert.Null(r.PaymentStatus);
    }

    [Fact]
    public void ToResponse_DonDangLam_HaiBenThaySoDienThoaiNhau()
    {
        var b = Sample(BookingStatus.Accepted);
        var asTasker = BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 2);
        Assert.Equal("0901111111", asTasker.CustomerPhone);
        Assert.Equal("0902222222", asTasker.TaskerPhone);
        Assert.Equal("0901111111", asTasker.ContactPhone);
    }

    [Fact]
    public void ToResponse_UuTienSdtLienHeCuaDon()
    {
        var b = Sample(BookingStatus.InProgress);
        b.ContactPhone = "0912345678";
        Assert.Equal("0912345678", BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 2).CustomerPhone);
    }

    [Fact]
    public void ToResponse_DonChuaCoNguoiNhanHoacDaXong_AnSoDienThoai()
    {
        var asTasker = BookingMapper.ToResponse(Sample(BookingStatus.Paid), DateTime.UtcNow, viewerId: 2);
        Assert.Null(asTasker.CustomerPhone);
        Assert.Null(asTasker.TaskerPhone);
        Assert.Null(asTasker.ContactPhone);

        var asCustomer = BookingMapper.ToResponse(Sample(BookingStatus.Paid), DateTime.UtcNow, viewerId: 1);
        Assert.Equal("0901111111", asCustomer.ContactPhone);   // khách luôn thấy SĐT liên hệ của đơn mình
        Assert.Null(asCustomer.TaskerPhone);
    }

    [Fact]
    public void ToResponse_NguoiNgoai_KhongThaySoDienThoai()
    {
        var r = BookingMapper.ToResponse(Sample(BookingStatus.Accepted), DateTime.UtcNow, viewerId: 99);
        Assert.Null(r.CustomerPhone);
        Assert.Null(r.ContactPhone);
    }

    [Fact]
    public void ToResponse_CanReview_KhiDonXongVaChuaDanhGia()
    {
        var b = Sample(BookingStatus.Paid);
        Assert.True(BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 1).CanReview);
        b.Reviews.Add(new Review { ReviewerId = 1 });
        Assert.False(BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 1).CanReview);
        Assert.True(BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 2).CanReview);
        Assert.False(BookingMapper.ToResponse(Sample(BookingStatus.InProgress), DateTime.UtcNow, viewerId: 1).CanReview);
        Assert.False(BookingMapper.ToResponse(Sample(BookingStatus.Paid), DateTime.UtcNow, viewerId: 99).CanReview);
    }

    [Fact]
    public void ToResponse_CanDisputeVaDisputeId()
    {
        var b = Sample(BookingStatus.Completed);
        b.CompletedAt = DateTime.UtcNow.AddHours(-1);
        b.Payment = new Payment { Status = PaymentStatus.Held };
        var r = BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 1);
        Assert.True(r.CanDispute);
        Assert.Equal(PaymentStatus.Held, r.PaymentStatus);

        b.Dispute = new Dispute { Id = 5 };
        var r2 = BookingMapper.ToResponse(b, DateTime.UtcNow, viewerId: 1);
        Assert.False(r2.CanDispute);
        Assert.Equal(5, r2.DisputeId);
    }
}
