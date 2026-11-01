using HomeService.Api.Common;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>US17 – Tasker nhận / từ chối lời mời (OfferService).</summary>
public class OfferServiceTests : TestBase
{
    private (Booking Booking, User Tasker) MatchedWithOffer(PaymentMethod method = PaymentMethod.EWallet, decimal wallet = 500_000m,
        OfferStatus offerStatus = OfferStatus.Sent, DateTime? expiresAt = null)
    {
        var tasker = Db.Tasker(wallet: wallet);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 200_000m), BookingStatus.Matched, method: method,
            configure: b => b.CurrentWave = 1);
        Db.Offer(booking, tasker, offerStatus, expiresAt: expiresAt);
        return (booking, tasker);
    }

    [Fact]
    public async Task Accept_NguoiDauTienThang_DongLoiMoiKhac()
    {
        var (booking, tasker) = MatchedWithOffer();
        var other = Db.Tasker("Tasker B");
        Db.Offer(booking, other);

        await CreateOfferService().AcceptAsync(booking.Id, tasker.Id);

        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Accepted, b.Status);
        Assert.Equal(tasker.Id, b.TaskerId);
        Assert.Null(b.NextMatchAt);
        Assert.Equal(OfferStatus.Accepted, (await fresh.BookingOffers.SingleAsync(o => o.TaskerId == tasker.Id)).Status);
        Assert.Equal(OfferStatus.Closed, (await fresh.BookingOffers.SingleAsync(o => o.TaskerId == other.Id)).Status);
        Assert.Equal("Đã có Tasker nhận đơn", (await fresh.Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task Accept_TaskerThuHai_BaoDonDaCoNguoiNhan()
    {
        var (booking, tasker) = MatchedWithOffer();
        var other = Db.Tasker("Tasker B");
        Db.Offer(booking, other);

        await CreateOfferService().AcceptAsync(booking.Id, tasker.Id);
        Db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, other.Id));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("Đơn đã được Tasker khác nhận.", ex.Message);
    }

    [Fact]
    public async Task Accept_DonTienMat_TamGiuChietKhau()
    {
        var (booking, tasker) = MatchedWithOffer(PaymentMethod.Cash);
        await CreateOfferService().AcceptAsync(booking.Id, tasker.Id);
        Assert.Equal(40_000m, (await Fresh().TaskerProfiles.SingleAsync()).ReservedAmount);
    }

    [Theory]
    [InlineData(PaymentMethod.EWallet)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Cash)]
    public async Task Accept_ViDuoiMucKyQuy_BiTuChoi_MoiPhuongThuc(PaymentMethod method)
    {
        var (booking, tasker) = MatchedWithOffer(method, wallet: 99_999m);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Contains("tối thiểu 100.000 đ khả dụng (ký quỹ)", ex.Message);
        Assert.Null((await Fresh().Bookings.SingleAsync()).TaskerId);
    }

    [Fact]
    public async Task Accept_DonTienMat_DuKyQuyNhungKhongDuChietKhau_BiTuChoi()
    {
        var tasker = Db.Tasker(wallet: 150_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 1_000_000m), BookingStatus.Matched, method: PaymentMethod.Cash,
            configure: b => b.CurrentWave = 1);   // chiết khấu 200.000
        Db.Offer(booking, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Contains("Số dư ví không đủ", ex.Message);
    }

    [Fact]
    public async Task Accept_HoSoChuaDuyet_BiTuChoi()
    {
        var tasker = Db.Tasker(status: TaskerVerificationStatus.Pending);
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched);
        Db.Offer(booking, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Equal("Hồ sơ Tasker chưa được duyệt.", ex.Message);
    }

    [Fact]
    public async Task Accept_DangBiTamKhoa_BiTuChoi()
    {
        var tasker = Db.Tasker(configure: p => p.SuspendedUntil = DateTime.UtcNow.AddDays(3));
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched);
        Db.Offer(booking, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Contains("tạm khóa", ex.Message);
    }

    [Fact]
    public async Task Accept_KhongCoLoiMoi_403()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Accept_LoiMoiHetHan_409()
    {
        var (booking, tasker) = MatchedWithOffer(expiresAt: DateTime.UtcNow.AddSeconds(-1));
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Equal("Lời mời đã hết hạn.", ex.Message);
    }

    [Theory]
    [InlineData(OfferStatus.Closed, "Đơn đã được Tasker khác nhận.")]
    [InlineData(OfferStatus.Accepted, "Bạn đã nhận đơn này.")]
    [InlineData(OfferStatus.Rejected, "Bạn đã từ chối lời mời này.")]
    [InlineData(OfferStatus.Expired, "Lời mời đã hết hạn.")]
    public async Task Accept_LoiMoiKhongConMo_409(OfferStatus status, string message)
    {
        var (booking, tasker) = MatchedWithOffer(offerStatus: status);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Accept_TrungLichDuoi2Gio_409()
    {
        var (booking, tasker) = MatchedWithOffer();
        Db.Booking(Db.Customer(), Db.Service("Giặt ủi"), BookingStatus.Accepted, tasker,
            configure: b => b.ScheduledTime = booking.ScheduledTime.AddMinutes(90));
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Contains("trùng lịch", ex.Message);
    }

    [Fact]
    public async Task Accept_DonDaHuy_409()
    {
        var (booking, tasker) = MatchedWithOffer();
        booking.Status = BookingStatus.Cancelled;
        Db.SaveChanges();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(booking.Id, tasker.Id));
        Assert.Equal("Đơn không còn nhận Tasker.", ex.Message);
    }

    [Fact]
    public async Task Accept_DonKhongTonTai_404()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(999, Db.Tasker().Id));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Reject_ChiDongLoiMoiCuaMinh_DonVanCho()
    {
        var (booking, tasker) = MatchedWithOffer();
        await CreateOfferService().RejectAsync(booking.Id, tasker.Id);

        var fresh = Fresh();
        Assert.Equal(OfferStatus.Rejected, (await fresh.BookingOffers.SingleAsync()).Status);
        Assert.Equal(BookingStatus.Matched, (await fresh.Bookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reject_HaiLan_LanSauBao409()
    {
        var (booking, tasker) = MatchedWithOffer();
        await CreateOfferService().RejectAsync(booking.Id, tasker.Id);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().RejectAsync(booking.Id, tasker.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Reject_DonDaCoNguoiNhan_409()
    {
        var (booking, tasker) = MatchedWithOffer();
        booking.Status = BookingStatus.Accepted;
        Db.SaveChanges();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().RejectAsync(booking.Id, tasker.Id));
        Assert.Equal("Đơn không còn nhận Tasker.", ex.Message);
    }
}
