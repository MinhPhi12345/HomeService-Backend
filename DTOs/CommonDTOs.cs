namespace HomeService.Api.DTOs;

// SRS 8.7 – định dạng phân trang cho API danh sách mới
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public static class Paging
{
    /// <summary>page ≥ 1; pageSize 1–100, mặc định 20 (NFR-02).</summary>
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
        => (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 100));
}
