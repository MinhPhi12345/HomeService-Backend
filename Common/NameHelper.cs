namespace HomeService.Api.Common;

/// <summary>US26 – chỉ hiện tên gọi (từ cuối của họ tên, vd. "Trần Thị Lan" → "Lan") khi xem nhận xét về người khác.</summary>
public static class NameHelper
{
    public static string GivenName(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "Người dùng" : parts[^1];
    }
}
