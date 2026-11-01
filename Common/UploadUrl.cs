using System.Text.RegularExpressions;

namespace HomeService.Api.Common;

/// <summary>
/// Ảnh tải lên được lưu dưới dạng đường dẫn TƯƠNG ĐỐI "/uploads/yyyyMM/ten-file.png".
/// Không lưu kèm tên máy (http://localhost:8080…) để ảnh vẫn hiển thị sau khi deploy, đổi tên miền
/// hoặc khi chạy sau reverse proxy HTTPS. FE / Mobile tự ghép địa chỉ Backend đang dùng khi hiển thị.
/// </summary>
public static partial class UploadUrl
{
    [GeneratedRegex(@"^https?://[^/]+(?=/uploads/)", RegexOptions.IgnoreCase)]
    private static partial Regex HostPrefix();

    /// <summary>"http://localhost:8080/uploads/a.png" → "/uploads/a.png"; ảnh ngoài hệ thống giữ nguyên.</summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        return HostPrefix().Replace(trimmed, string.Empty);
    }

    /// <summary>Chuẩn hóa danh sách URL nối bằng dấu phẩy (ảnh bằng chứng khiếu nại).</summary>
    public static string? NormalizeJoined(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return urls;
        return string.Join(',', urls.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(u => Normalize(u)!));
    }

    public static bool NeedsNormalize(string? url) =>
        !string.IsNullOrEmpty(url) && HostPrefix().IsMatch(url.Trim());
}
