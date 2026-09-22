using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP02 - Quản lý dịch vụ (US07-US11)
[ApiController]
[Route("api/[controller]")]
public class ServiceCategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ServiceCategoriesController(AppDbContext db)
    {
        _db = db;
    }

    // US10 - Xem/tìm kiếm dịch vụ (public, không cần đăng nhập)
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ServiceCategoryResponse>>> GetAll([FromQuery] string? keyword)
    {
        var query = _db.ServiceCategories.Where(s => s.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(s => s.Name.Contains(keyword));

        var result = await query
            .Select(s => new ServiceCategoryResponse(s.Id, s.Name, s.Description, s.BasePrice, s.Unit, s.IconUrl, s.IsActive))
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceCategoryResponse>> GetById(int id)
    {
        var s = await _db.ServiceCategories.FindAsync(id);
        if (s is null) return NotFound();
        return Ok(new ServiceCategoryResponse(s.Id, s.Name, s.Description, s.BasePrice, s.Unit, s.IconUrl, s.IsActive));
    }

    // US07 - Thêm danh mục dịch vụ (Admin)
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServiceCategoryResponse>> Create(UpsertServiceCategoryRequest request)
    {
        var entity = new ServiceCategory
        {
            Name = request.Name,
            Description = request.Description,
            BasePrice = request.BasePrice,
            Unit = request.Unit,
            IconUrl = request.IconUrl,
        };
        _db.ServiceCategories.Add(entity);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id },
            new ServiceCategoryResponse(entity.Id, entity.Name, entity.Description, entity.BasePrice, entity.Unit, entity.IconUrl, entity.IsActive));
    }

    // US08 - Sửa danh mục dịch vụ (Admin) — cũng dùng để thiết lập bảng giá (US11)
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, UpsertServiceCategoryRequest request)
    {
        var entity = await _db.ServiceCategories.FindAsync(id);
        if (entity is null) return NotFound();

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.BasePrice = request.BasePrice;
        entity.Unit = request.Unit;
        entity.IconUrl = request.IconUrl;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US09 - Xóa danh mục dịch vụ (Admin) — soft delete
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.ServiceCategories.FindAsync(id);
        if (entity is null) return NotFound();

        entity.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
