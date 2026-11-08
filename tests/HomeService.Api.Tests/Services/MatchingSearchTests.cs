using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace HomeService.Api.Tests.Services;

/// <summary>BR-M05, BR-M07, BR-M08 – đơn đã quét hết mà chưa ai nhận: tiếp tục tìm, "Tìm lại ngay", Tasker bật nhận đơn.</summary>
public class MatchingSearchTests : TestBase
{
    private const double FourKmNorth = Seed.BenThanhLat + 0.036;   // ≈ 4 km từ Bến Thành

    private Booking NewBooking(double hoursUntil, Action<Booking>? configure = null) =>
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Pending,
            configure: b => { b.ScheduledTime = DateTime.UtcNow.AddHours(hoursUntil); configure?.Invoke(b); });

    private void MakeDue(int bookingId)
    {
        var b = Db.Bookings.Single(x => x.Id == bookingId);
        b.NextMatchAt = DateTime.UtcNow.AddSeconds(-1);
        b.CurrentWave = 0;
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task QuetHet_GioHenTrong2Gio_QuetLaiSau5Phut_BaoKhachMotLan()
    {
        var booking = NewBooking(hoursUntil: 1.5);

        await CreateMatching().ProcessDueBookingsAsync();
        var b = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.NotNull(b.SearchExhaustedAt);
        Assert.InRange(b.NextMatchAt!.Value, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));
        var note = await Fresh().Notifications.SingleAsync();
        Assert.Equal("Đang tiếp tục tìm Tasker", note.Title);
        Assert.Contains("Tìm lại ngay", note.Message);

        // Lượt quét lại vẫn không có ai → không báo thêm lần nữa
        MakeDue(booking.Id);
        await CreateMatching().ProcessDueBookingsAsync();
        Assert.Single(await Fresh().Notifications.ToListAsync());
        Assert.Equal(BookingStatus.Pending, (await Fresh().Bookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task QuetHet_GioHenConXa_QuetLaiSau30Phut()
    {
        NewBooking(hoursUntil: 6);
        await CreateMatching().ProcessDueBookingsAsync();
        var b = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.InRange(b.NextMatchAt!.Value, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public async Task QuetLai_KhongVuotMocDung45Phut()
    {
        NewBooking(hoursUntil: 0.8); // 48 phút → mốc dừng sau 3 phút
        await CreateMatching().ProcessDueBookingsAsync();
        var b = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.InRange(b.NextMatchAt!.Value, DateTime.UtcNow.AddMinutes(2), DateTime.UtcNow.AddMinutes(3.2));
    }

    [Fact]
    public async Task DenMocDung_ChuyenChuaCoTaskerNhan_BaoHoanTien()
    {
        var booking = NewBooking(hoursUntil: 0.7, b => b.SearchExhaustedAt = DateTime.UtcNow.AddMinutes(-20));
        MakeDue(booking.Id);

        await CreateMatching().ProcessDueBookingsAsync();

        var b = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.NoTaskerFound, b.Status);
        Assert.Null(b.NextMatchAt);
        var note = await Fresh().Notifications.SingleAsync();
        Assert.Equal("Chưa có Tasker nhận đơn", note.Title);
        Assert.Contains("hoàn 100%", note.Message);
    }

    [Fact]
    public async Task QuetLai_ChiMoiTaskerChuaDuocMoi()
    {
        var declined = Db.Tasker("Đã từ chối");
        var booking = NewBooking(hoursUntil: 1.5, b => b.SearchExhaustedAt = DateTime.UtcNow.AddMinutes(-5));
        Db.Offer(booking, declined, OfferStatus.Rejected);
        var newcomer = Db.Tasker("Mới bật nhận đơn");
        MakeDue(booking.Id);

        await CreateMatching().ProcessDueBookingsAsync();

        var sent = await Fresh().BookingOffers.Where(o => o.Status == OfferStatus.Sent).ToListAsync();
        Assert.Equal(newcomer.Id, Assert.Single(sent).TaskerId);
    }

    [Fact]
    public async Task TaskerBatNhanDon_DanhThucDonGanDangCho()
    {
        var tasker = Db.Tasker(configure: p => p.IsAvailable = false);
        var near = NewBooking(1.5, b => { b.SearchExhaustedAt = DateTime.UtcNow; b.NextMatchAt = DateTime.UtcNow.AddMinutes(4); });
        var far = Db.Booking(Db.Customer("far@test.vn"), Db.Service("Dọn dẹp xa"), BookingStatus.Pending, configure: b =>
        {
            b.ScheduledTime = DateTime.UtcNow.AddHours(1.5);
            b.Latitude = Seed.ThuDucLat; b.Longitude = Seed.ThuDucLng;
            b.SearchExhaustedAt = DateTime.UtcNow; b.NextMatchAt = DateTime.UtcNow.AddMinutes(4);
        });

        var ctrl = new TaskersController(Db, CreateNotifications(), MsOptions.Create(BusinessOptions)).AsUser(tasker.Id, UserRole.Tasker);
        await ctrl.UpdateAvailability(new UpdateAvailabilityRequest { IsAvailable = true }, MsOptions.Create(MatchingOptions), CreateMatching());

        var fresh = Fresh();
        Assert.True((await fresh.Bookings.SingleAsync(b => b.Id == near.Id)).NextMatchAt <= DateTime.UtcNow);
        Assert.True((await fresh.Bookings.SingleAsync(b => b.Id == far.Id)).NextMatchAt > DateTime.UtcNow);

        Db.ChangeTracker.Clear();
        await CreateMatching().ProcessDueBookingsAsync();
        var offer = await Fresh().BookingOffers.SingleAsync();
        Assert.Equal(near.Id, offer.BookingId);
        Assert.Equal(tasker.Id, offer.TaskerId);
    }

    [Fact]
    public async Task TaskerBatNhanDon_DaDuocMoiHoacKhacKyNang_KhongDanhThuc()
    {
        var tasker = Db.Tasker(lat: FourKmNorth);
        var invited = NewBooking(1.5, b => { b.SearchExhaustedAt = DateTime.UtcNow; b.NextMatchAt = DateTime.UtcNow.AddMinutes(4); });
        Db.Offer(invited, tasker, OfferStatus.Expired);
        var cooking = Db.Booking(Db.Customer("nau@test.vn"), Db.Service("Nấu ăn"), BookingStatus.Pending, configure: b =>
        {
            b.ScheduledTime = DateTime.UtcNow.AddHours(1.5);
            b.SearchExhaustedAt = DateTime.UtcNow; b.NextMatchAt = DateTime.UtcNow.AddMinutes(4);
        });

        Assert.Equal(0, await CreateMatching().WakeNearbyBookingsAsync(tasker.Id));
    }

    [Fact]
    public async Task TimLaiNgay_QuetNgay_VaPhaiCho5Phut()
    {
        var booking = NewBooking(1.5, b => { b.SearchExhaustedAt = DateTime.UtcNow.AddMinutes(-3); b.NextMatchAt = DateTime.UtcNow.AddMinutes(4); b.CurrentWave = 0; });

        await CreateBookingService().SearchNowAsync(booking.Id, booking.CustomerId);

        var b = await Fresh().Bookings.SingleAsync();
        Assert.True(b.NextMatchAt <= DateTime.UtcNow);
        Assert.InRange(b.ManualSearchAvailableAt!.Value, DateTime.UtcNow.AddMinutes(4.5), DateTime.UtcNow.AddMinutes(5.1));

        Db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().SearchNowAsync(booking.Id, booking.CustomerId));
        Assert.Equal("Bạn vừa tìm lại. Vui lòng thử lại sau 5 phút.", ex.Message);
    }

    [Theory]
    [InlineData("chua-quet-het", "Đơn không ở trạng thái chờ tìm lại Tasker.")]
    [InlineData("dang-gui-loi-moi", "Hệ thống đang gửi lời mời cho Tasker gần bạn, vui lòng chờ kết quả.")]
    [InlineData("sat-gio", "Đã gần giờ hẹn nên hệ thống đã dừng tìm Tasker cho đơn này.")]
    [InlineData("chua-co-tasker", "Đơn không ở trạng thái chờ tìm lại Tasker.")]
    public async Task TimLaiNgay_KhongHopLe_400(string kind, string message)
    {
        var booking = NewBooking(kind == "sat-gio" ? 0.5 : 1.5, b =>
        {
            b.SearchExhaustedAt = kind == "chua-quet-het" ? null : DateTime.UtcNow.AddMinutes(-3);
            if (kind == "dang-gui-loi-moi") b.Status = BookingStatus.Matched;
            if (kind == "chua-co-tasker") b.Status = BookingStatus.NoTaskerFound;
        });
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().SearchNowAsync(booking.Id, booking.CustomerId));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task TimLaiNgay_KhongPhaiDonCuaMinh_403()
    {
        var booking = NewBooking(1.5, b => b.SearchExhaustedAt = DateTime.UtcNow);
        var other = Db.Customer("khac@test.vn");
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().SearchNowAsync(booking.Id, other.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task ThuLai_VongMoi_XoaTrangThaiDaQuetHet()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.NoTaskerFound, configure: b =>
        {
            b.SearchExhaustedAt = DateTime.UtcNow.AddMinutes(-30);
            b.ManualSearchAvailableAt = DateTime.UtcNow.AddMinutes(2);
        });
        await CreateBookingService().RetryAsync(booking.Id, booking.CustomerId, new RetryBookingRequest { ScheduledTime = DateTime.UtcNow.AddHours(3) });
        var b = await Fresh().Bookings.SingleAsync();
        Assert.Null(b.SearchExhaustedAt);
        Assert.Null(b.ManualSearchAvailableAt);
    }

    [Fact]
    public void Mapper_TraTrangThaiDangTimTiep()
    {
        var scheduled = DateTime.UtcNow.AddHours(1.5);
        var b = new Booking
        {
            Status = BookingStatus.Pending, ScheduledTime = scheduled,
            SearchExhaustedAt = DateTime.UtcNow.AddMinutes(-2), NextMatchAt = DateTime.UtcNow.AddMinutes(3),
            ManualSearchAvailableAt = DateTime.UtcNow.AddMinutes(1),
        };
        var r = BookingMapper.ToResponse(b, DateTime.UtcNow);
        Assert.Equal(b.NextMatchAt, r.NextSearchAt);
        Assert.Equal(scheduled.AddMinutes(-45), r.SearchStopsAt);
        Assert.Equal(b.ManualSearchAvailableAt, r.SearchNowAvailableAt);

        b.Status = BookingStatus.Accepted; b.TaskerId = 9;
        Assert.Null(BookingMapper.ToResponse(b, DateTime.UtcNow).SearchExhaustedAt);
    }
}
