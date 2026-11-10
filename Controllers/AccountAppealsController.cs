using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Options;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HomeService.Api.Controllers;

// US48 – yêu cầu mở khóa tài khoản (SRS v1.8).
// Người bị khóa: send-otp → verify (nhận token phiên) → upload ảnh (header X-Appeal-Token) → POST gửi yêu cầu.
// Admin: danh sách, chi tiết, chấp nhận (mở khóa), từ chối.
[ApiController]
[Route("api/account-appeals")]
public class AccountAppealsController : ControllerBase
{
    private readonly AppealService _appeals;
    private readonly string _uploadsRoot;

    public AccountAppealsController(AppealService appeals, IOptions<StorageOptions> storage, IWebHostEnvironment env)
    {
        _appeals = appeals;
        _uploadsRoot = storage.Value.UploadsPath ?? Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
    }

    // Bước 1 – gửi mã OTP (luôn 200, không tiết lộ email có bị khóa hay không)
    [HttpPost("send-otp")]
    [AllowAnonymous]
    public async Task<ActionResult<OtpSentResponse>> SendOtp(EmailRequest request) =>
        Ok(await _appeals.SendOtpAsync(request.Email));

    // Bước 2 – nhập mã OTP → token phiên + lý do khóa + có được gửi yêu cầu không
    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult<AppealSessionResponse>> Verify(AppealVerifyRequest request) =>
        Ok(await _appeals.VerifyAsync(request.Email, request.Code));

    // Tải lại thông tin trong phiên
    [HttpGet("session")]
    [AllowAnonymous]
    public async Task<ActionResult<AppealContextResponse>> Session([FromHeader(Name = "X-Appeal-Token")] string? token) =>
        Ok(await _appeals.GetContextAsync(token));

    // Ảnh đính kèm (JPG/PNG ≤ 5 MB) trong phiên đã xác thực
    [HttpPost("upload")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ImageStorage.MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<UploadResponse>> Upload([FromHeader(Name = "X-Appeal-Token")] string? token, IFormFile? file)
    {
        var url = await _appeals.UploadAsync(token, file, _uploadsRoot);
        return StatusCode(StatusCodes.Status201Created, new UploadResponse(url));
    }

    // Bước 3 – gửi yêu cầu mở khóa
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<AppealContextResponse>> Submit(SubmitAppealRequest request) =>
        Ok(await _appeals.SubmitAsync(request));

    // Admin – danh sách (?status=0 chờ xử lý, 1 đã chấp nhận, 2 đã từ chối)
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<AppealListItem>>> List([FromQuery] AppealStatus? status) =>
        Ok(await _appeals.ListAsync(status));

    [HttpGet("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<AppealDetailResponse>> Get(int id) => Ok(await _appeals.GetAsync(id));

    [HttpPut("{id:int}/approve")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Approve(int id, ResolveAppealRequest? request = null)
    {
        await _appeals.ApproveAsync(id, User.GetUserId(), request?.Note);
        return NoContent();
    }

    [HttpPut("{id:int}/reject")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Reject(int id, RejectAppealRequest request)
    {
        await _appeals.RejectAsync(id, User.GetUserId(), request.Note);
        return NoContent();
    }
}
