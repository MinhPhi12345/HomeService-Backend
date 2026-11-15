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

    [Fact]
    public async Task HoSoChiKemBaDanhGiaMoiNhat_SoLuongTheoSao_XemDuCoLocSaoVaPhanTrang()
    {
        var customer = Db.Customer("Nguyễn Minh Anh");
        var tasker = Db.Tasker("Ngô Thị Mai");
        var start = DateTime.UtcNow.AddDays(-30);
        var ratings = new[] { 5, 5, 4, 5, 3, 5, 4, 5, 5, 1, 5, 4 }; // 7×5★, 3×4★, 1×3★, 1×1★
        for (var i = 0; i < ratings.Length; i++)
        {
            var b = Db.Booking(customer, Db.Service(), BookingStatus.Paid, tasker);
            var r = Db.Review(b, customer, tasker, ratings[i], comment: $"Nhận xét {i}");
            r.CreatedAt = start.AddDays(i);
        }
        Db.SaveChanges();

        var p = (await As(customer).Get(tasker.Id)).OkValue();
        Assert.Equal(12, p.ReviewCount);
        Assert.Equal(3, p.Reviews.Count);
        Assert.Equal("Nhận xét 11", p.Reviews[0].Comment); // mới nhất trước
        Assert.Equal(new[] { 7, 3, 1, 0, 1 }, p.RatingBreakdown!.Select(x => x.Count));
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, p.RatingBreakdown!.Select(x => x.Stars));

        var page1 = (await As(customer).GetReviews(tasker.Id, null, 1, 5)).OkValue();
        var page3 = (await As(customer).GetReviews(tasker.Id, null, 3, 5)).OkValue();
        Assert.Equal((12, 5, 2), (page1.TotalCount, page1.Items.Count, page3.Items.Count));

        var four = (await As(customer).GetReviews(tasker.Id, 4, null, null)).OkValue();
        Assert.Equal(3, four.TotalCount);
        Assert.All(four.Items, x => Assert.Equal(4, x.Rating));
        Assert.All(four.Items, x => Assert.Equal("Anh", x.ReviewerName));

        Assert.IsType<ForbidResult>((await As(Db.Customer("Người lạ")).GetReviews(tasker.Id, null, null, null)).Result);
    }
}
