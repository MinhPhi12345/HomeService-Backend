namespace HomeService.Api.Common;

public static class VnTime
{
    /// <summary>Hiển thị giờ Việt Nam (UTC+7) trong nội dung thông báo: dd/MM/yyyy HH:mm (SRS 8.5).</summary>
    public static string Format(DateTime utc) => utc.AddHours(7).ToString("dd/MM/yyyy HH:mm");

    /// <summary>Giờ ngắn gọn: "10:30" nếu cùng ngày (giờ VN) với hiện tại, ngược lại "10:30 ngày 09/11".</summary>
    public static string Clock(DateTime utc, DateTime? nowUtc = null)
    {
        var vn = utc.AddHours(7);
        var today = (nowUtc ?? DateTime.UtcNow).AddHours(7).Date;
        return vn.Date == today ? vn.ToString("HH:mm") : vn.ToString("HH:mm 'ngày' dd/MM");
    }
}
