namespace HomeService.Api.Common;

// SRS 7.9 – danh sách khu vực làm việc cố định (FE hardcode cùng danh sách)
public static class WorkingAreas
{
    public static readonly string[] All =
    {
        "Quận 1", "Quận 3", "Quận 4", "Quận 5", "Quận 7", "Quận 10",
        "Bình Thạnh", "Phú Nhuận", "Gò Vấp", "Tân Bình", "Thủ Đức",
    };

    public static bool IsValid(string? area) => area is not null && All.Contains(area.Trim());
}
