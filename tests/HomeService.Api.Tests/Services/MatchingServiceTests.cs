using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>US16 / FR-12 – ghép nối Tasker theo đợt 3 → 5 → 8 km (MatchingService).</summary>
public class MatchingServiceTests : TestBase
{
    private const double FourKmNorth = Seed.BenThanhLat + 0.036;   // ≈ 4 km từ Bến Thành

    private Booking NewBooking(double hoursUntil = 3, PaymentMethod method = PaymentMethod.EWallet, decimal price = 150_000m) =>
        Db.Booking(Db.Customer(), Db.Service(price: price), BookingStatus.Pending, method: method,
            configure: b => b.ScheduledTime = DateTime.UtcNow.AddHours(hoursUntil));

    [Fact]
    public async Task Process_GuiDot1ChoTaskerGanVaDungKyNang()
    {
        var tasker = Db.Tasker();
        var booking = NewBooking();

        var processed = await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(1, processed);
        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Matched, b.Status);
        Assert.Equal(1, b.CurrentWave);
        Assert.InRange(b.NextMatchAt!.Value, DateTime.UtcNow.AddSeconds(40), DateTime.UtcNow.AddSeconds(46));
        var offer = await fresh.BookingOffers.SingleAsync();
        Assert.Equal(tasker.Id, offer.TaskerId);
        Assert.Equal(OfferStatus.Sent, offer.Status);
        Assert.Equal(1, offer.Wave);
        Assert.Equal(booking.MatchRound, offer.Round);
        Assert.Equal("Bạn có đơn mới", (await fresh.Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task Process_KhongDungKyNang_KhongMoi_ThuLaiSau30Phut()
    {
        Db.Tasker(skills: "Nấu ăn");
        NewBooking(hoursUntil: 5);

        await CreateMatching().ProcessDueBookingsAsync();

        var fresh = Fresh();
        Assert.Empty(await fresh.BookingOffers.ToListAsync());
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.Equal(0, b.CurrentWave);
        Assert.InRange(b.NextMatchAt!.Value, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public async Task Process_GanGioHenMaKhongCoAi_ChuyenChuaCoTaskerNhan()
    {
        NewBooking(hoursUntil: 0.5); // còn 30 phút < mốc dừng 45 phút (BR-M05)

        await CreateMatching().ProcessDueBookingsAsync();

        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.NoTaskerFound, b.Status);
        Assert.Null(b.NextMatchAt);
        Assert.Equal("Chưa có Tasker nhận đơn", (await fresh.Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task Process_TaskerCach4Km_DuocMoiODot2()
    {
        var tasker = Db.Tasker(lat: FourKmNorth);
        NewBooking();

        await CreateMatching().ProcessDueBookingsAsync();

        var fresh = Fresh();
        Assert.Equal(2, (await fresh.Bookings.SingleAsync()).CurrentWave);
        var offer = await fresh.BookingOffers.SingleAsync();
        Assert.Equal(tasker.Id, offer.TaskerId);
        Assert.Equal(2, offer.Wave);
        Assert.InRange(offer.DistanceKm, 3.8m, 4.2m);
    }

    [Fact]
    public async Task Process_NgoaiBanKinhPhucVuCuaTasker_KhongMoi()
    {
        Db.Tasker(lat: FourKmNorth, configure: p => p.ServiceRadiusKm = 3);
        NewBooking();
        await CreateMatching().ProcessDueBookingsAsync();
        Assert.Empty(await Fresh().BookingOffers.ToListAsync());
    }

    [Fact]
    public async Task Process_TaskerXaHon8Km_KhongMoi()
    {
        Db.Tasker(lat: Seed.ThuDucLat, lng: Seed.ThuDucLng);
        NewBooking();
        await CreateMatching().ProcessDueBookingsAsync();
        Assert.Empty(await Fresh().BookingOffers.ToListAsync());
    }

    [Fact]
    public async Task Process_LoaiTaskerKhongDuDieuKien()
    {
        Db.Tasker("Chưa duyệt", status: TaskerVerificationStatus.Pending);
        Db.Tasker("Tắt nhận đơn", configure: p => p.IsAvailable = false);
        Db.Tasker("Bị tạm khóa", configure: p => p.SuspendedUntil = DateTime.UtcNow.AddDays(1));
        var locked = Db.Tasker("Bị khóa tài khoản");
        locked.IsActive = false;
        Db.SaveChanges();
        NewBooking();

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Empty(await Fresh().BookingOffers.ToListAsync());
    }

    [Fact]
    public async Task Process_DonTienMat_ChiMoiTaskerDuSoDuVi()
    {
        Db.Tasker("Ví thấp", wallet: 10_000m);
        var rich = Db.Tasker("Ví đủ", wallet: 100_000m);
        NewBooking(method: PaymentMethod.Cash, price: 200_000m);   // chiết khấu ước tính 40.000

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(rich.Id, (await Fresh().BookingOffers.SingleAsync()).TaskerId);
    }

    [Fact]
    public async Task Process_DonOnline_BoQuaTaskerChuaDuKyQuy()
    {
        Db.Tasker("Chưa ký quỹ", wallet: 99_999m);
        var ok = Db.Tasker("Đủ ký quỹ", wallet: 100_000m);
        NewBooking();

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(ok.Id, (await Fresh().BookingOffers.SingleAsync()).TaskerId);
    }

    [Fact]
    public async Task Process_GioiHanSoLoiMoiMoiDot_UuTienDiemDanhGiaCao()
    {
        MatchingOptions.OffersPerWave = 2;
        var best = Db.Tasker("5 sao", configure: p => p.RatingAverage = 5m);
        var good = Db.Tasker("4 sao", configure: p => p.RatingAverage = 4m);
        Db.Tasker("1 sao", configure: p => p.RatingAverage = 1m);
        NewBooking();

        await CreateMatching().ProcessDueBookingsAsync();

        var invited = await Fresh().BookingOffers.Select(o => o.TaskerId).ToListAsync();
        Assert.Equal(2, invited.Count);
        Assert.Contains(best.Id, invited);
        Assert.Contains(good.Id, invited);
    }

    [Fact]
    public async Task Process_TaskerTrungLich_KhongMoi()
    {
        var busy = Db.Tasker();
        var booking = NewBooking();
        Db.Booking(Db.Customer("Khách khác"), Db.Service("Giặt ủi"), BookingStatus.Accepted, busy,
            configure: b => b.ScheduledTime = booking.ScheduledTime.AddHours(1));

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Empty(await Fresh().BookingOffers.ToListAsync());
    }

    [Fact]
    public async Task Process_DungViTriHienTaiNeuConMoi()
    {
        // Điểm làm việc ở Thủ Đức (xa) nhưng vừa bật nhận đơn tại Bến Thành
        var tasker = Db.Tasker(lat: Seed.ThuDucLat, lng: Seed.ThuDucLng, configure: p =>
        {
            p.LastLatitude = Seed.BenThanhLat;
            p.LastLongitude = Seed.BenThanhLng;
            p.LastLocationAt = DateTime.UtcNow.AddMinutes(-10);
        });
        NewBooking();

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(tasker.Id, (await Fresh().BookingOffers.SingleAsync()).TaskerId);
    }

    [Fact]
    public async Task Process_ViTriHienTaiQuaCu_DungDiemLamViec()
    {
        Db.Tasker(lat: Seed.ThuDucLat, lng: Seed.ThuDucLng, configure: p =>
        {
            p.LastLatitude = Seed.BenThanhLat;
            p.LastLongitude = Seed.BenThanhLng;
            p.LastLocationAt = DateTime.UtcNow.AddMinutes(-90);
        });
        NewBooking();

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Empty(await Fresh().BookingOffers.ToListAsync());
    }

    [Fact]
    public async Task Process_MoiNguoiTuChoi_SangDotKeNgay_KhongMoiLai()
    {
        var first = Db.Tasker("Đã từ chối");
        var second = Db.Tasker("Ở xa hơn", lat: FourKmNorth);
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched, configure: b =>
        {
            b.CurrentWave = 1;
            b.NextMatchAt = DateTime.UtcNow.AddSeconds(40);   // chưa hết giờ chờ
        });
        Db.Offer(booking, first, OfferStatus.Rejected);

        var processed = await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(1, processed);
        var fresh = Fresh();
        Assert.Equal(2, (await fresh.Bookings.SingleAsync()).CurrentWave);
        var sent = await fresh.BookingOffers.Where(o => o.Status == OfferStatus.Sent).ToListAsync();
        Assert.Equal(second.Id, Assert.Single(sent).TaskerId);
    }

    [Fact]
    public async Task Process_DotDangChoTraLoi_ChuaXuLy()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched, configure: b =>
        {
            b.CurrentWave = 1;
            b.NextMatchAt = DateTime.UtcNow.AddSeconds(40);
        });
        Db.Offer(booking, tasker);

        Assert.Equal(0, await CreateMatching().ProcessDueBookingsAsync());
    }

    [Fact]
    public async Task Process_HetGioChoDot_LoiMoiCuChuyenHetHan()
    {
        var tasker = Db.Tasker();
        Db.Tasker("Tasker 2", lat: FourKmNorth);
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched, configure: b =>
        {
            b.CurrentWave = 1;
            b.NextMatchAt = DateTime.UtcNow.AddSeconds(-1);
        });
        var old = Db.Offer(booking, tasker);

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(OfferStatus.Expired, (await Fresh().BookingOffers.SingleAsync(o => o.Id == old.Id)).Status);
    }

    [Fact]
    public async Task Process_TaskerCuKhongDen_KhongDuocMoiLaiOVongMoi()
    {
        var noShow = Db.Tasker("Không đến");
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Pending, configure: b => b.MatchRound = 2);
        Db.BookingOffers.Add(new BookingOffer
        {
            BookingId = booking.Id, TaskerId = noShow.Id, Round = 1, Wave = 1, Status = OfferStatus.Accepted,
            SentAt = DateTime.UtcNow.AddHours(-3), ExpiresAt = DateTime.UtcNow.AddHours(-3), RespondedAt = DateTime.UtcNow.AddHours(-3),
        });
        Db.SaveChanges();

        await CreateMatching().ProcessDueBookingsAsync();

        Assert.Equal(1, await Fresh().BookingOffers.CountAsync());
    }

    [Fact]
    public async Task Process_DonDaCoTaskerHoacDaHuy_BoQua()
    {
        Db.Tasker();
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, Db.Tasker("Đã nhận"));
        Db.Booking(Db.Customer(), Db.Service("Giặt"), BookingStatus.Cancelled);
        Assert.Equal(0, await CreateMatching().ProcessDueBookingsAsync());
    }
}
