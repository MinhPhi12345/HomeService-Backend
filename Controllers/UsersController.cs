using HomeService.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP01 - US06 (phân quyền) / EP08 - US30 (quản lý KH & Tasker)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? role)
    {
        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<Enums.UserRole>(role, true, out var parsedRole))
            query = query.Where(u => u.Role == parsedRole);

        var users = await query
            .Select(u => new { u.Id, u.FullName, u.Email, u.PhoneNumber, u.Role, u.IsActive, u.CreatedAt })
            .ToListAsync();

        return Ok(users);
    }

    // US30 - Khóa / mở khóa tài khoản vi phạm
    [HttpPut("{id:int}/active-status")]
    public async Task<IActionResult> SetActiveStatus(int id, [FromQuery] bool isActive)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.IsActive = isActive;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
