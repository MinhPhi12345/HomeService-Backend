using HomeService.Api.Controllers;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HomeService.Api.Tests.Controllers;

/// <summary>US26 – PublicProfilesController: hồ sơ của bên còn lại trong đơn kèm đánh giá đã nhận.</summary>
public class PublicProfilesControllerTests : TestBase
{
    private PublicProfilesController As(User u) => new PublicProfilesController(Db).AsUser(u.Id, u.Role);

    [Fact]
    public async Task KhachXemHoSoTaskerCuaDon_HoTenDayDu_DanhGiaChiHienTenGoi()
    {
        var customer = Db.Customer("Nguyễn Minh Anh");
        var tasker = Db.Tasker("Trần Thị Lan");
        var other = Db.Customer("Phạm Thu Trang");
        var done = Db.Booking(other, Db.Service(), BookingStatus.Paid, tasker);
        Db.Review(done, other, tasker, 4, comment: "Làm kỹ, đúng giờ.");
        var flagged = Db.Booking(other, Db.Service("Giặt"), BookingStatus.Paid, tasker);
        Db.Review(flagged, other, tasker, 1, flagged: true);
        Db.Booking(customer, Db.Service("Nấu"), BookingStatus.Accepted, tasker);

        var p = (await As(customer).Get(tasker.Id)).OkValue();

        Assert.Equal("Trần Thị Lan", p.DisplayName);
        Assert.Equal(UserRole.Tasker, p.Role);
        Assert.Equal(2, p.CompletedBookings);
        Assert.Equal(1, p.ReviewCount);            // đánh giá đang bị báo cáo không tính
        Assert.Equal(4.00m, p.RatingAverage);
        Assert.True(p.IsVerified);
        Assert.Equal("Quận 1", p.WorkingArea);
        var r = Assert.Single(p.Reviews);
        Assert.Equal("Trang", r.ReviewerName);
        Assert.Null(r.ReportReason);
    }

    [Fact]
    public async Task TaskerDuocMoi_ThayTenGoiCuaKhach_NguoiNgoai403_Admin404()
    {
        var customer = Db.Customer("Nguyễn Minh Anh");
        var tasker = Db.Tasker();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Matched);
        Db.Offer(booking, tasker);

        var p = (await As(tasker).Get(customer.Id)).OkValue();
        Assert.Equal("Anh", p.DisplayName);
        Assert.Equal(UserRole.Customer, p.Role);
        Assert.Null(p.IsVerified);
        Assert.Null(p.Skills);

        Assert.IsType<ForbidResult>((await As(Db.Tasker("Người lạ")).Get(customer.Id)).Result);
        Assert.IsType<NotFoundResult>((await As(customer).Get(Db.Admin().Id)).Result);
        Assert.IsType<NotFoundResult>((await As(customer).Get(999)).Result);
        Assert.Equal("Nguyễn Minh Anh", (await new PublicProfilesController(Db).AsUser(1, UserRole.Admin).Get(customer.Id)).OkValue().DisplayName);
    }
}
