namespace HomeService.Api.Options;

/// <summary>
/// Nơi lưu ảnh tải lên. Mặc định: &lt;thư mục chạy&gt;/wwwroot/uploads.
/// Có thể đặt đường dẫn tuyệt đối (vd. D:\\data\\uploads) hoặc tương đối so với thư mục ứng dụng.
/// Khi cập nhật bản mới, KHÔNG xóa thư mục này để giữ ảnh người dùng đã tải lên.
/// </summary>
public class StorageOptions
{
    public const string Section = "Storage";

    public string? UploadsPath { get; set; }

    public static string ResolveUploadsPath(IConfiguration config, string contentRoot)
    {
        var configured = config.GetSection(Section)["UploadsPath"];
        if (string.IsNullOrWhiteSpace(configured)) return Path.Combine(contentRoot, "wwwroot", "uploads");
        return Path.IsPathRooted(configured) ? Path.GetFullPath(configured) : Path.GetFullPath(Path.Combine(contentRoot, configured));
    }
}
