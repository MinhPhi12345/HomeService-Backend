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

namespace HomeService.Api.Tests.Services;

/// <summary>EP10 – Mã giảm giá (FR-32, BR-V01 → BR-V05): tính tiền giảm, điều kiện mã, giữ / trả lại lượt, dòng tiền.</summary>
public class VoucherServiceTests : TestBase
{
    private Voucher Campaign(string code = "GIAM30", VoucherDiscountType type = VoucherDiscountType.FixedAmount,
        decimal value = 30_000, Action<Voucher>? configure = null)
    {
        var v = new Voucher
        {
            Code = code, Name = "Khuyến mãi tháng 11", DiscountType = type, DiscountValue = value,
            MaxDiscount = type == VoucherDiscountType.Percent ? 50_000 : null,
            StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(10),
            PerCustomerLimit = 1, IsPublic = true,
        };
        configure?.Invoke(v);
        Db.Vouchers.Add(v);
        Db.SaveChanges();
        return v;
    }

    private static CreateBookingRequest Request(int serviceId, string? code, PaymentMethod method = PaymentMethod.EWallet) => new()
    {
        ServiceCategoryId = serviceId,
        ScheduledTime = DateTime.UtcNow.AddHours(3),
        Address = "12 Nguyễn Huệ, Quận 1",
        Latitude = Seed.BenThanhLat,
        Longitude = Seed.BenThanhLng,
        PaymentMethod = method,
        CustomerPresent = method == PaymentMethod.Cash,
        VoucherCode = code,
    };

    // ---------- Tính tiền giảm (BR-V02) ----------

    [Theory]
    [InlineData(VoucherDiscountType.Percent, 20.0, 50_000.0, 150_000.0, 30_000.0)]   // 20% của 150.000
    [InlineData(VoucherDiscountType.Percent, 20.0, 50_000.0, 400_000.0, 50_000.0)]   // vượt mức tối đa → 50.000
    [InlineData(VoucherDiscountType.FixedAmount, 30_000.0, null, 150_000.0, 30_000.0)]
    [InlineData(VoucherDiscountType.FixedAmount, 200_000.0, null, 150_000.0, 140_000.0)] // khách vẫn trả tối thiểu 10.000
    [InlineData(VoucherDiscountType.Percent, 15.0, 100_000.0, 99_999.0, 15_000.0)]    // làm tròn đến đồng
    public void CalculateDiscount_TheoLoaiMucToiDaVaSoTienToiThieu(VoucherDiscountType type, double value, double? max,
        double order, double expected)
    {
        Assert.Equal((decimal)expected, VoucherService.CalculateDiscount(type, (decimal)value, (decimal?)max, (decimal)order, 10_000));
    }

    // ---------- Điều kiện mã (BR-V01) ----------

