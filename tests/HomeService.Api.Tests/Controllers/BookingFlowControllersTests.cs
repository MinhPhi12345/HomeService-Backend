using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace HomeService.Api.Tests.Controllers;

/// <summary>EP03 / EP04 – BookingsController.</summary>
public class BookingsControllerTests : TestBase
{
    private BookingsController As(User u) =>
        new BookingsController(Db, CreateBookingService(), CreateOfferService(), CreateCancellation(), MsOptions.Create(SettlementOptions))
            .AsUser(u.Id, u.Role);

    [Fact]
    public async Task Create_201_TraBookingResponse()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 100_000m);
        var result = await As(customer).Create(new CreateBookingRequest
        {
            ServiceCategoryId = service.Id, ScheduledTime = DateTime.UtcNow.AddHours(4), Address = "1 Lê Lợi, Quận 1",
            Latitude = Seed.BenThanhLat, Longitude = Seed.BenThanhLng, Quantity = 3,
        });
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var body = Assert.IsType<BookingResponse>(created.Value);
        Assert.Equal(300_000m, body.TotalPrice);
        Assert.Equal(BookingStatus.AwaitingPayment, body.Status);
        Assert.Equal(service.Name, body.ServiceCategoryName);
    }

    [Fact]
    public async Task GetMine_KhachChiThayDonCuaMinh_TaskerChiThayDonDuocGiao()
    {
        var c1 = Db.Customer();
        var c2 = Db.Customer("Khách 2");
        var tasker = Db.Tasker();
        var service = Db.Service();
        Db.Booking(c1, service, BookingStatus.Accepted, tasker);
        Db.Booking(c1, service, BookingStatus.Pending);
        Db.Booking(c2, service, BookingStatus.Pending);

        Assert.Equal(2, (await As(c1).GetMine(null)).OkValue().Count());
        var taskerList = (await As(tasker).GetMine(null)).OkValue().ToList();
        Assert.Equal(c1.Id, Assert.Single(taskerList).CustomerId);
    }

    [Fact]
    public async Task GetMine_LocTrangThai_TuKhoa_VaSapToi()
    {
        var c = Db.Customer();
        var tasker = Db.Tasker();
        Db.Booking(c, Db.Service("Giặt ủi"), BookingStatus.Paid, tasker);
        var later = Db.Booking(c, Db.Service("Dọn dẹp nhà"), BookingStatus.Accepted, tasker, configure: b => b.ScheduledTime = DateTime.UtcNow.AddHours(10));
        var sooner = Db.Booking(c, Db.Service("Sửa điện"), BookingStatus.InProgress, tasker, configure: b => b.ScheduledTime = DateTime.UtcNow.AddHours(1));

        Assert.Equal(2, (await As(c).GetMine("2, 3")).OkValue().Count());
        Assert.Equal("Giặt ủi", Assert.Single((await As(c).GetMine(null, keyword: "giặt")).OkValue()).ServiceCategoryName);
        var upcoming = (await As(c).GetMine(null, upcoming: true)).OkValue().Select(b => b.Id).ToList();
        Assert.Equal(new List<int> { sooner.Id, later.Id }, upcoming);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("99")]
    public async Task GetMine_TrangThaiLocKhongHopLe_400(string status)
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(Db.Customer()).GetMine(status));
        Assert.Equal("Trạng thái lọc không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task GetById_ChuDonTaskerAdminXemDuoc_NguoiKhac403()
    {
        var c = Db.Customer();
        var tasker = Db.Tasker();
        var b = Db.Booking(c, Db.Service(), BookingStatus.Accepted, tasker);

        Assert.Equal(b.Id, (await As(c).GetById(b.Id)).OkValue().Id);
        Assert.Equal(b.Id, (await As(tasker).GetById(b.Id)).OkValue().Id);
        Assert.Equal(b.Id, (await As(Db.Admin()).GetById(b.Id)).OkValue().Id);
        Assert.IsType<ForbidResult>((await As(Db.Customer("Lạ")).GetById(b.Id)).Result);
        Assert.IsType<NotFoundResult>((await As(c).GetById(999)).Result);
    }

    [Fact]
    public async Task Cancel_DonHopLe_204_DonDangLam_400()
    {
        var c = Db.Customer();
        var pending = Db.Booking(c, Db.Service(), BookingStatus.Pending, method: PaymentMethod.Cash);
        var running = Db.Booking(c, Db.Service("Giặt"), BookingStatus.InProgress, Db.Tasker());

        Assert.IsType<NoContentResult>(await As(c).Cancel(pending.Id));
        Assert.Equal(BookingStatus.Cancelled, (await Fresh().Bookings.SingleAsync(b => b.Id == pending.Id)).Status);
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(c).Cancel(running.Id));
        Assert.Equal("Không thể hủy đơn ở trạng thái hiện tại.", ex.Message);
    }

    [Fact]
    public async Task Cancel_DonNguoiKhac_403_KhongTonTai_404()
    {
        var b = Db.Booking(Db.Customer(), Db.Service());
        Assert.IsType<ForbidResult>(await As(Db.Customer("Lạ")).Cancel(b.Id));
        Assert.IsType<NotFoundResult>(await As(Db.Customer("Lạ 2")).Cancel(999));
    }

    [Fact]
    public async Task CancellationFee_XemTruocPhiVaTienHoan()
    {
        var c = Db.Customer();
        var b = Db.Booking(c, Db.Service(price: 200_000m), BookingStatus.Accepted, Db.Tasker(),
            configure: x => x.ScheduledTime = DateTime.UtcNow.AddHours(1));
        Db.HeldPayment(b);

        var fee = (await As(c).GetCancellationFee(b.Id)).OkValue();

        Assert.Equal(40_000m, fee.CancellationFee);
        Assert.Equal(160_000m, fee.RefundAmount);
        Assert.Contains("20%", fee.Reason);
    }

    [Fact]
    public async Task UpdateLocation_TaskerCuaDonDangLam_LuuViTri()
    {
        var tasker = Db.Tasker();
        var b = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, tasker);

        Assert.IsType<NoContentResult>(await As(tasker).UpdateLocation(b.Id, new LocationRequest { Latitude = 10.78, Longitude = 106.69 }));
        var p = await Fresh().TaskerProfiles.SingleAsync();
        Assert.Equal(10.78, p.LastLatitude);
        Assert.NotNull(p.LastLocationAt);
    }

    [Fact]
    public async Task UpdateLocation_SaiNguoiHoacSaiTrangThai()
    {
        var tasker = Db.Tasker();
        var b = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Paid, tasker);
        Assert.IsType<ForbidResult>(await As(Db.Tasker("Khác")).UpdateLocation(b.Id, new LocationRequest { Latitude = 1, Longitude = 1 }));
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(tasker).UpdateLocation(b.Id, new LocationRequest { Latitude = 1, Longitude = 1 }));
        Assert.Equal("Chỉ gửi vị trí khi đơn đang được thực hiện.", ex.Message);
    }

    [Fact]
    public async Task GetLocation_KhachXemViTriVaKhoangCach()
    {
        var c = Db.Customer();
        var tasker = Db.Tasker(configure: p =>
        {
            p.LastLatitude = Seed.Q3Lat;
            p.LastLongitude = Seed.Q3Lng;
            p.LastLocationAt = DateTime.UtcNow;
        });
        var b = Db.Booking(c, Db.Service(), BookingStatus.Accepted, tasker);

        var loc = (await As(c).GetLocation(b.Id)).OkValue();

        Assert.Equal(Seed.Q3Lat, loc.Latitude);
        Assert.NotNull(loc.DistanceKm);
        Assert.InRange(loc.DistanceKm!.Value, 1.8, 2.2);
    }

    [Fact]
    public async Task GetLocation_TaskerChuaGuiViTri_404_DonKhac_403()
    {
        var c = Db.Customer();
        var b = Db.Booking(c, Db.Service(), BookingStatus.Accepted, Db.Tasker());
        Assert.IsType<NotFoundResult>((await As(c).GetLocation(b.Id)).Result);
        Assert.IsType<ForbidResult>((await As(Db.Customer("Lạ")).GetLocation(b.Id)).Result);
    }

    [Fact]
    public async Task Accept_Reject_Confirm_Retry_UpdateStatus_ReportUnpaid_TraVe204()
    {
        // Đi hết vòng đời 1 đơn qua controller: nhận → bắt đầu → hoàn thành → xác nhận
        var customer = Db.Customer();
        var tasker = Db.Tasker(wallet: 100_000m);   // đủ ký quỹ tối thiểu
        var b = Db.Booking(customer, Db.Service(price: 100_000m), BookingStatus.Matched);
        Db.HeldPayment(b);
        Db.Offer(b, tasker);

        Assert.IsType<NoContentResult>(await As(tasker).Accept(b.Id));
        Db.ChangeTracker.Clear();
        Assert.IsType<NoContentResult>(await As(tasker).UpdateStatus(b.Id, new UpdateBookingStatusRequest { Status = BookingStatus.InProgress, PhotoUrl = "/uploads/a.png" }));
        Assert.IsType<NoContentResult>(await As(tasker).UpdateStatus(b.Id, new UpdateBookingStatusRequest { Status = BookingStatus.Completed, PhotoUrl = "/uploads/b.png" }));
        Assert.IsType<NoContentResult>(await As(customer).Confirm(b.Id));

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
        Assert.Equal(180_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);

        // Từ chối lời mời & thử lại đơn chưa có Tasker
        var b2 = Db.Booking(customer, Db.Service("Giặt"), BookingStatus.Matched);
        Db.Offer(b2, tasker);
        Assert.IsType<NoContentResult>(await As(tasker).Reject(b2.Id));
        var b3 = Db.Booking(customer, Db.Service("Nấu ăn"), BookingStatus.NoTaskerFound);
        Assert.IsType<NoContentResult>(await As(customer).Retry(b3.Id));

        // Báo khách chưa thanh toán (tiền mặt)
        var b4 = Db.Booking(customer, Db.Service("Sửa điện"), BookingStatus.InProgress, tasker, PaymentMethod.Cash);
        Assert.IsType<NoContentResult>(await As(tasker).ReportUnpaid(b4.Id, new ReportUnpaidRequest { Note = "Khách đi vắng, không trả", PhotoUrl = "/uploads/c.png" }));
    }

    [Fact]
    public async Task CreateDispute_201_TraDisputeResponse()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var b = Db.Booking(customer, Db.Service(), BookingStatus.Completed, tasker, configure: x => x.CompletedAt = DateTime.UtcNow.AddHours(-1));
        Db.HeldPayment(b);

        var result = await As(customer).CreateDispute(b.Id, new CreateDisputeRequest
        {
            Reason = DisputeReason.Incomplete, Description = "Chưa làm xong bếp", EvidenceUrls = new() { "/uploads/e.png" },
        }, CreateDisputeService());

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
        var body = Assert.IsType<DisputeResponse>(obj.Value);
        Assert.Equal(DisputeReason.Incomplete, body.Reason);
        Assert.Equal(tasker.FullName, body.TaskerName);
    }
}

