using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>US48 (SRS v1.8) – yêu cầu mở khóa tài khoản: OTP, phiên gửi, giới hạn 1 chờ / 7 ngày / 3 lần, Admin duyệt (AppealService).</summary>
public class AppealServiceTests : TestBase
{
    public AppealServiceTests()
    {
        // Test gửi OTP nhiều lần liên tiếp – bỏ thời gian chờ gửi lại
        OtpOptions.ResendSeconds = 0;
        OtpOptions.MaxPerHour = 100;
    }

    private User LockedCustomer(string email = "bikhoa@test.vn") => Db.Customer(configure: u =>
    {
        u.Email = email;
        u.IsActive = false;
        u.LockedAt = DateTime.UtcNow.AddDays(-2);
        u.LockReason = AccountLockReason.NonPayment;
        u.LockDetail = "Nợ đơn #15 quá 7 ngày";
    });

    private async Task<AppealSessionResponse> SessionAsync(string email = "bikhoa@test.vn")
    {
        await CreateAppeals().SendOtpAsync(email);
        return await CreateAppeals().VerifyAsync(email, Emails.LastCode(email));
    }

    private static SubmitAppealRequest Submit(string token, List<string>? urls = null) => new()
    {
        Token = token,
        Content = "  Tôi đã thanh toán khoản nợ qua chuyển khoản, xin mở khóa tài khoản.  ",
        AttachmentUrls = urls,
    };

    [Fact]
    public async Task SendOtp_TaiKhoanKhongBiKhoa_KhongGuiEmail_TraKetQuaGiongNhau()
    {
        Db.Customer(configure: u => u.Email = "binhthuong@test.vn");
        LockedCustomer();

        var a = await CreateAppeals().SendOtpAsync("binhthuong@test.vn");
        var b = await CreateAppeals().SendOtpAsync("khongco@test.vn");
        Assert.Empty(Emails.Messages);
        Assert.Equal("bi********@test.vn", a.MaskedEmail);
        Assert.NotNull(b.MaskedEmail);

        await CreateAppeals().SendOtpAsync("  BiKhoa@Test.vn ");
        var mail = Assert.Single(Emails.Messages);
        Assert.Contains("mở khóa", mail.Subject);
    }

    [Fact]
    public async Task Verify_DungMa_TraTokenVaLyDoKhoa_SaiMa_400()
    {
        LockedCustomer();
        await CreateAppeals().SendOtpAsync("bikhoa@test.vn");
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAppeals().VerifyAsync("bikhoa@test.vn", "000000" == Emails.LastCode() ? "111111" : "000000"));
        Assert.Contains("không đúng", ex.Message);