    [Fact]
    public async Task Validate_MaHopLe_KhongPhanBietHoaThuong()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        Campaign();
        var (voucher, discount) = await CreateVouchers().ValidateAsync(customer.Id, "  giam30 ", service.Id, 150_000, PaymentMethod.EWallet);
        Assert.Equal("GIAM30", voucher.Code);
        Assert.Equal(30_000m, discount);
    }

    [Theory]
    [InlineData("notfound", "Mã giảm giá không tồn tại.")]
    [InlineData("paused", "Chương trình khuyến mãi này đang tạm dừng.")]
    [InlineData("future", "Mã giảm giá chưa đến thời gian áp dụng.")]
    [InlineData("expired", "Mã giảm giá đã hết hạn.")]
    [InlineData("cash", "Mã giảm giá chỉ áp dụng cho đơn thanh toán trước (ví điện tử, thẻ).")]
    [InlineData("service", "Mã giảm giá không áp dụng cho dịch vụ này.")]
    [InlineData("minorder", "Đơn tối thiểu 200.000 đ mới dùng được mã này.")]
    [InlineData("soldout", "Mã giảm giá đã hết lượt sử dụng.")]
    [InlineData("newonly", "Mã này chỉ dành cho khách hàng chưa có đơn hoàn tất.")]
    [InlineData("othergift", "Mã giảm giá không tồn tại.")]
    public async Task Validate_MaKhongHopLe_400KemLyDo(string scenario, string message)
    {
        var customer = Db.Customer();
        var other = Db.Customer("Khách khác");
        var service = Db.Service(price: 150_000m);
        var method = PaymentMethod.EWallet;
        switch (scenario)
        {
            case "paused": Campaign(configure: v => v.IsActive = false); break;
            case "future": Campaign(configure: v => v.StartsAt = DateTime.UtcNow.AddDays(1)); break;
            case "expired": Campaign(configure: v => v.EndsAt = DateTime.UtcNow.AddMinutes(-1)); break;
            case "cash": Campaign(); method = PaymentMethod.Cash; break;
            case "service": Campaign(configure: v => v.ServiceCategoryIds = (service.Id + 100).ToString()); break;
            case "minorder": Campaign(configure: v => v.MinOrderValue = 200_000); break;
            case "soldout": Campaign(configure: v => { v.TotalLimit = 5; v.UsedCount = 5; }); break;
            case "newonly":
                Campaign(configure: v => v.Audience = VoucherAudience.NewCustomers);
                Db.Booking(customer, service, BookingStatus.Paid);
                break;
            case "othergift": Campaign(configure: v => v.AssignedCustomerId = other.Id); break;
        }
        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            CreateVouchers().ValidateAsync(customer.Id, "GIAM30", service.Id, 150_000, method));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(message, ex.Message);
    }

    // ---------- Áp mã khi tạo đơn, giữ / trả lại lượt (BR-V04) ----------

    [Fact]
    public async Task TaoDon_CoMa_ChotGiaGocTienGiamVaGiuLuot()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        var voucher = Campaign(configure: v => v.TotalLimit = 10);

        var booking = await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, "giam30"));

        var saved = await Fresh().Bookings.SingleAsync();
        Assert.Equal(150_000m, saved.TotalPrice);       // giá gốc
        Assert.Equal(30_000m, saved.DiscountAmount);
        Assert.Equal(120_000m, saved.PayableAmount);    // khách trả
        Assert.Equal("GIAM30", saved.VoucherCode);
        Assert.Equal(1, (await Fresh().Vouchers.SingleAsync()).UsedCount);
        var redemption = await Fresh().VoucherRedemptions.SingleAsync();
        Assert.Equal((voucher.Id, booking.Id, VoucherRedemptionStatus.Applied), (redemption.VoucherId, redemption.BookingId, redemption.Status));
        Assert.Contains("120.000 đ", (await Fresh().Notifications.SingleAsync()).Message);

        // Thanh toán trước: số tiền tạm giữ = số tiền khách trả
        var payment = await CreatePaymentService().CreateAsync(customer.Id, new CreatePaymentRequest { BookingId = booking.Id, Method = PaymentMethod.EWallet });
        Assert.Equal(120_000m, payment.Amount);
    }

    [Fact]
    public async Task TaoDon_MaHetLuot_KhongTaoDon()
    {
        var service = Db.Service(price: 150_000m);
        Campaign(configure: v => v.TotalLimit = 1);
        await CreateBookingService().CreateAsync(Db.Customer().Id, Request(service.Id, "GIAM30"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(Db.Customer("Khách 2").Id, Request(service.Id, "GIAM30")));
        Assert.Equal("Mã giảm giá đã hết lượt sử dụng.", ex.Message);
        Assert.Equal(1, await Fresh().Bookings.CountAsync());
    }

    [Fact]
    public async Task TaoDon_DaDungHetLuotCuaMinh_400()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        Campaign();
        await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, "GIAM30"));
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(customer.Id, Request(service.Id, "GIAM30")));
        Assert.Equal("Bạn đã dùng hết lượt của mã này.", ex.Message);
    }

    [Fact]
    public async Task HuyDonMienPhi_TraLaiLuot_DungLaiDuoc()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        Campaign();
        var booking = await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, "GIAM30"));

        var tracked = await Db.Bookings.Include(b => b.Payment).FirstAsync(b => b.Id == booking.Id);
        await CreateCancellation().CancelAsync(tracked, "Khách đổi ý");

        Assert.Equal(VoucherRedemptionStatus.Released, (await Fresh().VoucherRedemptions.SingleAsync()).Status);
        Assert.Equal(0, (await Fresh().Vouchers.SingleAsync()).UsedCount);
        await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, "GIAM30")); // dùng lại được
    }

    [Fact]
    public async Task QuaHanThanhToan_TuHuyVaTraLaiLuot()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        Campaign();
        var booking = await CreateBookingService().CreateAsync(customer.Id, Request(service.Id, "GIAM30"));
        await Db.Bookings.Where(b => b.Id == booking.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.PaymentDueAt, DateTime.UtcNow.AddMinutes(-1)));

        await CreateJobs().ExpireUnpaidBookingsAsync();

        Assert.Equal(BookingStatus.Cancelled, (await Fresh().Bookings.SingleAsync()).Status);
        Assert.Equal(VoucherRedemptionStatus.Released, (await Fresh().VoucherRedemptions.SingleAsync()).Status);
        Assert.Contains("Mã giảm giá đã được trả lại", (await Fresh().Notifications.OrderBy(n => n.Id).LastAsync()).Message);
    }

    // ---------- Dòng tiền: Tasker theo giá gốc, nền tảng bù (BR-V03) ----------

    [Fact]
    public void SplitOnline_CoKhuyenMai_TaskerTinhTheoGiaGoc()
    {
        // Giá gốc 150.000, khách trả 120.000 (giảm 30.000): Tasker 120.000, chiết khấu 30.000
        Assert.Equal((30_000m, 120_000m), SettlementService.SplitOnline(120_000, 0, 0.2m, 30_000));
        // Hoàn một phần 50.000: giá trị tính cho Tasker = 120.000 + 30.000 − 50.000 = 100.000
        Assert.Equal((20_000m, 80_000m), SettlementService.SplitOnline(120_000, 50_000, 0.2m, 30_000));
    }

    [Fact]
    public async Task GiaiNgan_CoMa_TaskerNhanDuNenTangBu_DanhDauDaDung()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker(wallet: 0);
        var service = Db.Service(price: 150_000m);
        var voucher = Campaign();
        var booking = Db.Booking(customer, service, BookingStatus.Completed, tasker, configure: b =>
        {
            b.VoucherId = voucher.Id; b.VoucherCode = voucher.Code; b.DiscountAmount = 30_000;
        });
        Db.VoucherRedemptions.Add(new VoucherRedemption { VoucherId = voucher.Id, CustomerId = customer.Id, BookingId = booking.Id, DiscountAmount = 30_000 });
        Db.SaveChanges();
        Db.HeldPayment(booking, 120_000);

        var tracked = await Db.Bookings.Include(b => b.Payment).FirstAsync(b => b.Id == booking.Id);
        Assert.True(await CreateSettlement().ReleaseOnlineAsync(tracked, isAuto: false));
        await Db.SaveChangesAsync();

        var payment = await Fresh().Payments.SingleAsync();
        Assert.Equal((120_000m, 30_000m, 30_000m, 120_000m), (payment.Amount, payment.PromoSubsidy, payment.CommissionAmount, payment.TaskerAmount));
        Assert.Equal(120_000m, (await Fresh().TaskerProfiles.SingleAsync()).WalletBalance);
        Assert.Equal(VoucherRedemptionStatus.Used, (await Fresh().VoucherRedemptions.SingleAsync()).Status);
        Assert.Contains("khuyến mãi nền tảng bù 30000", (await Fresh().AuditLogs.SingleAsync(a => a.Action == AuditService.Released)).Detail);
    }

    [Fact]
    public async Task PhiHuy_TinhTrenSoTienKhachThucTra()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var service = Db.Service(price: 150_000m);
        var booking = Db.Booking(customer, service, BookingStatus.Accepted, tasker, configure: b =>
        {
            b.ScheduledTime = DateTime.UtcNow.AddMinutes(90); b.DiscountAmount = 50_000;
        });
        var (fee, _) = await CreateCancellation().CalculateFeeAsync(booking);
        Assert.Equal(20_000m, fee); // 20% × 100.000 (không phải 150.000)
    }
}

