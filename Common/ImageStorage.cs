namespace HomeService.Api.Common;

/// <summary>Lưu ảnh JPG/PNG ≤ 5 MB vào thư mục uploads (NFR-15). Dùng chung cho POST /api/uploads và ảnh yêu cầu mở khóa.</summary>
public static class ImageStorage
{
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>Kiểm tra & lưu ảnh, trả về đường dẫn tương đối "/uploads/yyyyMM/ten-file.ext".</summary>
    public static async Task<string> SaveAsync(IFormFile? file, string uploadsRoot)
    {
        if (file is null || file.Length == 0)
            throw ApiException.BadRequest("Vui lòng chọn ảnh để tải lên.");
        if (file.Length > MaxBytes)
            throw ApiException.BadRequest("Ảnh tối đa 5 MB.");

        // Kiểm tra "chữ ký" đầu file để chắc chắn là ảnh JPG/PNG thật (không tin phần mở rộng)
        var header = new byte[8];
        await using (var peek = file.OpenReadStream())
        {
            _ = await peek.ReadAsync(header.AsMemory(0, header.Length));
        }
        var extension = DetectImageExtension(header)
            ?? throw ApiException.BadRequest("Chỉ hỗ trợ ảnh JPG hoặc PNG.");

        var subFolder = DateTime.UtcNow.ToString("yyyyMM");
        var folder = Path.Combine(uploadsRoot, subFolder);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using (var output = File.Create(Path.Combine(folder, fileName)))
        {
            await file.CopyToAsync(output);
        }

        // Đường dẫn tương đối – không gắn tên máy để ảnh không "chết" khi deploy / đổi tên miền (xem UploadUrl)
        return $"/uploads/{subFolder}/{fileName}";
    }

    /// <summary>Chỉ nhận ảnh do hệ thống lưu (đường dẫn /uploads/...).</summary>
    public static bool IsUploadedImage(string? url) =>
        url is not null && url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) && !url.Contains("..");

    private static string? DetectImageExtension(byte[] h)
    {
        if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        return null;
    }
}