/// <summary>US17 – OffersController; US24 – PaymentsController; US35 – DisputesController.</summary>
public class OffersPaymentsDisputesControllersTests : TestBase
{
    [Fact]
    public async Task Offers_ChiLoiMoiConHan_KemDiemKhachHang()
    {
        var tasker = Db.Tasker();
        var customer = Db.Customer();
        var b1 = Db.Booking(customer, Db.Service(price: 150_000m), BookingStatus.Matched, method: PaymentMethod.Cash);
        var b2 = Db.Booking(customer, Db.Service("Giặt"), BookingStatus.Matched);
        Db.Offer(b1, tasker);
        Db.Offer(b2, tasker, expiresAt: DateTime.UtcNow.AddSeconds(-5));
        var old = Db.Booking(customer, Db.Service("Nấu"), BookingStatus.Paid, Db.Tasker("Khác"));
        Db.Review(old, Db.Users.Single(u => u.Id == old.TaskerId), customer, 4);

        var list = (await new OffersController(Db).AsUser(tasker.Id, UserRole.Tasker).GetMine()).OkValue().ToList();

        var offer = Assert.Single(list);
        Assert.Equal(b1.Id, offer.BookingId);
        Assert.Equal(30_000m, offer.EstimatedCommission);
        Assert.Equal(4.00m, offer.CustomerRatingAverage);
        Assert.Equal(customer.Id, offer.CustomerId);
        Assert.Equal(1, offer.CustomerReviewCount);
        Assert.Equal(2, (await new OffersController(Db).AsUser(tasker.Id, UserRole.Tasker).GetMine(all: true)).OkValue().Count());
    }

