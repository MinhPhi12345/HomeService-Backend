using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeService.Api.DTOs;
using HomeService.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Services;

/// <summary>
/// Trung chuyển ảnh bản đồ (OpenStreetMap) và tra địa chỉ (Nominatim) cho app/web.
/// - Ảnh bản đồ lưu cache ra đĩa; cùng 1 ô bản đồ chỉ tải từ OSM một lần.
/// - Nominatim giới hạn ~1 request/giây: các lần gọi được xếp hàng, kết quả cache 1 ngày.
/// </summary>
public class GeoService
{
    public const string HttpClientName = "osm";
    private static readonly SemaphoreSlim GeocoderGate = new(1, 1);
    private static DateTime _lastGeocoderCall = DateTime.MinValue;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TileLocks = new();

    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly MapProxyOptions _options;
    private readonly string _tileFolder;
    private readonly ILogger<GeoService> _logger;

    /// <summary>Lỗi gần nhất khi gọi OpenStreetMap (hiện ở /api/geo/status để chẩn đoán).</summary>
    public static string? LastError { get; private set; }

    public GeoService(IHttpClientFactory httpFactory, IMemoryCache cache, IOptions<MapProxyOptions> options,
        IWebHostEnvironment env, ILogger<GeoService> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
        _tileFolder = string.IsNullOrWhiteSpace(_options.TileCacheFolder)
            ? Path.Combine(env.ContentRootPath, "tile-cache")
            : _options.TileCacheFolder!;
    }

    // ------------------------------------------------------------------ Ảnh bản đồ

