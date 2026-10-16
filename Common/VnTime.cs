namespace HomeService.Api.Common;

public static class VnTime
{
    /// <summary>Hiển thị giờ Việt Nam (UTC+7) trong nội dung thông báo: dd/MM/yyyy HH:mm (SRS 8.5).</summary>
    public static string Format(DateTime utc) => utc.AddHours(7).ToString("dd/MM/yyyy HH:mm");
}
