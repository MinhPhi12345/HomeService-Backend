using MsOptions = Microsoft.Extensions.Options.Options;
using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Controllers;

/// <summary>EP06 – ReviewsController (US25 → US27).</summary>
public class ReviewsControllerTests : TestBase
{
    private (Booking Booking, User Customer, User Tasker) PaidBooking()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        return (Db.Booking(customer, Db.Service(), BookingStatus.Paid, tasker), customer, tasker);
    }

    private ReviewsController As(User u) => new ReviewsController(Db).AsUser(u.Id, u.Role);

    [Fact]
    public async Task Create_KhachDanhGiaTasker_CapNhatDiemTrungBinh()
    {
        var (booking, customer, tasker) = PaidBooking();
        var other = Db.Booking(Db.Customer("Khách 2"), Db.Service("Giặt"), BookingStatus.Paid, tasker);
        Db.Review(other, Db.Users.Single(u => u.Id == other.CustomerId), tasker, 3);

        var result = await As(customer).Create(new CreateReviewRequest(booking.Id, tasker.Id, 5, "  Rất tốt  "));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var body = Assert.IsType<ReviewResponse>(created.Value);
        Assert.Equal("Rất tốt", body.Comment);
        Assert.Equal(customer.FullName, body.ReviewerName);
        Assert.Equal(tasker.FullName, body.RevieweeName);
        Assert.Equal(4.00m, (await Fresh().TaskerProfiles.SingleAsync()).RatingAverage);   // (3 + 5) / 2
    }

    [Fact]
    public async Task Create_TaskerDanhGiaKhach()
    {
        var (booking, customer, tasker) = PaidBooking();
        var result = await As(tasker).Create(new CreateReviewRequest(booking.Id, customer.Id, 4, null));
        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(customer.Id, (await Fresh().Reviews.SingleAsync()).RevieweeId);
    }

    [Fact]
    public async Task Create_KhongThuocDon_400()
    {
        var (booking, _, tasker) = PaidBooking();
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(Db.Customer("Người lạ")).Create(new CreateReviewRequest(booking.Id, tasker.Id, 5, null)));
        Assert.Equal("Bạn không thuộc đơn này.", ex.Message);
    }

    [Fact]
    public async Task Create_SaiNguoiDuocDanhGia_400()
    {
        var (booking, customer, _) = PaidBooking();
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(customer).Create(new CreateReviewRequest(booking.Id, customer.Id, 5, null)));
        Assert.Equal("Người được đánh giá không đúng.", ex.Message);
    }

    [Fact]
    public async Task Create_DonChuaHoanThanh_400()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.InProgress, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(customer).Create(new CreateReviewRequest(booking.Id, tasker.Id, 5, null)));
        Assert.Equal("Chỉ đánh giá được khi đơn đã hoàn thành.", ex.Message);
    }

    [Fact]
    public async Task Create_DanhGiaLanHai_409_VaDonKhongTonTai_400()
    {
        var (booking, customer, tasker) = PaidBooking();
        await As(customer).Create(new CreateReviewRequest(booking.Id, tasker.Id, 5, null));
        var dup = await Assert.ThrowsAsync<ApiException>(() => As(customer).Create(new CreateReviewRequest(booking.Id, tasker.Id, 4, null)));
        Assert.Equal(409, dup.StatusCode);
        var missing = await Assert.ThrowsAsync<ApiException>(() => As(customer).Create(new CreateReviewRequest(999, tasker.Id, 4, null)));
        Assert.Equal("Đơn không tồn tại.", missing.Message);
    }

    [Fact]
    public async Task GetForBooking_VaGetForUser_BoQuaDanhGiaBiGanCo()
    {
        var (booking, customer, tasker) = PaidBooking();
        var b2 = Db.Booking(customer, Db.Service("Giặt"), BookingStatus.Paid, tasker);
        Db.Review(booking, customer, tasker, 5);
        Db.Review(b2, customer, tasker, 1, flagged: true);

        Assert.Single((await As(customer).GetForBooking(booking.Id)).OkValue());
        var summary = (await As(customer).GetForUser(tasker.Id)).OkValue();
        Assert.Equal(1, summary.ReviewCount);
        Assert.Equal(5.00m, summary.RatingAverage);
        Assert.Single(summary.Reviews);

        var empty = (await As(customer).GetForUser(customer.Id)).OkValue();
        Assert.Equal(0, empty.ReviewCount);
        Assert.Null(empty.RatingAverage);
    }

    private const string LyDo = "Khách không hề phàn nàn lúc nghiệm thu, nhận xét sai sự thật.";
    private static ReportReviewRequest Rp(string? reason = LyDo) => new(reason);
    private static ModerateReviewRequest Keep(string note = "Đã đối chiếu ảnh, nhận xét phù hợp thực tế.") => new(false, note);
    private static ModerateReviewRequest Remove(string note = "Nhận xét dùng từ ngữ xúc phạm, vi phạm quy định.") => new(true, note);

    [Fact]
    public async Task GetForUser_ChiHienTenGoiNguoiViet()
    {
        var customer = Db.Customer("Nguyễn Thị Minh Anh");
        var tasker = Db.Tasker("Trần Văn Hùng");
        Db.Review(Db.Booking(customer, Db.Service(), BookingStatus.Paid, tasker), customer, tasker, 5);

        var item = Assert.Single((await As(Db.Customer("Khách khác")).GetForUser(tasker.Id)).OkValue().Reviews);
        Assert.Equal("Anh", item.ReviewerName);
        Assert.Equal("Hùng", item.RevieweeName.Split(' ')[^1]);
    }

    [Theory]
    [InlineData("Trần Thị Lan", "Lan")]
    [InlineData("  Lê   Văn   Hùng  ", "Hùng")]
    [InlineData("Mai", "Mai")]
    [InlineData("", "Người dùng")]
    [InlineData(null, "Người dùng")]
    public void NameHelper_LayTenGoi(string? full, string expected) => Assert.Equal(expected, NameHelper.GivenName(full));

    [Fact]
    public async Task Report_LuuLyDoNguoiBaoCao_BaoAdmin_TamBoKhoiDiem()
    {
        var (booking, customer, tasker) = PaidBooking();
        var admin = Db.Admin();
        var b2 = Db.Booking(customer, Db.Service("Giặt"), BookingStatus.Paid, tasker);
        Db.Review(b2, customer, tasker, 5);
        var bad = Db.Review(booking, customer, tasker, 1);

        var result = await As(tasker).Report(bad.Id, Rp("  " + LyDo + "  "), CreateNotifications());

        Assert.IsType<NoContentResult>(result);
        var fresh = Fresh();
        var saved = await fresh.Reviews.SingleAsync(r => r.Id == bad.Id);
        Assert.True(saved.IsFlagged);
        Assert.Equal(LyDo, saved.ReportReason);
        Assert.Equal(tasker.Id, saved.ReportedById);
        Assert.NotNull(saved.ReportedAt);
        Assert.Equal(5.00m, (await fresh.TaskerProfiles.SingleAsync()).RatingAverage);
        var noti = await fresh.Notifications.SingleAsync();
        Assert.Equal(admin.Id, noti.UserId);
        Assert.Contains("nhận xét sai sự thật", noti.Message);
    }

    [Fact]
    public async Task Report_LyDoQuaNgan_400()
    {
        var (booking, customer, tasker) = PaidBooking();
        var review = Db.Review(booking, customer, tasker, 1);
        await Assert.ThrowsAsync<ApiException>(() => As(tasker).Report(review.Id, Rp("   sai    "), CreateNotifications()));
        Assert.False((await Fresh().Reviews.SingleAsync()).IsFlagged);
    }

    [Fact]
    public async Task Report_DanhGiaCuaChinhMinh_400_NguoiNgoai_403_KhongTonTai_404()
    {
        var (booking, customer, tasker) = PaidBooking();
        var review = Db.Review(booking, customer, tasker, 2);
        await Assert.ThrowsAsync<ApiException>(() => As(customer).Report(review.Id, Rp(), CreateNotifications()));
        Assert.IsType<ForbidResult>(await As(Db.Customer("Người lạ")).Report(review.Id, Rp(), CreateNotifications()));
        Assert.IsType<NotFoundResult>(await As(tasker).Report(999, Rp(), CreateNotifications()));
    }

    [Fact]
    public async Task Admin_XemDanhSachCoLyDoVaNguoiBaoCao()
    {
        var (booking, customer, tasker) = PaidBooking();
        var bad = Db.Review(booking, customer, tasker, 1);
        await As(tasker).Report(bad.Id, Rp(), CreateNotifications());
        var admin = new ReviewsController(Db).AsUser(1, UserRole.Admin);

        var item = Assert.Single((await admin.GetFlagged()).OkValue());
        Assert.Equal(bad.Id, item.Id);
        Assert.Equal(LyDo, item.ReportReason);
        Assert.Equal(tasker.FullName, item.ReportedByName);
        Assert.NotNull(item.ReportedAt);
    }

    [Fact]
    public async Task Admin_GoDanhGia_BatBuocLyDo_ThongBaoHaiBen_GhiNhatKy_TinhLaiDiem()
    {
        var (booking, customer, tasker) = PaidBooking();
        var b2 = Db.Booking(customer, Db.Service("Giặt"), BookingStatus.Paid, tasker);
        var keep = Db.Review(b2, customer, tasker, 4);
        var bad = Db.Review(booking, customer, tasker, 1);
        await As(tasker).Report(bad.Id, Rp(), CreateNotifications());
        var admin = new ReviewsController(Db).AsUser(1, UserRole.Admin);

        await Assert.ThrowsAsync<ApiException>(() => admin.Moderate(bad.Id, Remove("ngắn"), CreateNotifications(), CreateAudit()));
        Assert.IsType<NoContentResult>(await admin.Moderate(bad.Id, Remove(), CreateNotifications(), CreateAudit()));

        var fresh = Fresh();
        Assert.Equal(keep.Id, (await fresh.Reviews.SingleAsync(r => r.RemovedAt == null)).Id);
        var removed = await fresh.Reviews.SingleAsync(r => r.Id == bad.Id);   // chỉ ẩn, không xóa hẳn
        Assert.NotNull(removed.RemovedAt);
        Assert.False(removed.IsFlagged);
        Assert.Equal("Nhận xét dùng từ ngữ xúc phạm, vi phạm quy định.", removed.ModerationNote);
        Assert.Equal(4.00m, (await fresh.TaskerProfiles.SingleAsync()).RatingAverage);
        Assert.DoesNotContain((await As(customer).GetForBooking(booking.Id)).OkValue(), r => r.Id == bad.Id);
        Assert.Equal(1, (await As(customer).GetForUser(tasker.Id)).OkValue().ReviewCount);
        Assert.True(await fresh.Notifications.AnyAsync(n => n.UserId == customer.Id && n.Message.Contains("vi phạm quy định")));
        Assert.True(await fresh.Notifications.AnyAsync(n => n.UserId == tasker.Id && n.Title.Contains("được chấp nhận")));
        var log = await fresh.AuditLogs.SingleAsync(a => a.Action == AuditService.ReviewRemoved);
        Assert.Equal(booking.Id, log.BookingId);
        Assert.Contains("Lý do gỡ: Nhận xét dùng từ ngữ xúc phạm", log.Detail);
        Assert.IsType<NotFoundResult>(await admin.Moderate(999, Remove(), CreateNotifications(), CreateAudit()));
    }

    [Fact]
    public async Task DanhGiaBiGo_KhongDuocDanhGiaLaiDonDo_KhongBaoCaoDuoc()
    {
        var (booking, customer, tasker) = PaidBooking();
        var bad = Db.Review(booking, customer, tasker, 1);
        await As(tasker).Report(bad.Id, Rp(), CreateNotifications());
        var admin = new ReviewsController(Db).AsUser(1, UserRole.Admin);
        await admin.Moderate(bad.Id, Remove(), CreateNotifications(), CreateAudit());

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            As(customer).Create(new CreateReviewRequest(booking.Id, tasker.Id, 5, "Viết lại")));
        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("đã bị quản trị viên gỡ", ex.Message);
        Assert.Single(await Fresh().Reviews.Where(r => r.BookingId == booking.Id).ToListAsync());
        Assert.IsType<NotFoundResult>(await As(tasker).Report(bad.Id, Rp(), CreateNotifications()));
    }

    [Fact]
    public async Task Admin_GiuLai_LuuGhiChu_ThongBaoNguoiBaoCao_KhongBaoCaoLaiDuoc()
    {
        var (booking, customer, tasker) = PaidBooking();
        var review = Db.Review(booking, customer, tasker, 2);
        await As(tasker).Report(review.Id, Rp(), CreateNotifications());
        var admin = new ReviewsController(Db).AsUser(1, UserRole.Admin);

        Assert.IsType<NoContentResult>(await admin.Moderate(review.Id, Keep(), CreateNotifications(), CreateAudit()));

        var fresh = Fresh();
        var saved = await fresh.Reviews.SingleAsync();
        Assert.False(saved.IsFlagged);
        Assert.Equal("Đã đối chiếu ảnh, nhận xét phù hợp thực tế.", saved.ModerationNote);
        Assert.NotNull(saved.ModeratedAt);
        Assert.Equal(2.00m, (await fresh.TaskerProfiles.SingleAsync()).RatingAverage);
        Assert.True(await fresh.Notifications.AnyAsync(n => n.UserId == tasker.Id && n.Message.Contains("được giữ lại")));
        Assert.True(await fresh.AuditLogs.AnyAsync(a => a.Action == AuditService.ReviewKept));

        // đã kiểm duyệt: không báo cáo lại, không kiểm duyệt lần nữa
        await Assert.ThrowsAsync<ApiException>(() => As(tasker).Report(review.Id, Rp(), CreateNotifications()));
        await Assert.ThrowsAsync<ApiException>(() => admin.Moderate(review.Id, Remove(), CreateNotifications(), CreateAudit()));
    }

    [Fact]
    public async Task XemTheoDon_ChiNguoiTrongDonThayLyDoBaoCao()
    {
        var (booking, customer, tasker) = PaidBooking();
        var review = Db.Review(booking, customer, tasker, 2);
        await As(tasker).Report(review.Id, Rp(), CreateNotifications());

        Assert.Equal(LyDo, Assert.Single((await As(customer).GetForBooking(booking.Id)).OkValue()).ReportReason);
        Assert.Null(Assert.Single((await As(Db.Customer("Người lạ")).GetForBooking(booking.Id)).OkValue()).ReportReason);
    }
}

