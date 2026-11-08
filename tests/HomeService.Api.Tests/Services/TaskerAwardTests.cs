using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MsOptions = Microsoft.Extensions.Options.Options;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>EP09 – Tasker xuất sắc tháng (FR-31, BR-T01 → BR-T04).</summary>
public class TaskerAwardServiceTests : TestBase
{
    private readonly (int Year, int Month) _prev = TaskerAwardService.PreviousMonth(DateTime.UtcNow);

    private DateTime InPrevMonth(int day = 5, int hour = 10)
    {
        var (start, _) = TaskerAwardService.MonthRange(_prev.Year, _prev.Month);
        return start.AddDays(day - 1).AddHours(hour);
    }

    /// <summary>Tasker có `completed` đơn Paid trong tháng trước, mỗi đơn 1 đánh giá `rating` sao và 1 lời mời đã nhận.</summary>
    private User StarTasker(string name, int completed, int rating = 5, int reviews = -1, bool late = false)
    {
        var tasker = Db.Tasker(name);
        var customer = Db.Customer("Khách của " + name);
        var service = Db.Service();
        if (reviews < 0) reviews = completed;
        for (var i = 0; i < completed; i++)
        {
            var at = InPrevMonth(1 + i % 25);
            var b = Db.Booking(customer, service, BookingStatus.Paid, tasker, configure: x =>
            {
                x.ScheduledTime = at;
                x.StartedAt = late ? at.AddMinutes(40) : at.AddMinutes(5);
                x.CompletedAt = at.AddHours(2);
            });
            var offer = Db.Offer(b, tasker, OfferStatus.Accepted);
            offer.SentAt = at.AddHours(-3);
            if (i < reviews)
            {
                var r = Db.Review(b, customer, tasker, rating);
                r.CreatedAt = at.AddHours(3);
            }
        }
        Db.SaveChanges();
        return tasker;
    }

    // ---------- Tháng theo giờ Việt Nam ----------

