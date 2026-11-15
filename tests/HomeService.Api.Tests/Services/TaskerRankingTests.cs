using HomeService.Api.Controllers;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>EP09 – Tasker xem hạng tạm tính tháng này; khách thấy nhãn "Đang trong top N" ở hồ sơ công khai.</summary>
public class TaskerRankingTests : TestBase
{
    private readonly (int Year, int Month) _now = TaskerAwardService.VnMonthOf(DateTime.UtcNow);
    private readonly (int Year, int Month) _prev = TaskerAwardService.PreviousMonth(DateTime.UtcNow);

    /// <summary>Tasker có `completed` đơn Paid trong tháng year/month, mỗi đơn 1 lời mời đã nhận; `reviews` đơn đầu có đánh giá `rating` sao.</summary>
    private User Tasker(string name, int completed, int rating = 5, int reviews = -1, (int Year, int Month)? month = null)
    {
        var (y, m) = month ?? _now;
        var (start, _) = TaskerAwardService.MonthRange(y, m);
        var tasker = Db.Tasker(name);
        var customer = Db.Customer("Khách của " + name);
        var service = Db.Service();
        if (reviews < 0) reviews = completed;
        for (var i = 0; i < completed; i++)
        {
            var at = start.AddHours(2 + i);
            var b = Db.Booking(customer, service, BookingStatus.Paid, tasker, configure: x =>
            {
                x.ScheduledTime = at;
                x.StartedAt = at.AddMinutes(5);
                x.CompletedAt = at.AddMinutes(50);
            });
            var offer = Db.Offer(b, tasker, OfferStatus.Accepted);
            offer.SentAt = at.AddMinutes(-30);
            if (i < reviews)
            {
                var r = Db.Review(b, customer, tasker, rating);
                r.CreatedAt = at.AddMinutes(55);
            }
        }
        Db.SaveChanges();
        return tasker;
    }

    [Fact]
    public async Task ChuaDuDieuKien_BaoRoConThieuGi_KhongCoHang()
    {
        var t = Tasker("Trần Thị Lan", 3, reviews: 2);

        var me = await CreateAwards().MyRankingAsync(t.Id);

        Assert.False(me.Eligible);
        Assert.Null(me.Rank);
        Assert.False(me.InTop);
        Assert.Contains(me.MissingConditions, x => x.StartsWith("Cần thêm 7 đơn hoàn tất"));
        Assert.Contains(me.MissingConditions, x => x.StartsWith("Cần thêm 3 đánh giá"));
        Assert.Equal(0m, me.PointsToTop!.Value); // chưa ai đủ điều kiện → chỉ cần đủ điều kiện là vào top
        Assert.Equal(5, me.ScoreParts.Count);
        Assert.Equal((10, 10, 5), (me.Rules.TopCount, me.Rules.MinCompletedBookings, me.Rules.MinReviews));
        Assert.Equal(1m, me.Rules.RatingWeight + me.Rules.CompletedWeight + me.Rules.AcceptanceWeight + me.Rules.OnTimeWeight + me.Rules.NoDisputeWeight);
        Assert.Equal((_now.Year, _now.Month), (me.Year, me.Month));
    }

    [Fact]
    public async Task DuDieuKien_CoHangTamTinh_PhanThuong_VaTopChiHienTenGoiNguoiKhac()
    {
        Tasker("Lê Văn Hùng", 20, rating: 5);
        var lan = Tasker("Trần Thị Lan", 15, rating: 5);
        Tasker("Phạm Minh Tuấn", 12, rating: 4);

        var me = await CreateAwards().MyRankingAsync(lan.Id);

        Assert.True(me.Eligible);
        Assert.Empty(me.MissingConditions);
        Assert.Equal(2, me.Rank);
        Assert.Equal(3, me.EligibleCount);
        Assert.True(me.InTop);
        Assert.Null(me.PointsToTop);
        Assert.True(me.PointsBehindPrevious > 0);
        Assert.Equal(500_000m, me.CurrentReward!.Bonus); // hạng 2–3 theo Awards:Tiers mặc định
        Assert.Equal(me.Score, Math.Round(me.ScoreParts.Sum(p => p.Points), 2), 1); // tổng điểm thành phần ≈ điểm tổng

        Assert.Equal(new[] { "Hùng", "Trần Thị Lan", "Tuấn" }, me.Leaderboard.Select(x => x.DisplayName));
        Assert.True(me.Leaderboard[1].IsMe);
        Assert.Equal(new[] { 1, 2, 3 }, me.Leaderboard.Select(x => x.Rank));
    }

    [Fact]
    public async Task NgoaiTop_BaoSoDiemCanThemDeVaoTop()
    {
        AwardOptions.TopCount = 1;
        Tasker("Lê Văn Hùng", 20, rating: 5);
        var lan = Tasker("Trần Thị Lan", 10, rating: 4);

        var me = await CreateAwards().MyRankingAsync(lan.Id);

        Assert.Equal(2, me.Rank);
        Assert.False(me.InTop);
        Assert.Null(me.CurrentReward);
        Assert.True(me.PointsToTop > 0);
        Assert.Single(me.Leaderboard);
    }

    [Fact]
    public async Task LichSu_ChiCoThangDaDuyet()
    {
        var admin = Db.Admin();
        var lan = Tasker("Trần Thị Lan", 12, month: _prev);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        Assert.Empty((await CreateAwards().MyRankingAsync(lan.Id)).History); // mới chốt, chưa duyệt

        await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);
        var h = Assert.Single((await CreateAwards().MyRankingAsync(lan.Id)).History);
        Assert.Equal((_prev.Year, _prev.Month, 1, 1_000_000m), (h.Year, h.Month, h.Rank, h.BonusAmount));
    }

    [Fact]
    public async Task HoSoCongKhai_TaskerDangTop_CoNhanDangTop_TaskerChuaDuDieuKien_KhongCo()
    {
        var top = Tasker("Trần Thị Lan", 12);
        var newbie = Tasker("Võ Thị Hoa", 2);
        var customer = Db.Customer("Nguyễn Minh Anh");
        var service = Db.Service();
        Db.Booking(customer, service, BookingStatus.Paid, top);
        Db.Booking(customer, service, BookingStatus.Paid, newbie);

        var profile = (await new PublicProfilesController(Db, CreateAwards()).AsUser(customer.Id, UserRole.Customer).Get(top.Id)).OkValue();
        Assert.NotNull(profile.CurrentTop);
        Assert.Equal($"Đang trong top 10 Tasker tháng {_now.Month:00}/{_now.Year}", profile.CurrentTop!.Label);

        var other = (await new PublicProfilesController(Db, CreateAwards()).AsUser(customer.Id, UserRole.Customer).Get(newbie.Id)).OkValue();
        Assert.Null(other.CurrentTop);
    }

    [Fact]
    public async Task TaskerXemQuaController_TraVeHangCuaChinhMinh()
    {
        var lan = Tasker("Trần Thị Lan", 12);
        var result = (await new TaskerRankingController(CreateAwards()).AsUser(lan.Id, UserRole.Tasker).Me()).OkValue();
        Assert.Equal(1, result.Rank);
        Assert.Equal(1_000_000m, result.CurrentReward!.Bonus);
        Assert.True(result.CurrentReward.Gift);
    }
}