/// <summary>US23 / US24 – WalletController.</summary>
public class WalletControllerTests : TestBase
{
    private WalletController New() => new(Db, CreateWallet(), CreateAudit(), MsOptions.Create(MatchingOptions));
    private WalletController As(User tasker) => New().AsUser(tasker.Id, UserRole.Tasker);

    /// <summary>Rút tiền đủ 2 bước: gửi OTP về email rồi rút kèm mã vừa nhận.</summary>
    private async Task<WalletResponse> WithdrawWithOtp(User tasker, decimal amount)
    {
        (await As(tasker).SendWithdrawOtp(new WithdrawOtpRequest { Amount = amount }, CreateOtp())).OkValue();
        return (await As(tasker).Withdraw(new WithdrawRequest { Amount = amount, OtpCode = Emails.LastCode(tasker.Email) }, CreateOtp())).OkValue();
    }

    [Fact]
    public async Task Get_TraSoDuVaKhaDung()
    {
        var tasker = Db.Tasker(wallet: 300_000m, configure: p => p.ReservedAmount = 50_000m);
        var w = (await As(tasker).Get()).OkValue();
        Assert.Equal(300_000m, w.Balance);
        Assert.Equal(50_000m, w.ReservedAmount);
        Assert.Equal(250_000m, w.AvailableBalance);
        Assert.Equal(100_000m, w.MinDeposit);
        Assert.True(w.MeetsDeposit);
        Assert.False((await As(Db.Tasker("Ví thấp", wallet: 99_999m)).Get()).OkValue().MeetsDeposit);
        Assert.IsType<NotFoundResult>((await New().AsUser(999, UserRole.Tasker).Get()).Result);
    }

