namespace HomeService.Api.Options;

/// <summary>
/// Nơi lưu ảnh tải lên. Mặc định: &lt;thư mục chạy&gt;/wwwroot/uploads.
/// Khi deploy Azure App Service đặt Storage__UploadsPath=/home/data/uploads (ổ cố định, không mất khi deploy lại).
/// </summary>
public class StorageOptions
{
    public const string Section = "Storage";

    public string? UploadsPath { get; set; }

    public static string ResolveUploadsPath(IConfiguration config, string contentRoot)
    {
        var configured = config.GetSection(Section)["UploadsPath"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRoot, "wwwroot", "uploads")
            : Path.GetFullPath(configured);
    }
}