/// <summary>EP10 – Quà tặng tự động (FR-33): chào mừng, khách thân thiết, xin lỗi.</summary>
public class GiftTests : TestBase
{
    [Fact]
    public async Task QuaChaoMung_KhachHangXacThucEmail_MotLanDuyNhat()
    {
        var customer = Db.Customer();
        var v1 = await CreateVouchers().GrantWelcomeAsync(customer);
        await Db.SaveChangesAsync();
        var v2 = await CreateVouchers().GrantWelcomeAsync(customer);

        Assert.NotNull(v1);
        Assert.Null(v2);
        var gift = await Fresh().Vouchers.SingleAsync();
        Assert.StartsWith("QUA", gift.Code);
        Assert.Equal((customer.Id, VoucherSource.Welcome, 1, false), (gift.AssignedCustomerId!.Value, gift.Source, gift.TotalLimit!.Value, gift.IsPublic));
        Assert.Equal((VoucherDiscountType.Percent, 20m, 50_000m, 100_000m), (gift.DiscountType, gift.DiscountValue, gift.MaxDiscount!.Value, gift.MinOrderValue));
        Assert.InRange(gift.EndsAt, DateTime.UtcNow.AddDays(29.9), DateTime.UtcNow.AddDays(30.1));
        Assert.Equal("Bạn nhận được quà chào mừng", (await Fresh().Notifications.SingleAsync()).Title);
    }