    /// <summary>Trả ảnh PNG của ô (z, x, y); null nếu toạ độ ô không hợp lệ hoặc không tải được.</summary>
    public async Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken ct)
    {
        if (z < 0 || z > 19) return null;
        var max = 1 << z;
        if (x < 0 || y < 0 || x >= max || y >= max) return null;

        var path = Path.Combine(_tileFolder, z.ToString(), x.ToString(), $"{y}.png");
        if (IsFresh(path)) return await File.ReadAllBytesAsync(path, ct);

        var gate = TileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (IsFresh(path)) return await File.ReadAllBytesAsync(path, ct);
            var url = _options.TileUrlTemplate
                .Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            using var response = await _httpFactory.CreateClient(HttpClientName).GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tải ảnh bản đồ {Url} thất bại: {Status}", url, (int)response.StatusCode);
                LastError = $"Ảnh bản đồ: OpenStreetMap trả mã {(int)response.StatusCode} {response.ReasonPhrase}";
                return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null; // dùng tạm bản cũ
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
            LastError = null;
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(ex, "Không tải được ảnh bản đồ {Z}/{X}/{Y}", z, x, y);
            LastError = $"Ảnh bản đồ: {Describe(ex)}";
            return File.Exists(path) ? await File.ReadAllBytesAsync(path, CancellationToken.None) : null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Gom thông điệp lỗi kèm lỗi gốc (DNS, SSL, timeout...) thành 1 dòng.</summary>
    private static string Describe(Exception ex)
    {
        if (ex is TaskCanceledException) return "Hết thời gian chờ (timeout) khi kết nối OpenStreetMap.";
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        return string.Join(" -> ", messages.Distinct());
    }

    private bool IsFresh(string path) =>
        File.Exists(path) && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-_options.TileCacheDays);

    // ------------------------------------------------------------------ Địa chỉ

    public async Task<string?> ReverseAsync(double lat, double lng, CancellationToken ct)
    {
        var key = $"rev:{Math.Round(lat, 5)}:{Math.Round(lng, 5)}";
        if (_cache.TryGetValue(key, out string? cached)) return cached;
        var json = await GeocodeAsync("/reverse", new Dictionary<string, string>
        {
            ["format"] = "jsonv2",
            ["lat"] = lat.ToString(CultureInfo.InvariantCulture),
            ["lon"] = lng.ToString(CultureInfo.InvariantCulture),
            ["zoom"] = "18",
            ["addressdetails"] = "1",
            ["accept-language"] = "vi",
        }, ct);
        var address = json is { ValueKind: JsonValueKind.Object } obj ? FormatAddress(obj) : null;
        if (json is not null) _cache.Set(key, address, TimeSpan.FromDays(1));
        return address;
    }

    public async Task<IReadOnlyList<GeoPlaceItem>> SearchAsync(string query, CancellationToken ct)
    {
        var q = query.Trim();
        if (q.Length < 3) return Array.Empty<GeoPlaceItem>();
        var key = $"search:{q.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out IReadOnlyList<GeoPlaceItem>? cached) && cached is not null) return cached;
        var json = await GeocodeAsync("/search", new Dictionary<string, string>
        {
            ["format"] = "jsonv2",
            ["q"] = q,
            ["countrycodes"] = "vn",
            ["addressdetails"] = "1",
            ["accept-language"] = "vi",
            ["limit"] = "6",
        }, ct);
        var results = new List<GeoPlaceItem>();
        if (json is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (!double.TryParse(item.TryGetProperty("lat", out var la) ? la.GetString() : null,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
                if (!double.TryParse(item.TryGetProperty("lon", out var lo) ? lo.GetString() : null,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
                var address = FormatAddress(item);
                if (address is null) continue;
                var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var title = string.IsNullOrWhiteSpace(name) || address.StartsWith(name!) ? null : name;
                results.Add(new GeoPlaceItem(lat, lon, address, title));
            }
            _cache.Set(key, (IReadOnlyList<GeoPlaceItem>)results, TimeSpan.FromDays(1));
        }
        return results;
    }

    private async Task<JsonElement?> GeocodeAsync(string path, Dictionary<string, string> query, CancellationToken ct)
    {
        var url = _options.GeocoderBaseUrl.TrimEnd('/') + path + "?" +
                  string.Join("&", query.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
        await GeocoderGate.WaitAsync(ct);
        try
        {
            // Chính sách Nominatim: tối đa 1 request/giây cho toàn bộ ứng dụng.
            var wait = _lastGeocoderCall.AddMilliseconds(1100) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastGeocoderCall = DateTime.UtcNow;
            using var response = await _httpFactory.CreateClient(HttpClientName).GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tra địa chỉ {Url} thất bại: {Status}", url, (int)response.StatusCode);
                LastError = $"Tra địa chỉ: Nominatim trả mã {(int)response.StatusCode} {response.ReasonPhrase}";
                return null;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Không tra được địa chỉ {Url}", url);
            LastError = $"Tra địa chỉ: {Describe(ex)}";
            return null;
        }
        finally
        {
            GeocoderGate.Release();
        }
    }

    /// <summary>Ghép địa chỉ gọn kiểu Việt Nam: "số nhà đường, phường, quận, thành phố".</summary>
    public static string? FormatAddress(JsonElement json)
    {
        if (json.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            string? Pick(params string[] keys)
            {
                foreach (var key in keys)
                    if (a.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(v.GetString()))
                        return v.GetString()!.Trim();
                return null;
            }

            var street = string.Join(" ", new[] { Pick("house_number"), Pick("road", "pedestrian", "footway", "residential") }
                .Where(s => s is not null));
            var parts = new[]
            {
                street.Length > 0 ? street : null,
                Pick("quarter", "neighbourhood", "suburb", "village", "hamlet"),
                Pick("city_district", "district", "county", "town"),
                Pick("city", "state"),
            }.Where(s => s is not null).Cast<string>().ToList();
            var unique = new List<string>();
            foreach (var p in parts)
                if (unique.Count == 0 || unique[^1] != p) unique.Add(p);
            if (unique.Count >= 2) return string.Join(", ", unique);
        }
        if (!json.TryGetProperty("display_name", out var d) || d.ValueKind != JsonValueKind.String) return null;
        var pieces = d.GetString()!.Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && s != "Việt Nam" && !Regex.IsMatch(s, @"^\d{5,6}$"));
        var text = string.Join(", ", pieces);
        return text.Length == 0 ? null : text;
    }
}
