using System.Net;
using System.Text;
using System.Text.Json;
using HomeService.Api.Common;
using HomeService.Api.Controllers;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Options;
using HomeService.Api.Services;
using HomeService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace HomeService.Api.Tests.Controllers;

/// <summary>POST /api/uploads – chỉ nhận ảnh JPG/PNG thật, ≤ 5 MB (NFR-15).</summary>
public class UploadsControllerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private UploadsController Create() =>
        new UploadsController(MsOptions.Create(new StorageOptions { UploadsPath = _folder.Path }), new FakeWebHostEnvironment(_folder.Path))
            .AsUser(1, UserRole.Customer);

    private static IFormFile File(byte[] content, string name = "anh.png") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", name);

    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] JpgHeader = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 1, 2 };

    [Fact]
    public async Task Upload_Png_201_LuuTheoThangVaTraDuongDanTuongDoi()
    {
        var result = await Create().Upload(File(PngHeader));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, obj.StatusCode);
        var url = Assert.IsType<UploadResponse>(obj.Value).Url;
        Assert.Matches(@"^/uploads/\d{6}/[0-9a-f]{32}\.png$", url);
        var saved = Path.Combine(_folder.Path, url["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar));
        Assert.True(System.IO.File.Exists(saved));
        Assert.Equal(PngHeader, await System.IO.File.ReadAllBytesAsync(saved));
    }

    [Fact]
    public async Task Upload_Jpg_DoiDuoiTheoNoiDungThat()
    {
        var result = await Create().Upload(File(JpgHeader, "khong-dung-duoi.png"));
        var url = Assert.IsType<UploadResponse>(Assert.IsType<ObjectResult>(result.Result).Value).Url;
        Assert.EndsWith(".jpg", url);
    }

    [Fact]
    public async Task Upload_KhongPhaiAnh_400()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Create().Upload(File(Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "a.png")));
        Assert.Equal("Chỉ hỗ trợ ảnh JPG hoặc PNG.", ex.Message);
    }

    [Fact]
    public async Task Upload_RongHoacThieuFile_400()
    {
        Assert.Equal("Vui lòng chọn ảnh để tải lên.", (await Assert.ThrowsAsync<ApiException>(() => Create().Upload(null))).Message);
        await Assert.ThrowsAsync<ApiException>(() => Create().Upload(File(Array.Empty<byte>())));
    }

    [Fact]
    public async Task Upload_QuaNamMB_400()
    {
        var big = new byte[5 * 1024 * 1024 + 1];
        PngHeader.CopyTo(big, 0);
        var ex = await Assert.ThrowsAsync<ApiException>(() => Create().Upload(File(big)));
        Assert.Equal("Ảnh tối đa 5 MB.", ex.Message);
    }

    public void Dispose() => _folder.Dispose();
}