    [Fact]
    public void MonthRange_TheoGioVietNam()
    {
        var (start, end) = TaskerAwardService.MonthRange(2026, 10);
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal((2026, 11), TaskerAwardService.VnMonthOf(new DateTime(2026, 10, 31, 17, 30, 0, DateTimeKind.Utc)));
        Assert.Equal((2025, 12), TaskerAwardService.PreviousMonth(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-3)));
    }

    // ---------- Chấm điểm & điều kiện (BR-T01, BR-T02) ----------

    [Fact]
    public void Score_TrungBinhCoTrongSo_NguoiItDanhGiaKhongVuotNguoiNhieuDanhGia()
    {
        var awards = CreateAwards();
        var few = awards.Score(new TaskerAwardService.TaskerMetrics(1, 10, 5, 25, 10, 10, 10, 10, 0, 0, 0, true), 40);
        var many = awards.Score(new TaskerAwardService.TaskerMetrics(2, 40, 40, 196, 40, 40, 40, 40, 0, 0, 0, true), 40);
        Assert.True(many.Score > few.Score);
        Assert.Equal(5.00m, few.RatingAverage);
        Assert.Equal(4.90m, many.RatingAverage);
    }

    [Theory]
    [InlineData(9, 9, 0, 0, true, false)]   // thiếu đơn
    [InlineData(10, 4, 0, 0, true, false)]  // thiếu đánh giá
    [InlineData(12, 12, 1, 0, true, false)] // có lần không đến
    [InlineData(12, 12, 0, 1, true, false)] // thua khiếu nại
    [InlineData(12, 12, 0, 0, false, false)] // tài khoản bị khóa
    [InlineData(10, 5, 0, 0, true, true)]
    public void IsEligible_TheoDieuKienXet(int completed, int reviews, int noShows, int lost, bool active, bool expected)
    {
        var m = new TaskerAwardService.TaskerMetrics(1, completed, reviews, reviews * 5, completed, completed, completed, completed,
            0, noShows, lost, active);
        Assert.Equal(expected, CreateAwards().IsEligible(m));
    }

    [Fact]
    public async Task CollectMetrics_DemDungDuLieuTrongThang()
    {
        var t = StarTasker("Lan", 10, late: true);
        // Đơn của tháng khác + lời mời bị từ chối trong tháng + đánh giá bị gắn cờ không tính
        var other = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Paid, t, configure: b => b.CompletedAt = DateTime.UtcNow);
        var rejected = Db.Offer(other, t, OfferStatus.Rejected);
        rejected.SentAt = InPrevMonth(20);
        var flagged = Db.Review(other, Db.Customer(), t, 1, flagged: true);
        flagged.CreatedAt = InPrevMonth(20);
        Db.SaveChanges();

        var m = (await CreateAwards().CollectMetricsAsync(_prev.Year, _prev.Month)).Single(x => x.TaskerId == t.Id);
        Assert.Equal((10, 10, 50m), (m.Completed, m.ReviewCount, m.RatingSum));
        Assert.Equal((10, 11), (m.Accepted, m.Answered));
        Assert.Equal((0, 10), (m.OnTime, m.Started)); // trễ 40 phút > 15 phút
    }

    // ---------- Chốt bảng xếp hạng ----------

    [Fact]
    public async Task Compute_XepHangVaGanThuongTheoHang()
    {
        var a = StarTasker("An", 20);
        var b = StarTasker("Bình", 15);
        var c = StarTasker("Chi", 12);
        var d = StarTasker("Dũng", 11);
        StarTasker("Em", 8); // không đủ 10 đơn

        var month = await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);

        Assert.Equal("Proposed", month.State);
        Assert.Equal(new[] { a.Id, b.Id, c.Id, d.Id }, month.Items.Select(i => i.TaskerId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, month.Items.Select(i => i.Rank));
        var top = month.Items[0];
        Assert.Equal((1_000_000m, 0.10m, true), (top.BonusAmount, top.PreferentialRate!.Value, top.HasGift));
        Assert.Equal((500_000m, 0.15m, false), (month.Items[1].BonusAmount, month.Items[1].PreferentialRate!.Value, month.Items[1].HasGift));
        Assert.Equal((0m, (decimal?)null, false), (month.Items[3].BonusAmount, month.Items[3].PreferentialRate, month.Items[3].HasGift));
        Assert.NotNull(top.Email); // để Admin liên hệ / xuất Excel
    }

    [Fact]
    public async Task Compute_ThangChuaKetThuc_400()
    {
        var (y, m) = TaskerAwardService.VnMonthOf(DateTime.UtcNow);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().ComputeAsync(y, m));
        Assert.Equal("Chỉ chốt được bảng xếp hạng của tháng đã kết thúc.", ex.Message);
    }

    [Fact]
    public async Task EnsurePreviousMonth_TuChotMotLan()
    {
        StarTasker("An", 10);
        Assert.True(await CreateAwards().EnsurePreviousMonthComputedAsync());
        Assert.False(await CreateAwards().EnsurePreviousMonthComputedAsync());
        Assert.Equal(1, await Fresh().TaskerMonthlyAwards.CountAsync());
    }

    [Fact]
    public async Task Loai_XepHangLai_GiuKhiTinhLai_KhoiPhucDuoc()
    {
        var a = StarTasker("An", 20);
        var b = StarTasker("Bình", 15);
        var month = await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);

        await CreateAwards().ExcludeAsync(month.Items[0].Id, "Đang bị khách khiếu nại chưa xử lý xong");
        var after = await CreateAwards().GetMonthAsync(_prev.Year, _prev.Month);
        var bRow = after.Items.Single(i => i.TaskerId == b.Id);
        Assert.Equal((1, 1_000_000m), (bRow.Rank, bRow.BonusAmount)); // Bình lên hạng 1 và nhận thưởng hạng 1
        var aRow = after.Items.Single(i => i.TaskerId == a.Id);
        Assert.Equal((TaskerAwardStatus.Excluded, 0, 0m), (aRow.Status, aRow.Rank, aRow.BonusAmount));

        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month); // tính lại vẫn giữ người đã loại
        Assert.Equal(TaskerAwardStatus.Excluded, (await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.TaskerId == a.Id)).Status);

        await CreateAwards().RestoreAsync((await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.TaskerId == a.Id)).Id);
        Assert.Equal(1, (await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.TaskerId == a.Id)).Rank);
    }

    [Fact]
    public async Task ChinhThuong_GiuNguyenKhiXepHangLai_KiemTraHopLe()
    {
        StarTasker("An", 20);
        StarTasker("Bình", 15);
        var month = await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        var second = month.Items[1];

        await CreateAwards().UpdateRewardAsync(second.Id, new UpdateTaskerAwardRequest { BonusAmount = 700_000, HasGift = true, GiftDescription = "Bình giữ nhiệt" });
        await CreateAwards().ExcludeAsync(month.Items[0].Id, "Đang bị khách khiếu nại chưa xử lý xong");
        var row = await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.Id == second.Id);
        Assert.Equal((1, 700_000m, true, "Bình giữ nhiệt", (decimal?)null), (row.Rank, row.BonusAmount, row.HasGift, row.GiftDescription, row.PreferentialRate));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().UpdateRewardAsync(second.Id,
            new UpdateTaskerAwardRequest { BonusAmount = 0, PreferentialRate = 0.25m }));
        Assert.Equal("Chiết khấu ưu đãi phải lớn hơn 0% và nhỏ hơn mức chung 20%.", ex.Message);
        ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().UpdateRewardAsync(second.Id,
            new UpdateTaskerAwardRequest { BonusAmount = 0, HasGift = true }));
        Assert.Equal("Vui lòng nhập tên quà tặng.", ex.Message);
    }

    // ---------- Duyệt & trao thưởng (BR-T03, BR-T04) ----------

    [Fact]
    public async Task Duyet_CongThuongVaoViMotLan_ApUuDai_ThongBao_KhoaChinhSua()
    {
        var admin = Db.Admin();
        var a = StarTasker("An", 20);
        StarTasker("Bình", 15);
        var month = await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        var walletBefore = (await Fresh().TaskerProfiles.SingleAsync(t => t.UserId == a.Id)).WalletBalance;

        var approved = await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);

        Assert.Equal("Approved", approved.State);
        Assert.Equal(walletBefore + 1_000_000m, (await Fresh().TaskerProfiles.SingleAsync(t => t.UserId == a.Id)).WalletBalance);
        var tx = await Fresh().WalletTransactions.SingleAsync(w => w.TaskerId == a.Id && w.Type == WalletTransactionType.Bonus);
        Assert.Contains("Thưởng Tasker xuất sắc tháng", tx.Note);
        var row = approved.Items[0];
        Assert.NotNull(row.BonusPaidAt);
        Assert.True(row.PreferentialTo > DateTime.UtcNow);
        Assert.Contains(await Fresh().Notifications.Where(n => n.UserId == a.Id).Select(n => n.Title).ToListAsync(),
            t => t.StartsWith("Chúc mừng bạn là Tasker xuất sắc tháng"));
        Assert.Equal(1, await Fresh().AuditLogs.CountAsync(l => l.Action == AuditService.TaskerAwardApproved));
        Assert.Equal(2, await Fresh().AuditLogs.CountAsync(l => l.Action == AuditService.TaskerBonus));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(2, await Fresh().WalletTransactions.CountAsync(w => w.Type == WalletTransactionType.Bonus)); // không cộng trùng
        await Assert.ThrowsAsync<ApiException>(() => CreateAwards().ComputeAsync(_prev.Year, _prev.Month));
        await Assert.ThrowsAsync<ApiException>(() => CreateAwards().ExcludeAsync(month.Items[1].Id, "Lý do loại sau khi duyệt"));
    }

    [Fact]
    public async Task UuDaiChietKhau_ChotLucNhanDon()
    {
        var admin = Db.Admin();
        var a = StarTasker("An", 20);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);

        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Matched);
        Db.Offer(booking, a);
        await CreateOfferService().AcceptAsync(booking.Id, a.Id);

        Assert.Equal(0.10m, (await Fresh().Bookings.SingleAsync(b => b.Id == booking.Id)).CommissionRate);
        var active = await TaskerAwardService.ActivePreferentialAsync(Fresh(), a.Id, DateTime.UtcNow);
        Assert.Equal(0.10m, active!.Value.Rate);
    }

    [Fact]
    public async Task UuDaiChietKhau_HetHan_KhongApDung()
    {
        var a = Db.Tasker();
        Db.TaskerMonthlyAwards.Add(new TaskerMonthlyAward
        {
            Year = 2025, Month = 1, TaskerId = a.Id, Rank = 1, Status = TaskerAwardStatus.Approved, PreferentialRate = 0.1m,
            PreferentialFrom = DateTime.UtcNow.AddDays(-40), PreferentialTo = DateTime.UtcNow.AddDays(-10), ApprovedAt = DateTime.UtcNow.AddDays(-40),
        });
        Db.SaveChanges();
        Assert.Null(await TaskerAwardService.ActivePreferentialAsync(Db, a.Id, DateTime.UtcNow));
    }

    // ---------- Quà hiện vật & xuất Excel ----------

    [Fact]
    public async Task EmailMoiNhanQua_GuiDungNguoi_GhiTrangThai_DaTraoQua()
    {
        var admin = Db.Admin();
        var a = StarTasker("An", 20);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        var month = await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);
        var id = month.Items[0].Id;

        await CreateAwards().SendGiftEmailAsync(id, new GiftInvitationRequest
        {
            Time = "9:00 – 11:00 thứ Bảy 07/11", Place = "806 Lê Quang Đạo, TP. Hồ Chí Minh", Note = "Gặp chị Trân ở quầy lễ tân",
        });

        var mail = Assert.Single(Emails.Messages);
        Assert.Equal(a.Email, mail.To);
        Assert.Contains("Mời nhận quà Tasker xuất sắc tháng", mail.Subject);
        Assert.Contains("806 Lê Quang Đạo", mail.Text);
        var row = await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.Id == id);
        Assert.NotNull(row.GiftEmailSentAt);
        Assert.Contains("Gặp chị Trân", row.GiftInvitation);

        await CreateAwards().MarkGiftDeliveredAsync(id);
        Assert.NotNull((await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.Id == id)).GiftDeliveredAt);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().SendGiftEmailAsync(id,
            new GiftInvitationRequest { Time = "Sáng mai", Place = "Văn phòng công ty" }));
        Assert.Equal("Tasker đã nhận quà.", ex.Message);
    }

    [Fact]
    public async Task EmailMoiNhanQua_KhongCoQua_HoacLoiGui()
    {
        var admin = Db.Admin();
        StarTasker("An", 20);
        StarTasker("Bình", 15);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        var month = await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);
        var req = new GiftInvitationRequest { Time = "Sáng thứ Bảy", Place = "Văn phòng công ty" };

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().SendGiftEmailAsync(month.Items[1].Id, req));
        Assert.Equal("Tasker này không có quà hiện vật.", ex.Message);

        Emails.Fail = true;
        ex = await Assert.ThrowsAsync<ApiException>(() => CreateAwards().SendGiftEmailAsync(month.Items[0].Id, req));
        Assert.Equal(503, ex.StatusCode);
        Assert.Null((await Fresh().TaskerMonthlyAwards.SingleAsync(x => x.Id == month.Items[0].Id)).GiftEmailSentAt);
    }

    [Fact]
    public async Task XuatExcel_GhiNhatKyKiemToan()
    {
        StarTasker("An", 20);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        var result = await new TaskerAwardsController(CreateAwards()).AsUser(1, UserRole.Admin)
            .Export(new AwardMonthRequest { Year = _prev.Year, Month = _prev.Month });
        Assert.Single(result.OkValue().Items);
        Assert.Contains("có email và SĐT", (await Fresh().AuditLogs.SingleAsync(l => l.Action == AuditService.TaskerAwardExport)).Detail);
    }

    // ---------- Huy hiệu & hiển thị ----------

    [Fact]
    public async Task HuyHieu_HienTrongHoSoCongKhaiVaHoSoTasker()
    {
        var admin = Db.Admin();
        var a = StarTasker("An", 20);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);

        var profile = (await new PublicProfilesController(Db).AsUser(admin.Id, UserRole.Admin).Get(a.Id)).OkValue();
        var badge = Assert.Single(profile.Badges!);
        Assert.Equal($"Tasker xuất sắc tháng {_prev.Month:00}/{_prev.Year}", badge.Label);

        var mine = (await new TaskersController(Db, CreateNotifications(), MsOptions.Create(BusinessOptions))
            .AsUser(a.Id, UserRole.Tasker).GetMyProfile()).OkValue();
        Assert.Equal((0.10m, 0.10m), (mine.CommissionRate!.Value, mine.PreferentialCommissionRate!.Value));
        Assert.Single(mine.Badges!);
    }

    [Fact]
    public async Task BaoCaoDoanhThu_CoChiPhiKhuyenMaiVaThuongTasker()
    {
        var admin = Db.Admin();
        StarTasker("An", 20);
        await CreateAwards().ComputeAsync(_prev.Year, _prev.Month);
        await CreateAwards().ApproveAsync(_prev.Year, _prev.Month, admin.Id);
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Paid);
        Db.Payments.Add(new Payment
        {
            BookingId = booking.Id, Amount = 120_000, PromoSubsidy = 30_000, CommissionAmount = 30_000, TaskerAmount = 120_000,
            Status = PaymentStatus.Released, PaidAt = DateTime.UtcNow, ReleasedAt = DateTime.UtcNow,
        });
        Db.SaveChanges();

        var report = (await new ReportsController(Db).AsUser(admin.Id, UserRole.Admin).GetRevenue(null, null)).OkValue();
        Assert.Equal((30_000m, 1_000_000m, 30_000m - 30_000m - 1_000_000m), (report.PromotionCost, report.TaskerBonusCost, report.NetRevenue));
    }
}
