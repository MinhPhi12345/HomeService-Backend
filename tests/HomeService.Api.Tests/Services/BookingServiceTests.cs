using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>EP03 – tạo đơn, cập nhật trạng thái, xác nhận, thử lại, báo khách chưa thanh toán (BookingService).</summary>
public class BookingServiceTests : TestBase
{
    private static CreateBookingRequest Request(int serviceId, PaymentMethod? method = null, int? quantity = null,
        bool? present = null, DateTime? at = null) => new()
    {
        ServiceCategoryId = serviceId,
        ScheduledTime = at ?? DateTime.UtcNow.AddHours(3),
        Address = "  12 Nguyễn Huệ, Quận 1  ",
        Latitude = Seed.BenThanhLat,
        Longitude = Seed.BenThanhLng,
        Quantity = quantity,
        PaymentMethod = method,
        CustomerPresent = present,
        Note = "  Có nuôi mèo  ",
    };

    /// <summary>Khách đủ điều kiện trả tiền mặt: đã có 3 đơn hoàn tất.</summary>
    private User CashEligibleCustomer(ServiceCategory service)
    {
        var c = Db.Customer();
        for (var i = 0; i < 3; i++) Db.Booking(c, service, BookingStatus.Paid);
        return c;
    }

    // ---------- Tạo đơn (US12, US13) ----------

