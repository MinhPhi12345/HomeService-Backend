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

/// <summary>
/// SRS v1.8 – số lượng theo từng dịch vụ (BR-B01), thời lượng & chống trùng lịch (BR-B02),
/// hạng mục và ảnh theo từng hạng mục (BR-B05), chi tiết dịch vụ (US10).
/// </summary>
public class ServiceQuantityAndPhotoTests : TestBase
{
    private ServiceCategory Cleaning() => Db.Service(configure: s => { s.MinQuantity = 2; s.MaxQuantity = 4; s.MinutesPerUnit = 60; });

    private ServiceCategory Repair() => Db.Service("Sửa điện nước", 200_000m, unit: "hạng mục",
        configure: s => { s.MinQuantity = 1; s.MaxQuantity = 5; s.MinutesPerUnit = 60; s.RequiresItemList = true; });

    private static CreateBookingRequest Req(int serviceId, int? qty, List<string>? items = null) => new()
    {
        ServiceCategoryId = serviceId, ScheduledTime = DateTime.UtcNow.AddHours(4), Address = "12 Nguyễn Huệ, Quận 1",
        Latitude = Seed.BenThanhLat, Longitude = Seed.BenThanhLng, Quantity = qty, Items = items,
    };

    // ---------- Số lượng & thời lượng ----------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Create_SoLuongNgoaiKhoangCuaDichVu_400(int qty)
    {
        var s = Cleaning();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(Db.Customer().Id, Req(s.Id, qty)));
        Assert.Equal("Số lượng của dịch vụ này từ 2 đến 4 giờ.", ex.Message);
    }

    [Fact]
    public async Task Create_KhongGuiSoLuong_LayMucToiThieu_ChotThoiLuong()
    {
        var s = Cleaning();
        var b = await CreateBookingService().CreateAsync(Db.Customer().Id, Req(s.Id, null));
        Assert.Equal(2, b.Quantity);
        Assert.Equal(300_000m, b.TotalPrice);
        Assert.Equal(120, b.EstimatedMinutes);

        var b4 = await CreateBookingService().CreateAsync(Db.Customer("Khách 2").Id, Req(s.Id, 4));
        Assert.Equal(240, b4.EstimatedMinutes);
    }

    [Fact]
    public async Task Create_DichVuCoDinh1Buoi_ThongBaoRieng()
    {
        var s = Db.Service("Nấu ăn gia đình", 180_000m, unit: "buổi", configure: x => { x.MinQuantity = 1; x.MaxQuantity = 1; x.MinutesPerUnit = 120; });
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(Db.Customer().Id, Req(s.Id, 2)));
        Assert.Equal("Dịch vụ này chỉ đặt 1 buổi mỗi đơn.", ex.Message);
    }

    // ---------- Hạng mục ----------

    [Fact]
    public async Task Create_DichVuTheoHangMuc_ThieuHangMuc_400_DuThiLuu()
    {
        var s = Repair();
        var customer = Db.Customer();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(customer.Id, Req(s.Id, 2, new() { "Vòi bếp rỉ nước" })));
        Assert.Equal("Vui lòng ghi rõ 2 hạng mục cần làm (mỗi dòng một hạng mục).", ex.Message);
        ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().CreateAsync(customer.Id, Req(s.Id, 2, new() { "Vòi bếp rỉ nước", "ab" })));
        Assert.Equal("Mỗi hạng mục cần mô tả từ 3 đến 200 ký tự.", ex.Message);

        var b = await CreateBookingService().CreateAsync(customer.Id, Req(s.Id, 2, new() { "  Vòi bếp rỉ nước ", "Ổ điện phòng ngủ" }));
        var items = await Fresh().BookingItems.Where(i => i.BookingId == b.Id).OrderBy(i => i.ItemNo).ToListAsync();
        Assert.Equal(new[] { "Vòi bếp rỉ nước", "Ổ điện phòng ngủ" }, items.Select(i => i.Description));
        Assert.Equal(new[] { 1, 2 }, items.Select(i => i.ItemNo));
    }

    [Fact]
    public async Task Create_DichVuThuong_BoQuaHangMuc()
    {
        var s = Cleaning();
        var b = await CreateBookingService().CreateAsync(Db.Customer().Id, Req(s.Id, 2, new() { "Không cần" }));
        Assert.Empty(await Fresh().BookingItems.Where(i => i.BookingId == b.Id).ToListAsync());
    }

    // ---------- Ảnh theo hạng mục ----------

    private (Booking Booking, User Tasker) RepairJob(BookingStatus status = BookingStatus.Accepted)
    {
        var tasker = Db.Tasker(skills: "Sửa điện nước");
        var b = Db.Booking(Db.Customer(), Repair(), status, tasker, configure: x =>
        {
            x.Quantity = 2;
            x.Items.Add(new BookingItem { ItemNo = 1, Description = "Vòi bếp rỉ nước" });
            x.Items.Add(new BookingItem { ItemNo = 2, Description = "Ổ điện phòng ngủ" });
        });
        return (b, tasker);
    }

    private static UpdateBookingStatusRequest Start(params (string Url, int? Item)[] photos) => new()
    {
        Status = BookingStatus.InProgress,
        Photos = photos.Select(p => new BookingPhotoInput { Url = p.Url, ItemNo = p.Item, Note = "  trước khi sửa " }).ToList(),
    };

    [Fact]
    public async Task BatDau_ThieuAnhMotHangMuc_400()
    {
        var (b, tasker) = RepairJob();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id,
            Start(("/uploads/202611/a.png", 1))));
        Assert.Equal("Vui lòng chụp ít nhất 1 ảnh cho hạng mục 2: Ổ điện phòng ngủ.", ex.Message);

        ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id,
            new UpdateBookingStatusRequest { Status = BookingStatus.InProgress, PhotoUrl = "/uploads/202611/cu.png" }));
        Assert.Equal("Vui lòng chụp ảnh theo từng hạng mục của đơn.", ex.Message); // app cũ 1 ảnh không đủ cho đơn theo hạng mục
    }

    [Fact]
    public async Task BatDau_QuaBaAnhMotHangMuc_400()
    {
        var (b, tasker) = RepairJob();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id,
            Start(("/uploads/1.png", 1), ("/uploads/2.png", 1), ("/uploads/3.png", 1), ("/uploads/4.png", 1), ("/uploads/5.png", 2))));
        Assert.Equal("Mỗi hạng mục tối đa 3 ảnh.", ex.Message);
    }

    [Fact]
    public async Task BatDau_HoanThanh_LuuAnhTheoHangMuc_TraVeTruocSau()
    {
        var (b, tasker) = RepairJob();
        await CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id,
            Start(("http://localhost:8080/uploads/202611/a1.png", 1), ("/uploads/202611/a2.png", 1), ("/uploads/202611/b1.png", 2)));
        await CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id, new UpdateBookingStatusRequest
        {
            Status = BookingStatus.Completed,
            Photos = new() { new() { Url = "/uploads/202611/a9.png", ItemNo = 1 }, new() { Url = "/uploads/202611/b9.png", ItemNo = 2 } },
        });

        var fresh = Fresh();
        var saved = await fresh.Bookings.Include(x => x.Photos).Include(x => x.Items).Include(x => x.ServiceCategory)
            .Include(x => x.Reviews).SingleAsync(x => x.Id == b.Id);
        Assert.Equal(BookingStatus.Completed, saved.Status);
        Assert.Equal("/uploads/202611/a1.png", saved.CheckInPhotoUrl);  // ảnh đầu tiên giữ cho FE cũ
        Assert.Equal("/uploads/202611/a9.png", saved.CheckOutPhotoUrl);
        Assert.Equal(5, saved.Photos.Count);
        Assert.Equal("trước khi sửa", saved.Photos.First(p => p.Stage == BookingPhotoStage.CheckIn).Note);

        var dto = BookingMapper.ToResponse(saved, DateTime.UtcNow, b.CustomerId);
        Assert.True(dto.RequiresItemList);
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Photos.Count(p => p.Stage == BookingPhotoStage.CheckIn && p.ItemNo == 1));
        Assert.Single(dto.Photos, p => p.Stage == BookingPhotoStage.CheckOut && p.ItemNo == 2);
        Assert.Equal("hạng mục", dto.ServiceUnit);
    }

    [Fact]
    public async Task DichVuThuong_NhieuAnh_ToiDa5_VaVanNhanPhotoUrlCu()
    {
        var tasker = Db.Tasker();
        var b = Db.Booking(Db.Customer(), Cleaning(), BookingStatus.Accepted, tasker);
        var six = Enumerable.Range(1, 6).Select(i => new BookingPhotoInput { Url = $"/uploads/x{i}.png", ItemNo = 3 }).ToList();
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id,
            new UpdateBookingStatusRequest { Status = BookingStatus.InProgress, Photos = six }));
        Assert.Equal("Tối đa 5 ảnh mỗi lần.", ex.Message);

        await CreateBookingService().UpdateStatusAsync(b.Id, tasker.Id,
            new UpdateBookingStatusRequest { Status = BookingStatus.InProgress, PhotoUrl = "/uploads/cu.png" });
        var photo = await Fresh().BookingPhotos.SingleAsync();
        Assert.Null(photo.ItemNo);
        Assert.Equal("/uploads/cu.png", photo.Url);
    }

    [Fact]
    public void Mapper_DonCu_KhongCoBangAnh_LayTuCheckInCheckOut()
    {
        var b = new Booking { CheckInPhotoUrl = "/uploads/in.png", CheckOutPhotoUrl = "/uploads/out.png", ServiceCategory = new ServiceCategory() };
        var photos = BookingMapper.PhotosOf(b);
        Assert.Equal(new[] { BookingPhotoStage.CheckIn, BookingPhotoStage.CheckOut }, photos.Select(p => p.Stage));
        Assert.All(photos, p => Assert.Null(p.ItemNo));
    }

    // ---------- Chống trùng lịch theo thời lượng (BR-B02) ----------

    [Theory]
    [InlineData(180, true)]   // đơn 4 giờ lúc 8:00 → bận đến 12:30, đơn 11:00 bị trùng
    [InlineData(270, false)]  // 12:30 → không trùng
    [InlineData(-149, true)]  // đơn 2 giờ lúc 5:31 → xong 7:31 + 30 phút di chuyển = 8:01 → trùng
    [InlineData(-150, false)] // đơn 2 giờ lúc 5:30 → 8:00 vừa kịp
    public void Overlaps_TinhTheoThoiLuongVaDiChuyen(int offsetMinutes, bool expected)
    {
        var eight = new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc);
        var minutes = offsetMinutes < 0 ? 120 : 60;
        Assert.Equal(expected, TaskerSchedule.Overlaps(eight, 240, eight.AddMinutes(offsetMinutes), minutes, 30));
    }

    [Fact]
    public async Task NhanDon_TrungLichTheoThoiLuong_409_KhongTrungThiDuoc()
    {
        var tasker = Db.Tasker();
        var service = Cleaning();
        var start = DateTime.UtcNow.AddDays(1);
        Db.Booking(Db.Customer(), service, BookingStatus.Accepted, tasker, configure: b => { b.ScheduledTime = start; b.EstimatedMinutes = 240; });

        var clash = Db.Booking(Db.Customer("Khách 2"), service, BookingStatus.Matched, configure: b => b.ScheduledTime = start.AddHours(3));
        Db.Offer(clash, tasker);
        var ex = await Assert.ThrowsAsync<ApiException>(() => CreateOfferService().AcceptAsync(clash.Id, tasker.Id));
        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("trùng lịch", ex.Message);
        Assert.Contains("dự kiến xong khoảng", ex.Message);

        var free = Db.Booking(Db.Customer("Khách 3"), service, BookingStatus.Matched, configure: b => b.ScheduledTime = start.AddMinutes(270));
        Db.Offer(free, tasker);
        await CreateOfferService().AcceptAsync(free.Id, tasker.Id);
        Assert.Equal(BookingStatus.Accepted, (await Fresh().Bookings.SingleAsync(b => b.Id == free.Id)).Status);
    }

    // ---------- Cấu hình dịch vụ & chi tiết dịch vụ ----------

    private ServiceCategoriesController AsAdmin() => new ServiceCategoriesController(Db).AsUser(1, UserRole.Admin);

    [Fact]
    public async Task Admin_TaoDichVu_CauHinhSoLuongVaChiTiet_ChuanHoaDong()
    {
        var created = await AsAdmin().Create(new UpsertServiceCategoryRequest("Vệ sinh máy lạnh", "Vệ sinh máy lạnh treo tường", 250_000m,
            "lần", null, MinQuantity: 1, MaxQuantity: 3, QuantityLabel: " Số máy ", QuantityHint: "Mỗi máy khoảng 45 phút",
            MinutesPerUnit: 45, RequiresItemList: true, Includes: "- Tháo, rửa lưới lọc\n\n• Xịt rửa dàn lạnh  \n", Excludes: "  ",
            CustomerPrepares: null, Notes: "Không bao gồm nạp gas"));
        var dto = Assert.IsType<ServiceCategoryResponse>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Equal(3, dto.MaxQuantity);
        Assert.Equal("Số máy", dto.QuantityLabel);
        Assert.True(dto.RequiresItemList);
        Assert.Equal("Tháo, rửa lưới lọc\nXịt rửa dàn lạnh", dto.Includes);
        Assert.Null(dto.Excludes);

        // Sửa mà không gửi các field mới → giữ nguyên
        await AsAdmin().Update(dto.Id, new UpsertServiceCategoryRequest("Vệ sinh máy lạnh", "Mô tả mới", 260_000m, "lần", null));
        var s = await Fresh().ServiceCategories.SingleAsync(x => x.Id == dto.Id);
        Assert.Equal(3, s.MaxQuantity);
        Assert.Equal(45, s.MinutesPerUnit);
        Assert.Equal("Không bao gồm nạp gas", s.Notes);

        var ex = await Assert.ThrowsAsync<ApiException>(() => AsAdmin().Update(dto.Id,
            new UpsertServiceCategoryRequest("Vệ sinh máy lạnh", null, 260_000m, "lần", null, MinQuantity: 4, MaxQuantity: 2)));
        Assert.Equal("Số lượng tối thiểu không được lớn hơn số lượng tối đa.", ex.Message);
    }

    [Fact]
    public void DanhMucMau_DuCauHinh_SuaDienNuocTheoHangMuc()
    {
        var all = HomeService.Api.Data.ServiceCatalog.Defaults();
        Assert.Equal(5, all.Count);
        Assert.All(all, s =>
        {
            Assert.InRange(s.MinQuantity, 1, s.MaxQuantity);
            Assert.True(s.MinutesPerUnit > 0);
            Assert.False(string.IsNullOrWhiteSpace(s.Includes));
            Assert.False(string.IsNullOrWhiteSpace(s.Excludes));
        });
        var repair = all.Single(s => s.Name == "Sửa điện nước");
        Assert.True(repair.RequiresItemList);
        Assert.Equal("hạng mục", repair.Unit);
        Assert.Equal((1, 5), (repair.MinQuantity, repair.MaxQuantity));
        Assert.Equal((2, 4), (all.Single(s => s.Name == "Dọn dẹp nhà").MinQuantity, all.Single(s => s.Name == "Dọn dẹp nhà").MaxQuantity));
        Assert.Equal("mẻ", all.Single(s => s.Name == "Giặt ủi").Unit);
    }
}
