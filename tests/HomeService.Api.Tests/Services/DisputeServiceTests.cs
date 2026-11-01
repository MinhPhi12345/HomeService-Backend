using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>US35 / FR-26 – khiếu nại & xử lý tranh chấp; FR-27 – khiếu nại về khoản nợ (DisputeService).</summary>
public class DisputeServiceTests : TestBase
{
    private static CreateDisputeRequest NewDispute() => new()
    {
        Reason = DisputeReason.PoorQuality,
        Description = "  Phòng khách còn bẩn, chưa lau  ",
        EvidenceUrls = new() { "http://localhost:8080/uploads/202610/e1.png", "/uploads/202610/e2.png" },
    };

    private static ResolveDisputeRequest Resolve(DisputeResolution r, decimal? refund = null, DateTime? redo = null) =>
        new() { Resolution = r, RefundAmount = refund, RedoScheduledTime = redo, AdminNote = "  Đã xem ảnh hai bên  " };

    /// <summary>Đơn online đã hoàn thành (chờ xác nhận), tiền đang tạm giữ.</summary>
    private (Booking Booking, User Customer, User Tasker) CompletedOnline(decimal price = 200_000m, double completedHoursAgo = 1)
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(customer, Db.Service(price: price), BookingStatus.Completed, tasker, configure: b =>
        {
            b.CompletedAt = DateTime.UtcNow.AddHours(-completedHoursAgo);
            b.AutoConfirmAt = b.CompletedAt.Value.AddHours(24);
        });
        Db.HeldPayment(booking);
        return (booking, customer, tasker);
    }

    // ---------- Điều kiện khiếu nại ----------

    [Theory]
    [InlineData(BookingStatus.Completed, PaymentMethod.EWallet, 1.0, true)]
    [InlineData(BookingStatus.Completed, PaymentMethod.EWallet, 25.0, false)]  // quá 24 giờ
    [InlineData(BookingStatus.Paid, PaymentMethod.Cash, 2.0, true)]            // tiền mặt đã thu
    [InlineData(BookingStatus.Paid, PaymentMethod.EWallet, 2.0, false)]        // online đã giải ngân
    [InlineData(BookingStatus.PaymentOverdue, PaymentMethod.Cash, 2.0, true)]  // khách cho rằng đã trả
    [InlineData(BookingStatus.InProgress, PaymentMethod.EWallet, 0.0, false)]
    public void CanDispute(BookingStatus status, PaymentMethod method, double completedHoursAgo, bool expected)
    {
        var now = DateTime.UtcNow;
        var b = new Booking { Status = status, PaymentMethod = method, CompletedAt = now.AddHours(-completedHoursAgo) };
        Assert.Equal(expected, DisputeService.CanDispute(b, now, 24));
    }

    [Fact]
    public void CanDispute_ChuaHoanThanhHoacDaKhieuNai_False()
    {
        var now = DateTime.UtcNow;
        Assert.False(DisputeService.CanDispute(new Booking { Status = BookingStatus.Completed }, now, 24));
        var withDispute = new Booking { Status = BookingStatus.Completed, CompletedAt = now, Dispute = new Dispute() };
        Assert.False(DisputeService.CanDispute(withDispute, now, 24));
    }

    // ---------- Tạo khiếu nại ----------

    [Fact]
    public async Task Create_ChuyenDonSangDangKhieuNaiVaThongBao()
    {
        var (booking, customer, tasker) = CompletedOnline();
        var admin = Db.Admin();

        var dispute = await CreateDisputeService().CreateAsync(booking.Id, customer.Id, NewDispute());

        var fresh = Fresh();
        var d = await fresh.Disputes.SingleAsync();
        Assert.Equal(dispute.Id, d.Id);
        Assert.Equal(tasker.Id, d.TaskerId);
        Assert.Equal("Phòng khách còn bẩn, chưa lau", d.Description);
        Assert.Equal("/uploads/202610/e1.png,/uploads/202610/e2.png", d.EvidenceUrls);
        Assert.Equal(DisputeStatus.Open, d.Status);
        Assert.Equal(BookingStatus.Disputed, (await fresh.Bookings.SingleAsync()).Status);
        var receivers = await fresh.Notifications.Select(n => n.UserId).ToListAsync();
        Assert.Contains(tasker.Id, receivers);
        Assert.Contains(admin.Id, receivers);
    }

    [Fact]
    public async Task Create_HetHan24Gio_BiTuChoi()
    {
        var (booking, customer, _) = CompletedOnline(completedHoursAgo: 25);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().CreateAsync(booking.Id, customer.Id, NewDispute()));
        Assert.Equal("Đã hết thời hạn khiếu nại.", ex.Message);
    }

    [Fact]
    public async Task Create_KhieuNaiLanHai_409()
    {
        var (booking, customer, _) = CompletedOnline();
        await CreateDisputeService().CreateAsync(booking.Id, customer.Id, NewDispute());
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().CreateAsync(booking.Id, customer.Id, NewDispute()));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Create_KhongPhaiChuDon_403()
    {
        var (booking, _, _) = CompletedOnline();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().CreateAsync(booking.Id, Db.Customer("X").Id, NewDispute()));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Create_LyDoKhongHopLe_BiTuChoi()
    {
        var (booking, customer, _) = CompletedOnline();
        var req = NewDispute();
        req.Reason = (DisputeReason)99;
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().CreateAsync(booking.Id, customer.Id, req));
        Assert.Equal("Lý do khiếu nại không hợp lệ.", ex.Message);
    }

    // ---------- Tasker phản hồi ----------

    [Fact]
    public async Task Respond_LuuPhanHoiVaBaoAdmin()
    {
        var (booking, _, tasker) = CompletedOnline();
        var admin = Db.Admin();
        var dispute = Db.Dispute(booking);

        await CreateDisputeService().RespondAsync(dispute.Id, tasker.Id, new TaskerDisputeResponseRequest
        {
            Response = "  Tôi đã lau kỹ, có ảnh  ",
            EvidenceUrls = new() { "http://x.vn:8080/uploads/r.png" },
        });

        var fresh = Fresh();
        var d = await fresh.Disputes.SingleAsync();
        Assert.Equal("Tôi đã lau kỹ, có ảnh", d.TaskerResponse);
        Assert.Equal("/uploads/r.png", d.TaskerEvidenceUrls);
        Assert.NotNull(d.TaskerRespondedAt);
        Assert.Equal(admin.Id, (await fresh.Notifications.SingleAsync()).UserId);
    }

    [Fact]
    public async Task Respond_QuaHan_BiTuChoi()
    {
        var (booking, _, tasker) = CompletedOnline();
        var dispute = Db.Dispute(booking, d => d.CreatedAt = DateTime.UtcNow.AddHours(-25));
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().RespondAsync(dispute.Id, tasker.Id, new TaskerDisputeResponseRequest { Response = "Phản hồi muộn quá" }));
        Assert.Equal("Đã hết thời hạn phản hồi khiếu nại.", ex.Message);
    }

    [Fact]
    public async Task Respond_KhongPhaiTaskerCuaDon_403_VaDaXuLy_400()
    {
        var (booking, _, tasker) = CompletedOnline();
        var dispute = Db.Dispute(booking);
        var forbidden = await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().RespondAsync(dispute.Id, Db.Tasker("B").Id, new TaskerDisputeResponseRequest { Response = "Không phải tôi làm" }));
        Assert.Equal(403, forbidden.StatusCode);

        dispute.Status = DisputeStatus.Resolved;
        Db.SaveChanges();
        var closed = await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().RespondAsync(dispute.Id, tasker.Id, new TaskerDisputeResponseRequest { Response = "Phản hồi khi đã xong" }));
        Assert.Equal("Khiếu nại đã được xử lý.", closed.Message);
    }

    // ---------- Admin xử lý ----------

    [Fact]
    public async Task Resolve_BacKhieuNai_GiaiNganDuChoTasker_KhongCongViPham()
    {
        var (booking, _, tasker) = CompletedOnline();
        var admin = Db.Admin();
        var dispute = Db.Dispute(booking);
        booking.Status = BookingStatus.Disputed;
        Db.SaveChanges();

        await CreateDisputeService().ResolveAsync(dispute.Id, admin.Id, Resolve(DisputeResolution.ReleaseFull));

        var fresh = Fresh();
        var d = await fresh.Disputes.SingleAsync();
        Assert.Equal(DisputeStatus.Resolved, d.Status);
        Assert.Equal(DisputeResolution.ReleaseFull, d.Resolution);
        Assert.Equal("Đã xem ảnh hai bên", d.AdminNote);
        Assert.Equal(admin.Id, d.ResolvedById);
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(160_000m, p.WalletBalance);
        Assert.Equal(0, p.ViolationPoints);
        Assert.Contains(await fresh.AuditLogs.Select(a => a.Action).ToListAsync(), a => a == AuditService.DisputeResolved);
    }

    [Fact]
    public async Task Resolve_HoanMotPhan_ChiaTienDungVaCongViPham()
    {
        var (booking, customer, tasker) = CompletedOnline(price: 200_000m);
        var dispute = Db.Dispute(booking);

        await CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.PartialRefund, refund: 50_000m));

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.PartiallyRefunded, p.Status);
        Assert.Equal(50_000m, p.RefundedAmount);
        Assert.Equal(30_000m, p.CommissionAmount);
        Assert.Equal(120_000m, p.TaskerAmount);
        Assert.Equal(1, (await fresh.TaskerProfiles.SingleAsync()).ViolationPoints);
        Assert.Equal(50_000m, (await fresh.Disputes.SingleAsync()).RefundAmount);
        Assert.Contains(await fresh.Notifications.Where(n => n.UserId == customer.Id).Select(n => n.Title).ToListAsync(), t => t == "Đã hoàn tiền");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("200000")]      // bằng toàn bộ số tiền → phải dùng FullRefund
    [InlineData("1000.5")]      // không phải số nguyên
    public async Task Resolve_HoanMotPhan_SoTienKhongHopLe(string refund)
    {
        var (booking, _, _) = CompletedOnline(price: 200_000m);
        var dispute = Db.Dispute(booking);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.PartialRefund, decimal.Parse(refund))));
        Assert.Equal("Số tiền hoàn không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task Resolve_HoanToanBo_DonChuyenDaHoanTien()
    {
        var (booking, _, tasker) = CompletedOnline(price: 200_000m);
        var dispute = Db.Dispute(booking);

        await CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.FullRefund));

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Refunded, (await fresh.Bookings.SingleAsync()).Status);
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Refunded, p.Status);
        Assert.Equal(200_000m, p.RefundedAmount);
        Assert.Equal(0m, (await fresh.TaskerProfiles.SingleAsync(t => t.UserId == tasker.Id)).WalletBalance);
        Assert.Equal(200_000m, (await fresh.Disputes.SingleAsync()).RefundAmount);
    }

    [Fact]
    public async Task Resolve_HoanToanBo_SoTienKhacTongTien_BiTuChoi()
    {
        var (booking, _, _) = CompletedOnline(price: 200_000m);
        var dispute = Db.Dispute(booking);
        await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.FullRefund, refund: 100_000m)));
    }

    [Fact]
    public async Task Resolve_LamLai_DonVeDaNhanVoiGioMoi_TienVanTamGiu()
    {
        var (booking, _, _) = CompletedOnline();
        var dispute = Db.Dispute(booking);
        var redo = DateTime.UtcNow.AddDays(1);

        await CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.Redo, redo: redo));

        var fresh = Fresh();
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Accepted, b.Status);
        Assert.Equal(redo, b.ScheduledTime, TimeSpan.FromMilliseconds(1));
        Assert.Null(b.CompletedAt);
        Assert.Null(b.AutoConfirmAt);
        Assert.Equal(PaymentStatus.Held, (await fresh.Payments.SingleAsync()).Status);
        Assert.Equal(1, (await fresh.TaskerProfiles.SingleAsync()).ViolationPoints);
    }

    [Fact]
    public async Task Resolve_LamLai_ThieuHoacSaiGio_BiTuChoi()
    {
        var (booking, _, _) = CompletedOnline();
        var dispute = Db.Dispute(booking);
        var admin = Db.Admin();
        await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().ResolveAsync(dispute.Id, admin.Id, Resolve(DisputeResolution.Redo)));
        await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().ResolveAsync(dispute.Id, admin.Id, Resolve(DisputeResolution.Redo, redo: DateTime.UtcNow.AddMinutes(10))));
    }

    [Fact]
    public async Task Resolve_DonTienMat_KhongDuocHoanTien()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Disputed, tasker, PaymentMethod.Cash,
            b => b.CompletedAt = DateTime.UtcNow.AddHours(-1));
        Db.Payments.Add(new Payment { BookingId = booking.Id, Amount = booking.TotalPrice, Method = PaymentMethod.Cash, Status = PaymentStatus.Success });
        Db.SaveChanges();
        var dispute = Db.Dispute(booking);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.PartialRefund, 10_000m)));
        Assert.Equal("Đơn tiền mặt chỉ có thể bác khiếu nại hoặc yêu cầu làm lại.", ex.Message);
    }

    [Fact]
    public async Task Resolve_DaXuLy_KhongXuLyLan2()
    {
        var (booking, _, _) = CompletedOnline();
        var dispute = Db.Dispute(booking);
        var admin = Db.Admin();
        await CreateDisputeService().ResolveAsync(dispute.Id, admin.Id, Resolve(DisputeResolution.ReleaseFull));
        Db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().ResolveAsync(dispute.Id, admin.Id, Resolve(DisputeResolution.ReleaseFull)));
        Assert.Equal("Khiếu nại đã được xử lý.", ex.Message);
    }

    [Fact]
    public async Task Resolve_KhieuNaiKhongTonTai_404()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateDisputeService().ResolveAsync(999, 1, Resolve(DisputeResolution.ReleaseFull)));
        Assert.Equal(404, ex.StatusCode);
    }

    // ---------- Khiếu nại về khoản nợ tiền mặt (FR-27) ----------

    private (Booking Booking, User Customer, User Tasker) UnpaidCash()
    {
        var customer = Db.Customer(configure: u => { u.OutstandingAmount = 150_000m; u.UnpaidReportCount = 2; u.IsCashBlocked = true; });
        var tasker = Db.Tasker(wallet: 100_000m);
        var booking = Db.Booking(customer, Db.Service(price: 150_000m), BookingStatus.Disputed, tasker, PaymentMethod.Cash,
            b => b.CompletedAt = DateTime.UtcNow.AddHours(-1));
        return (booking, customer, tasker);
    }

    [Fact]
    public async Task ResolveNo_BacKhieuNai_GiuNo()
    {
        var (booking, customer, _) = UnpaidCash();
        var dispute = Db.Dispute(booking);

        await CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.ReleaseFull));

        var fresh = Fresh();
        Assert.Equal(BookingStatus.PaymentOverdue, (await fresh.Bookings.SingleAsync()).Status);
        Assert.Equal(150_000m, (await fresh.Users.SingleAsync(u => u.Id == customer.Id)).OutstandingAmount);
    }

    [Fact]
    public async Task ResolveNo_HuyNo_DonDaThanhToan_TaskerBaoSaiCong2Diem()
    {
        var (booking, customer, _) = UnpaidCash();
        var dispute = Db.Dispute(booking);

        await CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.FullRefund));

        var fresh = Fresh();
        var c = await fresh.Users.SingleAsync(u => u.Id == customer.Id);
        Assert.Equal(0m, c.OutstandingAmount);
        Assert.Equal(1, c.UnpaidReportCount);
        Assert.False(c.IsCashBlocked);
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(70_000m, p.WalletBalance);       // trừ chiết khấu 30.000 như đơn tiền mặt
        Assert.Equal(2, p.ViolationPoints);
    }

    [Fact]
    public async Task ResolveNo_KetQuaKhac_BiTuChoi()
    {
        var (booking, _, _) = UnpaidCash();
        var dispute = Db.Dispute(booking);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateDisputeService().ResolveAsync(dispute.Id, Db.Admin().Id, Resolve(DisputeResolution.Redo, redo: DateTime.UtcNow.AddDays(1))));
        Assert.Contains("chỉ có thể bác (0) hoặc hủy khoản nợ (2)", ex.Message);
    }

    // ---------- Ánh xạ ----------

    [Fact]
    public async Task ToResponse_DuThongTinHaiBenVaHanPhanHoi()
    {
        var (booking, customer, tasker) = CompletedOnline();
        var dispute = Db.Dispute(booking, d => d.TaskerEvidenceUrls = "/uploads/t1.png,/uploads/t2.png");
        var service = CreateDisputeService();

        var full = await service.QueryWithDetails().FirstAsync(d => d.Id == dispute.Id);
        var r = service.ToResponse(full);

        Assert.Equal(booking.TotalPrice, r.Amount);
        Assert.Equal(customer.FullName, r.CustomerName);
        Assert.Equal(tasker.FullName, r.TaskerName);
        Assert.Single(r.EvidenceUrls);
        Assert.Equal(2, r.TaskerEvidenceUrls.Count);
        Assert.Equal(dispute.CreatedAt.AddHours(24), r.TaskerResponseDueAt, TimeSpan.FromMilliseconds(1));
    }
}