    [Fact]
    public async Task QuaChaoMung_TaskerKhongNhan()
    {
        Assert.Null(await CreateVouchers().GrantWelcomeAsync(Db.Tasker()));
    }

    [Fact]
    public async Task QuaChaoMung_TatTrongCaiDat_KhongTang()
    {
        var rule = VoucherService.DefaultRule(VoucherSource.Welcome);
        rule.IsEnabled = false;
        Db.GiftRules.Add(rule);
        Db.SaveChanges();
        Assert.Null(await CreateVouchers().GrantWelcomeAsync(Db.Customer()));
    }

    [Fact]
    public async Task QuaThanThiet_Don5HoanTat_TangMotLan()
    {
        var customer = Db.Customer();
        var service = Db.Service();
        for (var i = 0; i < 4; i++) Db.Booking(customer, service, BookingStatus.Paid);
        var fifth = Db.Booking(customer, service, BookingStatus.Completed);

        var gift = await CreateVouchers().GrantLoyaltyIfDueAsync(customer.Id, fifth.Id);
        await Db.SaveChangesAsync();
        Assert.NotNull(gift);
        Assert.Equal((VoucherSource.Loyalty, fifth.Id, 30_000m), (gift!.Source, gift.SourceBookingId!.Value, gift.DiscountValue));
        Assert.Null(await CreateVouchers().GrantLoyaltyIfDueAsync(customer.Id, fifth.Id)); // không tặng trùng

        fifth.Status = BookingStatus.Paid;
        Db.SaveChanges();
        var sixth = Db.Booking(customer, service, BookingStatus.Completed);
        Assert.Null(await CreateVouchers().GrantLoyaltyIfDueAsync(customer.Id, sixth.Id));
    }

