using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// US05 – Cập nhật thông tin cá nhân (tách riêng khỏi UsersController vì controller đó chỉ dành cho Admin – FR-05)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProfileController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<ProfileResponse>> Get()
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user is null) return NotFound();
        return Ok(ToResponse(user));
    }

    [HttpPut]
    public async Task<ActionResult<ProfileResponse>> Update(UpdateProfileRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user is null) return NotFound();

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        user.AvatarUrl = UploadUrl.Normalize(request.AvatarUrl);
        await _db.SaveChangesAsync();
        return Ok(ToResponse(user));
    }

    // Đổi mật khẩu – bước 1: gửi mã OTP về email của tài khoản đang đăng nhập
    [HttpPost("password/otp")]
    public async Task<ActionResult<OtpSentResponse>> SendChangePasswordOtp([FromServices] OtpService otp)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user is null) return NotFound();
        return Ok(await otp.SendAsync(user, OtpPurpose.ChangePassword));
    }

    // Đổi mật khẩu – bước 2: mật khẩu hiện tại + mã OTP + mật khẩu mới
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, [FromServices] OtpService otp)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user is null) return NotFound();
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            throw ApiException.BadRequest("Mật khẩu hiện tại không đúng.");
        if (request.NewPassword == request.CurrentPassword)
            throw ApiException.BadRequest("Mật khẩu mới phải khác mật khẩu hiện tại.");

        await otp.VerifyAsync(user.Id, OtpPurpose.ChangePassword, request.Code);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Đổi mật khẩu thành công." });
    }

    private static ProfileResponse ToResponse(User u) => new(
        u.Id, u.FullName, u.Email, u.PhoneNumber, u.Address, u.AvatarUrl, u.Role,
        u.OutstandingAmount, u.IsCashBlocked, u.CreatedAt, u.LockPendingAt);
}
