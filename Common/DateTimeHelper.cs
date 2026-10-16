namespace HomeService.Api.Common;

public static class DateTimeHelper
{
    /// <summary>
    /// Chuẩn hóa thời gian FE gửi lên về UTC (SRS 8.5).
    /// "2026-10-02T01:00:00Z" → Kind = Utc; chuỗi không có Z được hiểu là UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