    [Fact]
    public async Task QuaXinLoi_TaskerKhongDen_TangChoKhach()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Accepted, tasker, configure: b => b.ScheduledTime = DateTime.UtcNow.AddMinutes(-40));

        await CreateJobs().DetectNoShowAsync();

        var gift = await Fresh().Vouchers.SingleAsync();
        Assert.Equal((VoucherSource.Apology, booking.Id, customer.Id), (gift.Source, gift.SourceBookingId!.Value, gift.AssignedCustomerId!.Value));
        Assert.Contains(await Fresh().Notifications.Where(n => n.UserId == customer.Id).Select(n => n.Title).ToListAsync(),
            t => t == "Bạn nhận được quà xin lỗi");
    }

    [Fact]
    public async Task QuaXinLoi_KhieuNaiDuocChapNhan()
    {
        var customer = Db.Customer();
        var tasker = Db.Tasker();
        var admin = Db.Admin();
        var booking = Db.Booking(customer, Db.Service(), BookingStatus.Disputed, tasker);
        Db.HeldPayment(booking);
        var dispute = Db.Dispute(booking);

        await CreateDisputeService().ResolveAsync(dispute.Id, admin.Id, new ResolveDisputeRequest
        {
            Resolution = DisputeResolution.FullRefund, AdminNote = "Ảnh cho thấy phòng chưa được dọn sạch",
        });

        Assert.Equal(VoucherSource.Apology, (await Fresh().Vouchers.SingleAsync()).Source);
    }
}

/// <summary>EP10 – "Ưu đãi của tôi" và trang Khuyến mãi của Admin.</summary>
public class PromotionServiceTests : TestBase
{
    private static SaveVoucherRequest Save(string code = "THANG11", VoucherDiscountType type = VoucherDiscountType.Percent,
        decimal value = 10, decimal? max = 30_000) => new()
    {
        Code = code, Name = "Khuyến mãi tháng 11", DiscountType = type, DiscountValue = value, MaxDiscount = max,
        MinOrderValue = 100_000, StartsAt = DateTime.UtcNow.AddMinutes(-5), EndsAt = DateTime.UtcNow.AddDays(30),
        TotalLimit = 100, PerCustomerLimit = 2, IsPublic = true,
    };

    [Fact]
    public async Task TaoChuongTrinh_ChuanHoaMa_GhiNhatKy()
    {
        var admin = Db.Admin();
        var id = await CreatePromotions().CreateCampaignAsync(Save(code: "thang11"), admin.Id);
        var v = await Fresh().Vouchers.SingleAsync(x => x.Id == id);
        Assert.Equal(("THANG11", VoucherSource.Campaign, 30_000m), (v.Code, v.Source, v.MaxDiscount!.Value));
        Assert.Equal(AuditService.VoucherCreated, (await Fresh().AuditLogs.SingleAsync()).Action);
    }