    [Fact]
    public async Task Payments_GetMine_KhachVaTaskerThayDungGiaoDich()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var b = Db.Booking(customer, Db.Service(), BookingStatus.Pending, tasker);
        Db.HeldPayment(b);
        Db.HeldPayment(Db.Booking(Db.Customer("Khác"), Db.Service("Giặt")));

        var mine = (await new PaymentsController(CreatePaymentService(), Db).AsUser(customer.Id, UserRole.Customer).GetMine(null, null)).OkValue();
        Assert.Equal(1, mine.TotalCount);
        Assert.Equal(b.Id, mine.Items[0].BookingId);
        var forTasker = (await new PaymentsController(CreatePaymentService(), Db).AsUser(tasker.Id, UserRole.Tasker).GetMine(1, 10)).OkValue();
        Assert.Equal(1, forTasker.TotalCount);
    }

    [Fact]
    public async Task Payments_Create_201()
    {
        var customer = Db.Customer();
        var b = Db.Booking(customer, Db.Service(), BookingStatus.AwaitingPayment, configure: x => x.PaymentDueAt = DateTime.UtcNow.AddMinutes(10));
        var result = await new PaymentsController(CreatePaymentService(), Db).AsUser(customer.Id, UserRole.Customer)
            .Create(new CreatePaymentRequest { BookingId = b.Id, Method = PaymentMethod.EWallet });
        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
        Assert.Equal(PaymentStatus.Held, Assert.IsType<PaymentResponse>(obj.Value).Status);
    }

    [Fact]
    public async Task Disputes_AdminLocTheoTrangThai_VaPhanQuyenXemChiTiet()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var b1 = Db.Booking(customer, Db.Service(), BookingStatus.Disputed, tasker);
        var b2 = Db.Booking(customer, Db.Service("Giặt"), BookingStatus.Paid, tasker);
        var open = Db.Dispute(b1);
        Db.Dispute(b2, d => d.Status = DisputeStatus.Resolved);
        var controller = new DisputesController(CreateDisputeService());

        Assert.Equal(2, (await controller.AsUser(1, UserRole.Admin).GetAll(null)).OkValue().Count());
        Assert.Equal(open.Id, Assert.Single((await controller.AsUser(1, UserRole.Admin).GetAll(DisputeStatus.Open)).OkValue()).Id);

        Assert.Equal(open.Id, (await controller.AsUser(customer.Id, UserRole.Customer).GetById(open.Id)).OkValue().Id);
        Assert.Equal(open.Id, (await controller.AsUser(tasker.Id, UserRole.Tasker).GetById(open.Id)).OkValue().Id);
        Assert.IsType<ForbidResult>((await controller.AsUser(Db.Customer("Lạ").Id, UserRole.Customer).GetById(open.Id)).Result);
        Assert.IsType<NotFoundResult>((await controller.AsUser(1, UserRole.Admin).GetById(999)).Result);
    }

    [Fact]
    public async Task Disputes_RespondVaResolve_204()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker(wallet: 0m);
        var b = Db.Booking(customer, Db.Service(), BookingStatus.Disputed, tasker);
        Db.HeldPayment(b);
        var d = Db.Dispute(b);
        var admin = Db.Admin();

        Assert.IsType<NoContentResult>(await new DisputesController(CreateDisputeService()).AsUser(tasker.Id, UserRole.Tasker)
            .Respond(d.Id, new TaskerDisputeResponseRequest { Response = "Tôi đã làm đầy đủ" }));
        Assert.IsType<NoContentResult>(await new DisputesController(CreateDisputeService()).AsUser(admin.Id, UserRole.Admin)
            .Resolve(d.Id, new ResolveDisputeRequest { Resolution = DisputeResolution.ReleaseFull, AdminNote = "Ảnh cho thấy đã làm xong" }));
        Assert.Equal(DisputeStatus.Resolved, (await Fresh().Disputes.SingleAsync()).Status);
    }
}
