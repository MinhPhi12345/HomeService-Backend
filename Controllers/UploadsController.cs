using HomeService.Api.Common;
using HomeService.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

// US04 (ảnh CCCD), US18 (ảnh check-in/check-out), US35 (ảnh bằng chứng khiếu nại)
// POST /api/uploads – multipart/form-data, field "file"; chỉ nhận JPG/PNG ≤ 5 MB (NFR-15)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadsController : ControllerBase
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly IWebHostEnvironment _env;

    public UploadsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<UploadResponse>> Upload(IFormFile? file)
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

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var subFolder = DateTime.UtcNow.ToString("yyyyMM");
        var folder = Path.Combine(webRoot, "uploads", subFolder);
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using (var output = System.IO.File.Create(Path.Combine(folder, fileName)))
        {
            await file.CopyToAsync(output);
        }

        var url = $"{Request.Scheme}://{Request.Host}/uploads/{subFolder}/{fileName}";
        return StatusCode(StatusCodes.Status201Created, new UploadResponse(url));
    }

    private static string? DetectImageExtension(byte[] h)
    {
        if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        return null;
    }
}