    [Fact]
    public async Task TopUp_CongViGhiLichSuVaNhatKy()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var w = (await As(tasker).TopUp(new TopUpRequest { Amount = 200_000m, Channel = "Card" })).OkValue();

        Assert.Equal(200_000m, w.Balance);
        Assert.True(w.MeetsDeposit);
        var fresh = Fresh();
        var tx = await fresh.WalletTransactions.SingleAsync();
        Assert.Equal(WalletTransactionType.TopUp, tx.Type);
        Assert.Equal("Nạp tiền qua thẻ ngân hàng (mô phỏng)", tx.Note);
        Assert.Equal(AuditService.WalletTopUp, (await fresh.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task TopUp_SoLe_400()
    {
        var tasker = Db.Tasker();
        var ex = await Assert.ThrowsAsync<ApiException>(() => As(tasker).TopUp(new TopUpRequest { Amount = 100_000.5m }));
        Assert.Equal("Số tiền nạp không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task Withdraw_KhongVuotSoDuKhaDung()
    {
        var tasker = Db.Tasker(wallet: 300_000m, configure: p => { p.ReservedAmount = 100_000m; p.IsAvailable = false; });

        var ex = await Assert.ThrowsAsync<ApiException>(() => As(tasker).SendWithdrawOtp(new WithdrawOtpRequest { Amount = 250_000m }, CreateOtp()));
        Assert.Equal("Số dư khả dụng không đủ.", ex.Message);
        Assert.Empty(Emails.Messages);                     // số tiền sai thì không gửi mã

        var w = await WithdrawWithOtp(tasker, 200_000m);
        Assert.Equal(100_000m, w.Balance);
        Assert.Equal(0m, w.AvailableBalance);
        var tx = await Fresh().WalletTransactions.SingleAsync();
        Assert.Equal(-200_000m, tx.Amount);
        Assert.Equal(WalletTransactionType.Withdraw, tx.Type);
    }

    [Fact]
    public async Task Withdraw_DangBatNhanDon_PhaiGiuKyQuy()
    {
        var tasker = Db.Tasker(wallet: 300_000m);   // đang bật nhận đơn

        var ex = await Assert.ThrowsAsync<ApiException>(() => As(tasker).SendWithdrawOtp(new WithdrawOtpRequest { Amount = 250_000m }, CreateOtp()));
        Assert.Contains("tối thiểu 100.000 đ ký quỹ", ex.Message);
        Assert.Contains("rút tối đa 200.000 đ", ex.Message);

        Assert.Equal(100_000m, (await WithdrawWithOtp(tasker, 200_000m)).Balance);
    }

    [Fact]
    public async Task Withdraw_GuiOtpQuaEmail_SaiMaKhongTruTien_DungMaMoiRut()
    {
        var tasker = Db.Tasker(wallet: 500_000m, configure: p => p.IsAvailable = false);
        var sent = (await As(tasker).SendWithdrawOtp(new WithdrawOtpRequest { Amount = 200_000m }, CreateOtp())).OkValue();
        Assert.Equal(60, sent.ResendAfterSeconds);
        var mail = Emails.Messages.Single();
        Assert.Equal(tasker.Email, mail.To);
        Assert.Contains("rút tiền", mail.Subject);
        Assert.Contains("200.000 đ", mail.Text);              // email ghi rõ số tiền rút

        var code = Emails.LastCode();
        var wrong = code == "000000" ? "111111" : "000000";
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            As(tasker).Withdraw(new WithdrawRequest { Amount = 200_000m, OtpCode = wrong }, CreateOtp()));
        Assert.Equal("Mã OTP không đúng. Bạn còn 4 lần thử.", ex.Message);
        Assert.Equal(500_000m, (await Fresh().TaskerProfiles.SingleAsync()).WalletBalance);

        var w = (await As(tasker).Withdraw(new WithdrawRequest { Amount = 200_000m, OtpCode = code }, CreateOtp())).OkValue();
        Assert.Equal(300_000m, w.Balance);
        Assert.Contains("OTP", (await Fresh().AuditLogs.SingleAsync()).Detail);

        // Mã đã dùng không rút lại được lần 2
        await Assert.ThrowsAsync<ApiException>(() =>
            As(tasker).Withdraw(new WithdrawRequest { Amount = 200_000m, OtpCode = code }, CreateOtp()));
        Assert.Single(await Fresh().WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task Withdraw_MaOtpCuaSoTienKhac_BiTuChoi()
    {
        var tasker = Db.Tasker(wallet: 500_000m, configure: p => p.IsAvailable = false);
        await As(tasker).SendWithdrawOtp(new WithdrawOtpRequest { Amount = 100_000m }, CreateOtp());

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            As(tasker).Withdraw(new WithdrawRequest { Amount = 150_000m, OtpCode = Emails.LastCode() }, CreateOtp()));
        Assert.Contains("dùng để xác nhận rút 100.000 đ", ex.Message);
        Assert.Empty(await Fresh().WalletTransactions.ToListAsync());
    }

    [Fact]
    public async Task Withdraw_ChuaGuiOtp_BiTuChoi()
    {
        var tasker = Db.Tasker(wallet: 500_000m, configure: p => p.IsAvailable = false);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            As(tasker).Withdraw(new WithdrawRequest { Amount = 100_000m, OtpCode = "123456" }, CreateOtp()));
        Assert.Contains("hết hạn hoặc chưa được gửi", ex.Message);
    }

    [Fact]
    public async Task Withdraw_SoLe_400()
    {
        var tasker = Db.Tasker();
        await Assert.ThrowsAsync<ApiException>(() => As(tasker).Withdraw(new WithdrawRequest { Amount = 60_000.1m, OtpCode = "123456" }, CreateOtp()));
    }

    [Fact]
    public async Task Transactions_PhanTrangMoiNhatTruoc_ChiCuaMinh()
    {
        var tasker = Db.Tasker(wallet: 0m);
        var other = Db.Tasker("Khác");
        var wallet = CreateWallet();
        for (var i = 1; i <= 5; i++) await wallet.ApplyAsync(tasker.Id, i * 1000m, WalletTransactionType.TopUp, null, $"#{i}");
        await wallet.ApplyAsync(other.Id, 1000m, WalletTransactionType.TopUp, null, "khác");
        await Db.SaveChangesAsync();

        var page = (await As(tasker).GetTransactions(1, 2)).OkValue();
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("#5", page.Items[0].Note);
        var page3 = (await As(tasker).GetTransactions(3, 2)).OkValue();
        Assert.Equal("#1", Assert.Single(page3.Items).Note);
    }
}
