using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HomeService.Api.Tests.Services;

/// <summary>US21 – thanh toán trước & tạm giữ; US36 – trả nợ (PaymentService).</summary>
public class PaymentServiceTests : TestBase
{
    private Booking AwaitingBooking(User customer, PaymentMethod method = PaymentMethod.EWallet) =>
        Db.Booking(customer, Db.Service(price: 250_000m), BookingStatus.AwaitingPayment, method: method,
            configure: b => b.PaymentDueAt = DateTime.UtcNow.AddMinutes(10));

    private static CreatePaymentRequest Pay(int bookingId, PaymentMethod method = PaymentMethod.EWallet) =>
        new() { BookingId = bookingId, Method = method };

    [Fact]
    public async Task Create_ThanhToanTruoc_TamGiuVaBatDauTimTasker()
    {
        var customer = Db.Customer();
        var booking = AwaitingBooking(customer);

        var payment = await CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id));

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(payment.Id, p.Id);
        Assert.Equal(PaymentStatus.Held, p.Status);
        Assert.Equal(250_000m, p.Amount);
        Assert.StartsWith("SIM", p.GatewayTransactionId);
        var b = await fresh.Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, b.Status);
        Assert.Equal(0, b.CurrentWave);
        Assert.Equal(AuditService.PaymentHeld, (await fresh.AuditLogs.SingleAsync()).Action);
        Assert.Equal("Thanh toán thành công", (await fresh.Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task Create_TheKhacPhuongThucDaChon_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = AwaitingBooking(customer, PaymentMethod.EWallet);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id, PaymentMethod.Card)));
        Assert.Equal("Phương thức thanh toán không khớp đơn.", ex.Message);
    }

    [Fact]
    public async Task Create_TienMat_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = AwaitingBooking(customer);
        await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id, PaymentMethod.Cash)));
    }

    [Fact]
    public async Task Create_QuaHanThanhToan_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.AwaitingPayment, configure: b => b.PaymentDueAt = DateTime.UtcNow.AddMinutes(-1));
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id)));
        Assert.Equal("Đơn đã quá hạn thanh toán.", ex.Message);
    }

    [Fact]
    public async Task Create_DaThanhToan_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Pending);
        Db.HeldPayment(booking);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id)));
        Assert.Equal("Đơn đã được thanh toán.", ex.Message);
    }

    [Fact]
    public async Task Create_KhongOTrangThaiChoThanhToan_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Cancelled);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id)));
        Assert.Equal("Đơn không ở trạng thái chờ thanh toán.", ex.Message);
    }

    [Fact]
    public async Task Create_DonCuaNguoiKhac_403_VaKhongTonTai_404()
    {
        var booking = AwaitingBooking(Db.Customer());
        var forbidden = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(Db.Customer("B").Id, Pay(booking.Id)));
        Assert.Equal(403, forbidden.StatusCode);
        var notFound = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(1, Pay(99999)));
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task TraNo_DonTienMatChuaThanhToan_GiaiNganNgayChoTasker()
    {
        var customer = Db.Customer(configure: u => u.OutstandingAmount = 200_000m);
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(customer, Db.Service(price: 200_000m), BookingStatus.PaymentOverdue, tasker, PaymentMethod.Cash);

        await CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id, PaymentMethod.Card));

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Released, p.Status);
        Assert.Equal(40_000m, p.CommissionAmount);
        Assert.Equal(160_000m, p.TaskerAmount);
        Assert.Equal(0m, (await fresh.Users.SingleAsync(u => u.Id == customer.Id)).OutstandingAmount);
        Assert.Equal(160_000m, (await fresh.TaskerProfiles.SingleAsync()).WalletBalance);
        Assert.Equal(BookingStatus.Paid, (await fresh.Bookings.SingleAsync()).Status);
        Assert.Equal(AuditService.DebtPaid, (await fresh.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task TraNo_PhiHuyDonTienMat_ChuyenDuChoTaskerKhongChietKhau()
    {
        var customer = Db.Customer(configure: u => u.OutstandingAmount = 40_000m);
        var tasker = Db.Tasker(wallet: 0m);
        var booking = Db.Booking(customer, Db.Service(price: 200_000m), BookingStatus.Cancelled, tasker, PaymentMethod.Cash,
            b => b.CancellationFee = 40_000m);

        await CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id));

        var fresh = Fresh();
        var p = await fresh.Payments.SingleAsync();
        Assert.Equal(40_000m, p.Amount);
        Assert.Equal(0m, p.CommissionAmount);
        Assert.Equal(40_000m, p.TaskerAmount);
        Assert.Equal(BookingStatus.Cancelled, (await fresh.Bookings.SingleAsync()).Status);  // đơn vẫn là đã hủy
        Assert.Equal(WalletTransactionType.CancellationFee, (await fresh.WalletTransactions.SingleAsync()).Type);
        Assert.Equal(0m, (await fresh.Users.SingleAsync(u => u.Id == customer.Id)).OutstandingAmount);
    }

    [Fact]
    public async Task TraNo_BangTienMat_BiTuChoi()
    {
        var customer = Db.Customer();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.PaymentOverdue, Db.Tasker(), PaymentMethod.Cash);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePaymentService().CreateAsync(customer.Id, Pay(booking.Id, PaymentMethod.Cash)));
        Assert.Equal("Vui lòng thanh toán khoản nợ bằng ví điện tử hoặc thẻ.", ex.Message);
    }

    [Fact]
    public void ToResponse_AnhXaDuTruong()
    {
        var p = new Payment
        {
            Id = 3, BookingId = 7, Amount = 100_000m, CommissionAmount = 20_000m, TaskerAmount = 80_000m,
            Method = PaymentMethod.Card, Status = PaymentStatus.Released, RefundedAmount = 0m,
        };
        var r = PaymentService.ToResponse(p, "Giặt ủi");
        Assert.Equal(3, r.Id);
        Assert.Equal(7, r.BookingId);
        Assert.Equal("Giặt ủi", r.ServiceCategoryName);
        Assert.Equal(80_000m, r.TaskerAmount);
        Assert.Equal(PaymentStatus.Released, r.Status);
    }
}