/// <summary>Bản đồ & địa chỉ qua Backend (GeoService, GeoController) – dùng máy chủ OSM giả.</summary>
public class GeoServiceTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private GeoService Create(StubHttpHandler handler) => new(new StubHttpClientFactory(handler), new MemoryCache(new MemoryCacheOptions()),
        MsOptions.Create(new MapProxyOptions { TileCacheFolder = _folder.Path, GeocoderBaseUrl = "https://geo.test" }),
        new FakeWebHostEnvironment(_folder.Path), NullLogger<GeoService>.Instance);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(20, 0, 0)]
    [InlineData(2, 4, 0)]    // zoom 2 chỉ có x, y từ 0 đến 3
    [InlineData(2, 0, -1)]
    public async Task GetTile_ToaDoOKhongHopLe_TraNull_KhongGoiMang(int z, int x, int y)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(new byte[] { 1 }));
        Assert.Null(await Create(handler).GetTileAsync(z, x, y, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetTile_TaiMotLanRoiDungCache()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 9 };
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(png));
        var geo = Create(handler);

        Assert.Equal(png, await geo.GetTileAsync(3, 2, 1, CancellationToken.None));
        Assert.Equal(png, await geo.GetTileAsync(3, 2, 1, CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Equal("https://tile.openstreetmap.org/3/2/1.png", handler.Requests[0]);
        Assert.True(File.Exists(Path.Combine(_folder.Path, "3", "2", "1.png")));
    }

    [Fact]
    public async Task GetTile_MayChuLoiVaChuaCoCache_TraNull()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Status(HttpStatusCode.Forbidden));
        Assert.Null(await Create(handler).GetTileAsync(1, 0, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Search_TuKhoaQuaNgan_KhongGoiMang()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("[]"));
        Assert.Empty(await Create(handler).SearchAsync(" ab ", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Search_VaReverse_DocKetQuaNominatim()
    {
        const string searchJson = """
        [
          {"lat":"10.7725","lon":"106.698","name":"Chợ Bến Thành",
           "address":{"road":"Lê Lợi","house_number":"1","suburb":"Phường Bến Thành","city_district":"Quận 1","city":"Thành phố Hồ Chí Minh"}},
          {"lat":"không phải số","lon":"106.7"}
        ]
        """;
        const string reverseJson = """{"address":{"road":"Nguyễn Huệ","city_district":"Quận 1","city":"Thành phố Hồ Chí Minh"}}""";
        var handler = new StubHttpHandler(r => StubHttpHandler.Json(r.RequestUri!.AbsolutePath == "/search" ? searchJson : reverseJson));
        var geo = Create(handler);

        var places = await geo.SearchAsync("Chợ Bến Thành", CancellationToken.None);
        var place = Assert.Single(places);
        Assert.Equal(10.7725, place.Latitude);
        Assert.Equal("1 Lê Lợi, Phường Bến Thành, Quận 1, Thành phố Hồ Chí Minh", place.Address);
        Assert.Equal("Chợ Bến Thành", place.Title);
        Assert.Contains("countrycodes=vn", handler.Requests[0]);

        var address = await geo.ReverseAsync(10.77, 106.70, CancellationToken.None);
        Assert.Equal("Nguyễn Huệ, Quận 1, Thành phố Hồ Chí Minh", address);

        // Lần 2 lấy từ cache, không gọi Nominatim nữa
        await geo.SearchAsync("chợ bến thành", CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void FormatAddress_GhepDiaChiGonKieuVietNam()
    {
        var json = Json("""{"address":{"house_number":"12","road":"Lý Tự Trọng","quarter":"Phường Bến Nghé","district":"Quận 1","city":"Thành phố Hồ Chí Minh"}}""");
        Assert.Equal("12 Lý Tự Trọng, Phường Bến Nghé, Quận 1, Thành phố Hồ Chí Minh", GeoService.FormatAddress(json));
    }

    [Fact]
    public void FormatAddress_BoPhanLapLaiLienTiep()
    {
        var json = Json("""{"address":{"road":"Đường A","city_district":"Thủ Đức","city":"Thủ Đức"}}""");
        Assert.Equal("Đường A, Thủ Đức", GeoService.FormatAddress(json));
    }

    [Fact]
    public void FormatAddress_ThieuChiTiet_DungDisplayNameBoMaBuuChinhVaQuocGia()
    {
        var json = Json("""{"address":{"city":"Hà Nội"},"display_name":"Hồ Gươm, Hoàn Kiếm, Hà Nội, 100000, Việt Nam"}""");
        Assert.Equal("Hồ Gươm, Hoàn Kiếm, Hà Nội", GeoService.FormatAddress(json));
    }

    [Fact]
    public void FormatAddress_KhongCoThongTin_TraNull()
    {
        Assert.Null(GeoService.FormatAddress(Json("{}")));
    }

    [Fact]
    public async Task GeoController_Tile_KhongHopLe404_HopLeTraAnhPng()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Bytes(new byte[] { 1, 2, 3 }));
        var controller = new GeoController(Create(handler));
        Assert.IsType<NotFoundResult>(await controller.Tile(30, 0, 0, CancellationToken.None));
        var file = Assert.IsType<FileContentResult>(await controller.Tile(0, 0, 0, CancellationToken.None));
        Assert.Equal("image/png", file.ContentType);
    }

    public void Dispose() => _folder.Dispose();
}