    [Theory]
    [InlineData("TRUNG", "Mã giảm giá đã tồn tại.")]
    [InlineData("QUA123", "Mã bắt đầu bằng \"QUA\" được dành cho quà tặng tự động.")]
    public async Task TaoChuongTrinh_MaKhongHopLe(string code, string message)
    {
        var admin = Db.Admin();
        await CreatePromotions().CreateCampaignAsync(Save(code: "TRUNG"), admin.Id);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePromotions().CreateCampaignAsync(Save(code: code), admin.Id));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task TaoChuongTrinh_GiamTheoPhanTramThieuMucToiDa_400()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePromotions().CreateCampaignAsync(Save(max: null), Db.Admin().Id));
        Assert.Equal("Vui lòng nhập mức giảm tối đa cho mã giảm theo %.", ex.Message);
    }

    [Fact]
    public async Task TamDung_KhachKhongDungDuoc_TiepTucThiDungDuoc()
    {
        var admin = Db.Admin();
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        var id = await CreatePromotions().CreateCampaignAsync(Save(), admin.Id);

        await CreatePromotions().ChangeStatusAsync(id, "pause");
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreatePromotions().CheckAsync(customer.Id,
            new CheckVoucherRequest { Code = "THANG11", ServiceCategoryId = service.Id }));
        Assert.Equal("Chương trình khuyến mãi này đang tạm dừng.", ex.Message);

        await CreatePromotions().ChangeStatusAsync(id, "resume");
        var ok = await CreatePromotions().CheckAsync(customer.Id, new CheckVoucherRequest { Code = "THANG11", ServiceCategoryId = service.Id, Quantity = 2 });
        Assert.Equal((300_000m, 30_000m, 270_000m), (ok.OriginalPrice, ok.DiscountAmount, ok.PayableAmount)); // 10% của 300.000, tối đa 30.000
    }

    [Fact]
    public async Task UuDaiCuaToi_GomQuaRiengVaChuongTrinhCongKhai()
    {
        var customer = Db.Customer();
        var other = Db.Customer("Khách khác");
        var admin = Db.Admin();
        await CreatePromotions().CreateCampaignAsync(Save(), admin.Id);
        var hidden = Save(code: "BIMAT");
        hidden.IsPublic = false;
        await CreatePromotions().CreateCampaignAsync(hidden, admin.Id);
        await CreateVouchers().GrantWelcomeAsync(customer);
        await CreateVouchers().GrantWelcomeAsync(other);
        await Db.SaveChangesAsync();

        var mine = await CreatePromotions().GetMineAsync(customer.Id);
        Assert.Equal(2, mine.Count);
        Assert.True(mine[0].IsGift);                 // quà riêng đứng trước
        Assert.Equal("THANG11", mine[1].Code);
        Assert.Equal(2, mine[1].RemainingUses);
    }

    [Fact]
    public async Task ThongKe_ChiTinhLuotDaDung()
    {
        var customer = Db.Customer();
        var service = Db.Service();
        var id = await CreatePromotions().CreateCampaignAsync(Save(), Db.Admin().Id);
        var b1 = Db.Booking(customer, service, BookingStatus.Paid);
        var b2 = Db.Booking(customer, service, BookingStatus.Pending);
        Db.VoucherRedemptions.AddRange(
            new VoucherRedemption { VoucherId = id, CustomerId = customer.Id, BookingId = b1.Id, DiscountAmount = 15_000, Status = VoucherRedemptionStatus.Used },
            new VoucherRedemption { VoucherId = id, CustomerId = customer.Id, BookingId = b2.Id, DiscountAmount = 15_000 });
        Db.SaveChanges();

        var summary = await CreatePromotions().GetSummaryAsync();
        Assert.Equal((1, 1, 15_000m, 15_000m), (summary.ActiveCampaigns, summary.CampaignUses, summary.CampaignDiscount, summary.TotalDiscount));
        var row = (await CreatePromotions().GetCampaignsAsync()).Single();
        Assert.Equal(("Active", 1, 15_000m), (row.Status, row.CompletedCount, row.TotalDiscount));
    }

    [Fact]
    public async Task CaiDatQua_LuuVaDungChoLanTangSau()
    {
        var admin = Db.Admin();
        var saved = await CreatePromotions().SaveGiftRuleAsync(VoucherSource.Loyalty, new SaveGiftRuleRequest
        {
            IsEnabled = true, DiscountType = VoucherDiscountType.FixedAmount, DiscountValue = 40_000, MinOrderValue = 0,
            ValidDays = 15, EveryBookings = 3,
        }, admin.Id);
        Assert.Equal("Giảm 40.000 đ", saved.Summary);

        var customer = Db.Customer();
        var service = Db.Service();
        for (var i = 0; i < 2; i++) Db.Booking(customer, service, BookingStatus.Paid);
        var third = Db.Booking(customer, service, BookingStatus.Completed);
        var gift = await CreateVouchers().GrantLoyaltyIfDueAsync(customer.Id, third.Id);
        Assert.Equal(40_000m, gift!.DiscountValue);
        Assert.Equal(AuditService.GiftRuleUpdated, (await Fresh().AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task VouchersController_KhachKiemTraMa()
    {
        var customer = Db.Customer();
        var service = Db.Service(price: 150_000m);
        await CreatePromotions().CreateCampaignAsync(Save(), Db.Admin().Id);
        var result = (await new VouchersController(CreatePromotions()).AsUser(customer.Id, UserRole.Customer)
            .Check(new CheckVoucherRequest { Code = "thang11", ServiceCategoryId = service.Id })).OkValue();
        Assert.Equal("Áp dụng mã THANG11: giảm 15.000 đ.", result.Message);
    }
}
