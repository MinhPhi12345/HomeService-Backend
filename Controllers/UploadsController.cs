using HomeService.Api.Common;
using HomeService.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HomeService.Api.Options;

namespace HomeService.Api.Controllers;

// US04 (ảnh CCCD), US18 (ảnh check-in/check-out), US35 (ảnh bằng chứng khiếu nại)
// POST /api/uploads – multipart/form-data, field "file"; chỉ nhận JPG/PNG ≤ 5 MB (NFR-15)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadsController : ControllerBase
{
    private readonly string _uploadsRoot;

    public UploadsController(IOptions<StorageOptions> storage, IWebHostEnvironment env)
    {
        _uploadsRoot = storage.Value.UploadsPath ?? Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ImageStorage.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<UploadResponse>> Upload(IFormFile? file)
    {
        var url = await ImageStorage.SaveAsync(file, _uploadsRoot);
        return StatusCode(StatusCodes.Status201Created, new UploadResponse(url));
    }
}