    [Fact]
    public async Task Create_Online_ChoThanhToan15PhutVaThongBao()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);

        var booking = await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, quantity: 2));

        var saved = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.AwaitingPayment, saved.Status);
        Assert.Equal(PaymentMethod.EWallet, saved.PaymentMethod);       // mặc định online
        Assert.Equal(300_000m, saved.TotalPrice);                        // giá × số lượng (BR-B01)
        Assert.Equal(0.2m, saved.CommissionRate);                        // chốt tỷ lệ chiết khấu
        Assert.Equal("12 Nguyễn Huệ, Quận 1", saved.Address);
        Assert.Equal("Có nuôi mèo", saved.Note);
        Assert.Equal("0901234567", saved.ContactPhone);                  // lấy SĐT hồ sơ
        Assert.InRange(saved.PaymentDueAt!.Value, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.Equal(booking.Id, saved.Id);
        Assert.Equal("Vui lòng thanh toán đơn hàng", (await Fresh().Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task Create_MacDinhSoLuong1_VaSdtLienHeRieng()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 120_000m);
        var req = Request(service.Id);
        req.ContactPhone = " 0911222333 ";

        await CreateBookingService().CreateAsync(customer.Id, req);

        var saved = await Fresh().Bookings.SingleAsync();
        Assert.Equal(1, saved.Quantity);
        Assert.Equal(120_000m, saved.TotalPrice);
        Assert.Equal("0911222333", saved.ContactPhone);
    }

    [Fact]
    public async Task Create_TienMat_DuDieuKien_TimTaskerNgay()
    {
        var service = Db.Service(price: 200_000m);
        var customer = CashEligibleCustomer(service);

        var booking = await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, PaymentMethod.Cash, present: true));

        var saved = await Fresh().Bookings.FirstAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.Null(saved.PaymentDueAt);
        Assert.True(saved.CustomerPresent);
    }

    [Fact]
    public async Task Create_TienMat_ChuaDu3DonHoanTat_BiTuChoi()
    {
        var service = Db.Service();
        var customer = Db.Customer();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().CreateAsync(customer.Id, Request(service.Id, PaymentMethod.Cash, present: true)));
        Assert.Equal("Đơn này chỉ hỗ trợ thanh toán online.", ex.Message);
    }

    [Fact]
    public async Task Create_TienMat_KhongCamKetCoMat_BiTuChoi()
    {
        var service = Db.Service();
        var customer = CashEligibleCustomer(service);
        await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().CreateAsync(customer.Id, Request(service.Id, PaymentMethod.Cash, present: false)));
    }

    [Fact]
    public async Task Create_TienMat_VuotMot_TrieuDong_BiTuChoi()
    {
        var service = Db.Service(price: 600_000m);
        var customer = CashEligibleCustomer(service);
        await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().CreateAsync(customer.Id, Request(service.Id, PaymentMethod.Cash, quantity: 2, present: true)));
    }

    [Fact]
    public async Task Create_TienMat_KhachBiChanTienMat_BiTuChoi()
    {
        var service = Db.Service();
        var customer = CashEligibleCustomer(service);
        customer.IsCashBlocked = true;
        Db.SaveChanges();
        await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().CreateAsync(customer.Id, Request(service.Id, PaymentMethod.Cash, present: true)));
    }

    [Fact]
    public async Task Create_KhachDangNo_BiTuChoi()
    {
        var service = Db.Service();
        var customer = Db.Customer(configure: u => u.OutstandingAmount = 100_000m);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(customer.Id, Request(service.Id)));
        Assert.Equal("Vui lòng thanh toán khoản nợ trước khi đặt đơn mới.", ex.Message);
    }

    [Fact]
    public async Task Create_DichVuDaXoa_BiTuChoi()
    {
        var service = Db.Service(isActive: false);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(Db.Customer().Id, Request(service.Id)));
        Assert.Equal("Dịch vụ không tồn tại.", ex.Message);
    }

    [Fact]
    public async Task Create_DichVuKhongTonTai_BiTuChoi()
    {
        await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(Db.Customer().Id, Request(9999)));
    }

    [Theory]
    [InlineData(null, 106.7)]
    [InlineData(10.7, null)]
    [InlineData(91.0, 106.7)]
    [InlineData(10.7, 181.0)]
    public async Task Create_ToaDoKhongHopLe_BiTuChoi(double? lat, double? lng)
    {
        var service = Db.Service();
        var req = Request(service.Id);
        req.Latitude = lat;
        req.Longitude = lng;
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(Db.Customer().Id, req));
        Assert.Equal("Thiếu vị trí thực hiện.", ex.Message);
    }

    [Fact]
    public async Task Create_PhuongThucKhongHopLe_BiTuChoi()
    {
        var service = Db.Service();
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().CreateAsync(Db.Customer().Id, Request(service.Id, (PaymentMethod)9)));
        Assert.Equal("Phương thức thanh toán không hợp lệ.", ex.Message);
    }

    // ---------- Kiểm tra thời gian đặt (SRS 6.1) ----------

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(-2.0, false)]
    [InlineData(1.1, true)]
    [InlineData(24.0 * 29, true)]
    [InlineData(24.0 * 31, false)]
    public void ValidateScheduledTime_Tu1GioDen30Ngay(double hoursFromNow, bool valid)
    {
        var at = DateTime.UtcNow.AddHours(hoursFromNow);
        if (valid) Assert.Equal(DateTimeKind.Utc, CreateBookingService().ValidateScheduledTime(at).Kind);
        else Assert.Equal("Thời gian đặt không hợp lệ.", Assert.Throws<ApiException>(() => CreateBookingService().ValidateScheduledTime(at)).Message);
    }

    // ---------- Cập nhật trạng thái (US18) ----------

    private UpdateBookingStatusRequest Status(BookingStatus s, string? photo = "http://localhost:8080/uploads/202610/p.png", bool? cash = null)
        => new() { Status = s, PhotoUrl = photo, CashCollected = cash };

    [Fact]
    public async Task UpdateStatus_BatDau_LuuAnhCheckInDangTuongDoi()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, tasker);

        await CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.InProgress));

        var saved = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.InProgress, saved.Status);
        Assert.Equal("/uploads/202610/p.png", saved.CheckInPhotoUrl);
        Assert.Equal("Tasker đã bắt đầu làm việc", (await Fresh().Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task UpdateStatus_ThieuAnh_BiTuChoi()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.InProgress, photo: null)));
        Assert.Equal("Vui lòng chụp ảnh hiện trạng.", ex.Message);
    }

    [Fact]
    public async Task UpdateStatus_HoanThanhOnline_ChoKhachXacNhan24Gio()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, tasker);

        await CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.Completed));

        var saved = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Completed, saved.Status);
        Assert.NotNull(saved.CompletedAt);
        Assert.Equal(saved.CompletedAt!.Value.AddHours(24), saved.AutoConfirmAt!.Value);
        Assert.Equal("/uploads/202610/p.png", saved.CheckOutPhotoUrl);
    }

    [Fact]
    public async Task UpdateStatus_HoanThanhTienMat_ChuaXacNhanThuTien_BiTuChoi()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, tasker, PaymentMethod.Cash);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.Completed, cash: false)));
        Assert.Contains("thu đủ tiền mặt", ex.Message);
    }

    [Fact]
    public async Task UpdateStatus_HoanThanhTienMat_QuyetToanNgay()
    {
        var tasker = Db.Tasker(wallet: 100_000m, configure: p => p.ReservedAmount = 30_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 150_000m), BookingStatus.InProgress, tasker, PaymentMethod.Cash);

        await CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.Completed, cash: true));

        var fresh = Fresh();
        var saved = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Paid, saved.Status);
        Assert.NotNull(saved.CompletedAt);
        var profile = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(70_000m, profile.WalletBalance);   // trừ chiết khấu 30.000
        Assert.Equal(0m, profile.ReservedAmount);        // giải phóng tạm giữ
        Assert.Equal(PaymentStatus.Success, (await fresh.Payments.SingleAsync()).Status);
    }

    [Fact]
    public async Task UpdateStatus_LamLaiDonTienMatDaQuyetToan_ChuyenPaid()
    {
        var tasker = Db.Tasker(wallet: 100_000m);
        var booking = Db.Booking(Db.Customer(), Db.Service(price: 150_000m), BookingStatus.InProgress, tasker, PaymentMethod.Cash);
        Db.Payments.Add(new Payment { BookingId = booking.Id, Amount = 150_000m, Method = PaymentMethod.Cash, Status = PaymentStatus.Success });
        Db.SaveChanges();

        await CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.Completed, cash: true));

        var fresh = Fresh();
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
        Assert.Equal(100_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance); // không trừ chiết khấu lần 2
        Assert.Equal(1, await fresh.Payments.CountAsync());
    }

    [Fact]
    public async Task UpdateStatus_ChuyenTrangThaiSai_BiTuChoi()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.Completed)));
        Assert.Equal("Chuyển trạng thái không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task UpdateStatus_KhongPhaiTaskerCuaDon_403()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, Db.Tasker());
        var other = Db.Tasker("Người khác");
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().UpdateStatusAsync(booking.Id, other.Id, Status(BookingStatus.InProgress)));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_DonKhongTonTai_404()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().UpdateStatusAsync(12345, 1, Status(BookingStatus.InProgress)));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_TaskerDaBiDanhDauKhongDen_409()
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Accepted, tasker, configure: b => b.IsTaskerNoShow = true);
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateBookingService().UpdateStatusAsync(booking.Id, tasker.Id, Status(BookingStatus.InProgress)));
        Assert.Equal(409, ex.StatusCode);
    }

    // ---------- Xác nhận hoàn thành (US34) ----------

    [Fact]
    public async Task Confirm_GiaiNganChoTasker()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(customer, Db.Service(price: 100_000m), BookingStatus.Completed, tasker);
        Db.HeldPayment(booking);

        await CreateBookingService().ConfirmAsync(booking.Id, customer.Id);

        var fresh = Fresh();
        var saved = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Paid, saved.Status);
        Assert.False(saved.IsAutoConfirmed);
        Assert.Equal(80_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
    }

    [Fact]
    public async Task Confirm_SaiTrangThai_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.InProgress, Db.Tasker());
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().ConfirmAsync(booking.Id, customer.Id));
        Assert.Equal("Đơn không ở trạng thái chờ xác nhận.", ex.Message);
    }

    [Fact]
    public async Task Confirm_KhongPhaiChuDon_403()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.Completed, Db.Tasker());
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().ConfirmAsync(booking.Id, Db.Customer("Khác").Id));
        Assert.Equal(403, ex.StatusCode);
    }

    // ---------- Thử lại (US33, US37) ----------

    [Fact]
    public async Task Retry_ChuaCoTaskerNhan_VongMoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.NoTaskerFound, configure: b => { b.CurrentWave = 3; b.MatchRound = 1; });

        await CreateBookingService().RetryAsync(booking.Id, customer.Id, null);

        var saved = await Fresh().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.Equal(2, saved.MatchRound);
        Assert.Equal(0, saved.CurrentWave);
        Assert.Null(saved.NextMatchAt);
    }

    [Fact]
    public async Task Retry_DoiGioMoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.NoTaskerFound);
        var newTime = DateTime.UtcNow.AddDays(2);

        await CreateBookingService().RetryAsync(booking.Id, customer.Id, new RetryBookingRequest { ScheduledTime = newTime });

        Assert.Equal(newTime, (await Fresh().Bookings.SingleAsync()).ScheduledTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Retry_GioCuDaQua_BatBuocChonGioMoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.NoTaskerFound, configure: b => b.ScheduledTime = DateTime.UtcNow.AddMinutes(-5));
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().RetryAsync(booking.Id, customer.Id, null));
        Assert.Equal("Thời gian đặt không hợp lệ.", ex.Message);
    }

    [Fact]
    public async Task Retry_VuotSoVong_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.NoTaskerFound, configure: b => b.MatchRound = 5);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().RetryAsync(booking.Id, customer.Id, null));
        Assert.Equal("Đã vượt quá số lần thử lại.", ex.Message);
    }

    [Fact]
    public async Task Retry_SaiTrangThai_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Pending);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().RetryAsync(booking.Id, customer.Id, null));
        Assert.Equal("Đơn không ở trạng thái chờ thử lại.", ex.Message);
    }

    [Fact]
    public async Task Retry_TaskerKhongDen_TimTaskerKhacVaBaoTaskerCu()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Accepted, tasker, configure: b =>
        {
            b.IsTaskerNoShow = true;
            b.NoShowAt = DateTime.UtcNow;
            b.ScheduledTime = DateTime.UtcNow.AddMinutes(-40);
        });

        await CreateBookingService().RetryAsync(booking.Id, customer.Id, null);

        var fresh = Fresh();
        var saved = await fresh.Bookings.SingleAsync();
        Assert.Null(saved.TaskerId);
        Assert.False(saved.IsTaskerNoShow);
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.True(saved.ScheduledTime > DateTime.UtcNow.AddMinutes(59));   // giờ mới ≈ 1 giờ sau
        Assert.Equal(0, saved.ScheduledTime.Minute % 5);                     // làm tròn 5 phút
        Assert.Equal(tasker.Id, (await fresh.Notifications.SingleAsync()).UserId);
    }

    // ---------- Khách chưa thanh toán tiền mặt (US36) ----------

    private static ReportUnpaidRequest Unpaid() => new() { Note = "  Khách không có nhà để trả tiền  ", PhotoUrl = "/uploads/202610/x.png" };

    [Fact]
    public async Task ReportUnpaid_GhiNoKhach_KhongTruChietKhau()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker(wallet: 100_000m, configure: p => p.ReservedAmount = 30_000m);
        var booking = Db.Booking(customer, Db.Service(price: 150_000m), BookingStatus.InProgress, tasker, PaymentMethod.Cash);

        await CreateBookingService().ReportUnpaidAsync(booking.Id, tasker.Id, Unpaid());

        var fresh = Fresh();
        var saved = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.PaymentOverdue, saved.Status);
        Assert.Equal("/uploads/202610/x.png", saved.CheckOutPhotoUrl);
        var c = await fresh.Users.SingleAsync(u => u.Id == customer.Id);
        Assert.Equal(150_000m, c.OutstandingAmount);
        Assert.Equal(1, c.UnpaidReportCount);
        Assert.False(c.IsCashBlocked);
        var p = await fresh.TaskerProfiles.SingleAsync();
        Assert.Equal(100_000m, p.WalletBalance);
        Assert.Equal(0m, p.ReservedAmount);
        var audit = await fresh.AuditLogs.SingleAsync();
        Assert.Equal(AuditService.Debt, audit.Action);
        Assert.Contains("Khách không có nhà để trả tiền", audit.Detail);
    }

    [Fact]
    public async Task ReportUnpaid_LanThuHai_ChanThanhToanTienMat()
    {
        var customer = Db.Customer(configure: u => u.UnpaidReportCount = 1);
        var tasker = Db.Tasker();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.InProgress, tasker, PaymentMethod.Cash);

        await CreateBookingService().ReportUnpaidAsync(booking.Id, tasker.Id, Unpaid());

        Assert.True((await Fresh().Users.SingleAsync(u => u.Id == customer.Id)).IsCashBlocked);
    }

    [Theory]
    [InlineData(BookingStatus.Accepted, PaymentMethod.Cash)]
    [InlineData(BookingStatus.InProgress, PaymentMethod.EWallet)]
    public async Task ReportUnpaid_KhongPhaiDonTienMatDangLam_BiTuChoi(BookingStatus status, PaymentMethod method)
    {
        var tasker = Db.Tasker();
        var booking = Db.Booking(Db.Customer(), Db.Service(), status, tasker, method);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().ReportUnpaidAsync(booking.Id, tasker.Id, Unpaid()));
        Assert.Equal("Chỉ báo được với đơn tiền mặt đang thực hiện.", ex.Message);
    }

    [Fact]
    public async Task ReportUnpaid_KhongPhaiTaskerCuaDon_403()
    {
        var booking = Db.Booking(Db.Customer(), Db.Service(), BookingStatus.InProgress, Db.Tasker(), PaymentMethod.Cash);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().ReportUnpaidAsync(booking.Id, Db.Tasker("B").Id, Unpaid()));
        Assert.Equal(403, ex.StatusCode);
    }
}
