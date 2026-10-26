using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Extensions;
using HomeService.Api.Models;
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
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        await _db.SaveChangesAsync();
        return Ok(ToResponse(user));
    }

    private static ProfileResponse ToResponse(User u) => new(
        u.Id, u.FullName, u.Email, u.PhoneNumber, u.Address, u.AvatarUrl, u.Role,
        u.OutstandingAmount, u.IsCashBlocked, u.CreatedAt);
}