        var session = await CreateAppeals().VerifyAsync("bikhoa@test.vn", Emails.LastCode());
        Assert.True(session.Token.Length >= 40);
        Assert.Equal("Không thanh toán", session.Context.LockReasonText);
        Assert.Equal("Nợ đơn #15 quá 7 ngày", session.Context.LockDetail);
        Assert.True(session.Context.CanSubmit);
        Assert.Equal(3, session.Context.RemainingAppeals);
        // Token chỉ lưu bản băm
        Assert.DoesNotContain(await Fresh().OtpCodes.ToListAsync(), o => o.CodeHash.Contains(session.Token));
    }

    [Fact]
    public async Task Submit_TaoYeuCau_BaoAdmin_EmailXacNhan_KhongGuiThemKhiDangCho()
    {
        var admin = Db.Admin();
        var user = LockedCustomer();
        var session = await SessionAsync();

        var ctx = await CreateAppeals().SubmitAsync(Submit(session.Token, new() { "http://localhost:8080/uploads/202611/a.png" }));

        Assert.False(ctx.CanSubmit);
        Assert.Contains("đang chờ xử lý", ctx.BlockedMessage);
        var appeal = await Fresh().AccountAppeals.SingleAsync();
        Assert.Equal(user.Id, appeal.UserId);
        Assert.Equal("Tôi đã thanh toán khoản nợ qua chuyển khoản, xin mở khóa tài khoản.", appeal.Content);
        Assert.Equal("/uploads/202611/a.png", appeal.AttachmentUrls);
        Assert.Contains(await Fresh().Notifications.ToListAsync(), n => n.UserId == admin.Id && n.Title == "Yêu cầu mở khóa mới");
        Assert.Contains("Đã nhận yêu cầu mở khóa", Emails.Messages.Last().Subject);

        // Phiên đã dùng → không gửi lại được bằng token cũ
        await Assert.ThrowsAsync<ApiException>(() => CreateAppeals().SubmitAsync(Submit(session.Token)));
        // Phiên mới nhưng đang có yêu cầu chờ
        var again = await SessionAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAppeals().SubmitAsync(Submit(again.Token)));
        Assert.Contains("đang chờ xử lý", ex.Message);
    }

    [Fact]
    public async Task Submit_AnhKhongPhaiCuaHeThong_400()
    {
        LockedCustomer();
        var session = await SessionAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateAppeals().SubmitAsync(Submit(session.Token, new() { "https://example.com/x.png" })));
        Assert.Equal("Ảnh đính kèm không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task Session_TokenSai_400()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateAppeals().GetContextAsync("khong-hop-le"));
        Assert.Contains("hết hạn", ex.Message);
    }

    [Fact]
    public async Task TuChoi_GuiEmail_Cho7Ngay_ToiDa3Lan()
    {
        var admin = Db.Admin();
        LockedCustomer();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var s = await SessionAsync();
            Assert.True(s.Context.CanSubmit);
            await CreateAppeals().SubmitAsync(Submit(s.Token));
            var appeal = await Fresh().AccountAppeals.OrderByDescending(a => a.Id).FirstAsync();
            await CreateAppeals().RejectAsync(appeal.Id, admin.Id, "Chưa có bằng chứng thanh toán hợp lệ.");
            var rejectMail = Emails.Messages.Last();
            Assert.Contains("chưa được chấp nhận", rejectMail.Subject);
            Assert.Contains("Chưa có bằng chứng thanh toán", rejectMail.Text);

            var blocked = await SessionAsync();
            Assert.False(blocked.Context.CanSubmit);
            if (attempt < 3)
            {
                Assert.Contains("gửi lại từ", blocked.Context.BlockedMessage);
                // Giả lập đã qua 7 ngày
                Db.ChangeTracker.Clear();
                var a = await Db.AccountAppeals.SingleAsync(x => x.Id == appeal.Id);
                a.ResolvedAt = DateTime.UtcNow.AddDays(-8);
                await Db.SaveChangesAsync();
            }
            else
            {
                Assert.Contains("đủ 3 yêu cầu", blocked.Context.BlockedMessage);
                Assert.Contains("hết số lần", rejectMail.Text);
            }
        }
    }

    [Fact]
    public async Task ChapNhan_MoKhoa_EmailChapNhan_ChiTietChoAdmin()
    {
        var admin = Db.Admin();
        var user = LockedCustomer();
        var s = await SessionAsync();
        await CreateAppeals().SubmitAsync(Submit(s.Token));
        var appeal = await Fresh().AccountAppeals.SingleAsync();

        var detail = await CreateAppeals().GetAsync(appeal.Id);
        Assert.Equal(1, detail.AttemptNo);
        Assert.Equal(AccountLockReason.NonPayment, detail.User.LockReason);
        Assert.Single(await CreateAppeals().ListAsync(AppealStatus.Pending));

        await CreateAppeals().ApproveAsync(appeal.Id, admin.Id, "Đã xác minh chuyển khoản");

        var fresh = Fresh();
        Assert.True((await fresh.Users.SingleAsync(u => u.Id == user.Id)).IsActive);
        var a = await fresh.AccountAppeals.SingleAsync();
        Assert.Equal(AppealStatus.Approved, a.Status);
        Assert.Equal(admin.Id, a.ResolvedById);
        var mail = Emails.Messages.Last();
        Assert.Contains("đã được chấp nhận", mail.Subject);
        Assert.Contains("Đã xác minh chuyển khoản", mail.Text);
        Assert.Equal("Yêu cầu này đã được xử lý.",
            (await Assert.ThrowsAsync<ApiException>(() => CreateAppeals().RejectAsync(appeal.Id, admin.Id, "Thử từ chối lại lần nữa"))).Message);
    }
}
