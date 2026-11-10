using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>US30 (SRS v1.8) – khóa tài khoản an toàn khi còn đơn đang dở, email thông báo, mở khóa (AccountLockService).</summary>
public class AccountLockServiceTests : TestBase
{
    private static LockAccountRequest Req(AccountLockMode mode = AccountLockMode.AfterOrders,
        AccountLockReason reason = AccountLockReason.Fraud) => new()
    {
        Reason = reason,
        Detail = "  Gian lận xác nhận hoàn thành ở nhiều đơn  ",
        Mode = mode,
    };

    [Fact]
    public async Task Lock_KhongCoDonDangDo_KhoaNgay_GhiLyDo_GuiEmail()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();

        var result = await CreateLocks().LockAsync(tasker.Id, admin.Id, Req());

        Assert.Equal("Locked", result.Status);
        var u = await Fresh().Users.Include(x => x.TaskerProfile).SingleAsync(x => x.Id == tasker.Id);
        Assert.False(u.IsActive);
        Assert.NotNull(u.LockedAt);
        Assert.Null(u.LockPendingAt);
        Assert.Equal(AccountLockReason.Fraud, u.LockReason);
        Assert.Equal("Gian lận xác nhận hoàn thành ở nhiều đơn", u.LockDetail);
        Assert.Equal(admin.Id, u.LockedById);
        Assert.False(u.TaskerProfile!.IsAvailable);
        var mail = Assert.Single(Emails.Messages);
        Assert.Equal(u.Email, mail.To);
        Assert.Contains("đã bị khóa", mail.Subject);
        Assert.Contains("Gian lận xác nhận hoàn thành", mail.Text);
        Assert.Contains("/mo-khoa?email=", mail.Text); // đường dẫn gửi yêu cầu mở khóa
        Assert.Contains(await Fresh().AuditLogs.ToListAsync(), a => a.Action == AuditService.AccountLocked);
    }

    [Fact]
    public async Task Lock_TaskerConDon_ChoKhoa_KhongNhanDonMoi_TuKhoaKhiXong()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();
        var service = Db.Service();
        var job = Db.Booking(Db.Customer(), service, BookingStatus.Accepted, tasker);

        var result = await CreateLocks().LockAsync(tasker.Id, admin.Id, Req());

        Assert.Equal("Pending", result.Status);
        Assert.Equal(1, result.ActiveOrders);
        var u = await Fresh().Users.Include(x => x.TaskerProfile).SingleAsync(x => x.Id == tasker.Id);
        Assert.True(u.IsActive);              // vẫn đăng nhập để làm xong đơn
        Assert.NotNull(u.LockPendingAt);
        Assert.False(u.TaskerProfile!.IsAvailable);
        Assert.Contains("sắp bị khóa", Assert.Single(Emails.Messages).Subject);

        // Không nhận thêm đơn mới
        var other = Db.Booking(Db.Customer("Khách 2"), service, BookingStatus.Matched, configure: b => b.ScheduledTime = DateTime.UtcNow.AddDays(2));
        Db.Offer(other, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(other.Id, tasker.Id));
        Assert.Contains("chờ khóa", ex.Message);

        // Còn đơn đang dở → chưa khóa
        Assert.Equal(0, await CreateLocks().FinalizePendingLocksAsync());
        Db.ChangeTracker.Clear();
        var j = await Db.Bookings.SingleAsync(b => b.Id == job.Id);
        j.Status = BookingStatus.Paid;
        await Db.SaveChangesAsync();

        Assert.Equal(1, await CreateLocks().FinalizePendingLocksAsync());
        var locked = await Fresh().Users.SingleAsync(x => x.Id == tasker.Id);
        Assert.False(locked.IsActive);
        Assert.Null(locked.LockPendingAt);
        Assert.NotNull(locked.LockedAt);
        Assert.Contains("đã bị khóa", Emails.Messages.Last().Subject);
    }

    [Fact]
    public async Task Lock_KhachChoKhoa_KhongDatDonMoi()
    {
        var admin = Db.Admin();
        var customer = Db.Customer();
        var service = Db.Service();
        Db.Booking(customer, service, BookingStatus.Pending);

        await CreateLocks().LockAsync(customer.Id, admin.Id, Req(reason: AccountLockReason.NonPayment));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(customer.Id, new CreateBookingRequest
        {
            ServiceCategoryId = service.Id, ScheduledTime = DateTime.UtcNow.AddHours(5), Address = "12 Lê Lợi, Quận 1",
            Latitude = Seed.BenThanhLat, Longitude = Seed.BenThanhLng, Quantity = 2,
        }));
        Assert.Equal("Tài khoản của bạn đang chờ khóa nên không thể đặt đơn mới.", ex.Message);
    }

    [Fact]
    public async Task KhoaNgay_DonDaNhan_GoTasker_TimTaskerKhac_GiaiPhongTamGiu()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker(wallet: 500_000m, configure: p => p.ReservedAmount = 30_000m);
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Accepted, tasker, PaymentMethod.Cash,
            b => { b.MatchRound = 1; b.CommissionRate = 0.1m; });

        var result = await CreateLocks().LockAsync(tasker.Id, admin.Id, Req(AccountLockMode.Immediate));

        Assert.Equal("Locked", result.Status);
        var b = await Fresh().Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.Null(b.TaskerId);
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.Equal(2, b.MatchRound);           // vòng ghép mới
        Assert.Equal(0.2m, b.CommissionRate);    // trả về tỷ lệ chung (ưu đãi của Tasker cũ không còn áp dụng)
        Assert.Equal(0m, (await Fresh().TaskerProfiles.SingleAsync()).ReservedAmount);
        Assert.Contains(await Fresh().Notifications.Where(n => n.UserId == customer.Id).ToListAsync(),
            n => n.Title == "Đang tìm Tasker khác cho bạn");
        Assert.Contains($"Đơn #{booking.Id}", Assert.Single(result.Actions));
    }

    [Fact]
    public async Task KhoaNgay_DonOnlineDangLam_TuMoKhieuNai_KhongChoLamLai()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, tasker);
        Db.HeldPayment(booking);

        await CreateLocks().LockAsync(tasker.Id, admin.Id, Req(AccountLockMode.Immediate));

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Disputed, (await fresh.Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
        var dispute = await fresh.Disputes.SingleAsync();
        Assert.Equal(DisputeReason.TaskerLocked, dispute.Reason);
        Assert.Contains(await fresh.Notifications.ToListAsync(), n => n.Title == "Khiếu nại do khóa Tasker" && n.UserId == admin.Id);

        // Tasker đã bị khóa → không chọn "Làm lại" được, chỉ hoàn tiền
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().ResolveAsync(dispute.Id, admin.Id,
            new ResolveDisputeRequest { Resolution = DisputeResolution.Redo, RedoScheduledTime = DateTime.UtcNow.AddDays(1), AdminNote = "Làm lại cho khách" }));
        Assert.Contains("không thể làm lại", ex.Message);
        await CreateDisputeService().ResolveAsync(dispute.Id, admin.Id,
            new ResolveDisputeRequest { Resolution = DisputeResolution.FullRefund, AdminNote = "Hoàn toàn bộ vì Tasker bị khóa" });
        Assert.Equal(BookingStatus.Refunded, (await Fresh().Bookings.SingleAsync(x => x.Id == booking.Id)).Status);
    }

    [Fact]
    public async Task KhoaNgay_DonTienMatDangLam_HuyKhongPhi()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, tasker, PaymentMethod.Cash);

        await CreateLocks().LockAsync(tasker.Id, admin.Id, Req(AccountLockMode.Immediate));

        var b = await Fresh().Bookings.SingleAsync(x => x.Id == booking.Id);
        Assert.Equal(BookingStatus.Cancelled, b.Status);
        Assert.Equal(0m, b.CancellationFee);
    }

    [Fact]
    public async Task KhoaNgayKhachHang_HuyDonChuaBatDau_KhongPhi_HoanTien_DonDangLamGiuNguyen()
    {
        var admin = Db.Admin();
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var service = Db.Service();
        var searching = Db.Booking(customer, service, BookingStatus.Pending);
        Db.HeldPayment(searching);
        // Đã có Tasker nhận, còn dưới 2 giờ: bình thường mất phí 20% – khóa tài khoản thì miễn phí
        var soon = Db.Booking(customer, service, BookingStatus.Accepted, tasker, configure: b => b.ScheduledTime = DateTime.UtcNow.AddMinutes(70));
        Db.HeldPayment(soon);
        var working = Db.Booking(customer, service, BookingStatus.InProgress, Db.Tasker("Tasker 2"));

        await CreateLocks().LockAsync(customer.Id, admin.Id, Req(AccountLockMode.Immediate, AccountLockReason.Misconduct));

        var fresh = Fresh();
        foreach (var id in new[] { searching.Id, soon.Id })
        {
            var b = await fresh.Bookings.Include(x => x.Payment).SingleAsync(x => x.Id == id);
            Assert.Equal(BookingStatus.Cancelled, b.Status);
            Assert.Equal(0m, b.CancellationFee);
            Assert.Equal(PaymentStatus.Refunded, b.Payment!.Status);
        }
        Assert.Equal(BookingStatus.InProgress, (await fresh.Bookings.SingleAsync(x => x.Id == working.Id)).Status);
        Assert.Contains(await fresh.Notifications.Where(n => n.UserId == tasker.Id).ToListAsync(), n => n.Title == "Đơn đã bị hủy");
    }

    [Fact]
    public async Task Preview_LietKeDonDangDoVaNoiDungEmail()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();
        var b = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, tasker);

        var pending = await CreateLocks().PreviewAsync(tasker.Id, admin.Id, Req());
        var item = Assert.Single(pending.ActiveOrders);
        Assert.Equal(b.Id, item.BookingId);
        Assert.Contains("gỡ Tasker", item.ImmediateAction);
        Assert.Contains("sắp bị khóa", pending.EmailSubject);

        var now = await CreateLocks().PreviewAsync(tasker.Id, admin.Id, Req(AccountLockMode.Immediate));
        Assert.Contains("đã bị khóa", now.EmailSubject);
        Assert.Contains($"Đơn #{b.Id}", now.EmailText);
        Assert.Empty(Emails.Messages); // xem trước không gửi email
    }

    [Fact]
    public async Task Lock_DaKhoa_Hoac_TuKhoaChinhMinh_400()
    {
        var admin = Db.Admin();
        var locked = Db.Customer(configure: u => u.IsActive = false);
        Assert.Equal("Tài khoản đã bị khóa.",
            (await Assert.ThrowsAsync<ApiException>(() => CreateLocks().LockAsync(locked.Id, admin.Id, Req()))).Message);
        Assert.Equal("Không thể tự khóa tài khoản của chính mình.",
            (await Assert.ThrowsAsync<ApiException>(() => CreateLocks().LockAsync(admin.Id, admin.Id, Req()))).Message);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => CreateLocks().LockAsync(999, admin.Id, Req()))).StatusCode);
    }

    [Fact]
    public async Task Unlock_HuyLenhChoKhoa_VaMoKhoa_DongYeuCauDangCho()
    {
        var admin = Db.Admin();
        var tasker = Db.Tasker();
        Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, tasker);
        await CreateLocks().LockAsync(tasker.Id, admin.Id, Req());

        await CreateLocks().UnlockAsync(tasker.Id, admin.Id, null);
        var u = await Fresh().Users.SingleAsync(x => x.Id == tasker.Id);
        Assert.True(u.IsActive);
        Assert.Null(u.LockPendingAt);
        Assert.Null(u.LockReason);
        Assert.Contains("hủy lệnh khóa", Emails.Messages.Last().Subject);

        var customer = Db.Customer(configure: x => { x.IsActive = false; x.LockedAt = DateTime.UtcNow.AddDays(-1); x.LockReason = AccountLockReason.Other; });
        Db.AccountAppeals.Add(new AccountAppeal { UserId = customer.Id, LockedAt = customer.LockedAt!.Value, Content = "Xin mở khóa giúp tôi, tôi đã trả nợ." });
        await Db.SaveChangesAsync();
        await CreateLocks().UnlockAsync(customer.Id, admin.Id, "  Đã xác minh khách trả nợ  ");

        var fresh = Fresh();
        var c = await fresh.Users.SingleAsync(x => x.Id == customer.Id);
        Assert.True(c.IsActive);
        Assert.Null(c.LockedAt);
        Assert.Equal(AppealStatus.Approved, (await fresh.AccountAppeals.SingleAsync()).Status);
        var mail = Emails.Messages.Last();
        Assert.Contains("đã được mở khóa", mail.Subject);
        Assert.Contains("Đã xác minh khách trả nợ", mail.Text);
        Assert.Equal("Tài khoản không bị khóa.",
            (await Assert.ThrowsAsync<ApiException>(() => CreateLocks().UnlockAsync(customer.Id, admin.Id, null))).Message);
    }

    [Fact]
    public async Task Email_GuiLoi_VanKhoaThanhCong()
    {
        Emails.Fail = true;
        var admin = Db.Admin();
        var c = Db.Customer();
        var result = await CreateLocks().LockAsync(c.Id, admin.Id, Req());
        Assert.Equal("Locked", result.Status);
        Assert.False((await Fresh().Users.SingleAsync(x => x.Id == c.Id)).IsActive);
    }
}
