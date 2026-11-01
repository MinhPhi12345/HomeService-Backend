using System.Text.RegularExpressions;

namespace HomeService.Api.Common;

// SRS 7.9 – "Khu vực làm việc" của Tasker chỉ là NHÃN HIỂN THỊ (hồ sơ công khai, trang duyệt hồ sơ, app Tasker).
// Ghép nối đơn KHÔNG dùng nhãn này mà dùng tọa độ điểm làm việc + bán kính phục vụ (BR-M06).
// Nhãn được lấy tự động từ địa chỉ điểm làm việc Tasker chọn trên bản đồ, nên không cần danh sách quận cố định
// và luôn khớp với vị trí thật (kể cả địa giới mới của TP.HCM từ 01/7/2025: phường/xã/đặc khu).
public static partial class WorkingAreas
{
    public const int MaxLength = 100;

    // Cấp quận/huyện (tên quen gọi): "Quận 12", "Huyện Hóc Môn", "Thành phố Thủ Đức", "TP. Thủ Đức", "Thị xã …"
    [GeneratedRegex(@"^(Quận|Huyện|Thị xã|Thành phố|TP\.?)\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex DistrictPattern();

    // Cấp xã: "Phường Sài Gòn", "Xã Bình Chánh", "Đặc khu Côn Đảo", "Thị trấn …"
    [GeneratedRegex(@"^(Phường|Xã|Đặc khu|Thị trấn)\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex WardPattern();

    // Phần không dùng làm nhãn: tỉnh/thành phố trực thuộc trung ương, quốc gia, mã bưu chính, khu phố/ấp/tổ
    [GeneratedRegex(@"(Hồ Chí Minh|HCM|^Việt Nam$|^Vietnam$|^Tỉnh\s|^\d[\d\s]*$|^(Khu phố|Ấp|Tổ)\s)", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoredPattern();

    /// <summary>
    /// Lấy nhãn khu vực từ địa chỉ dạng "số nhà đường, phường, quận, thành phố" (định dạng GeoService.FormatAddress):
    /// ưu tiên cấp quận/huyện, không có thì lấy cấp phường/xã, cuối cùng là phần đứng trước tên thành phố.
    /// Trả null nếu địa chỉ chỉ có 1 phần (vd. "Chợ Bến Thành") – khi đó không đoán.
    /// </summary>
    public static string? FromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0 && !IgnoredPattern().IsMatch(p))
            .ToList();
        if (parts.Count == 0) return null;

        // Phần đầu là số nhà/tên đường – bỏ qua khi tìm theo tiền tố
        var candidates = parts.Count > 1 ? parts.Skip(1).ToList() : parts;
        var area = candidates.LastOrDefault(p => DistrictPattern().IsMatch(p))
                   ?? candidates.LastOrDefault(p => WardPattern().IsMatch(p))
                   ?? (parts.Count > 1 ? parts[^1] : null);
        if (area is null) return null;
        area = Regex.Replace(area, @"\s+", " ");
        return area.Length > MaxLength ? area[..MaxLength].TrimEnd() : area;
    }
}
