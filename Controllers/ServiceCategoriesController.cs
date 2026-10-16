using HomeService.Api.Common;
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

    // US10 - Xem/tìm kiếm dịch vụ theo tên (public, không cần đăng nhập); chỉ trả dịch vụ đang hoạt động
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<ServiceCategoryResponse>>> GetAll([FromQuery] string? keyword)
    {
        var query = _db.ServiceCategories.AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(kw)); // không phân biệt hoa thường
        }

        var result = await query
            .OrderBy(s => s.Name)
            .Select(s => new ServiceCategoryResponse(s.Id, s.Name, s.Description, s.BasePrice, s.Unit, s.IconUrl, s.IsActive))
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceCategoryResponse>> GetById(int id)
    {
        var s = await _db.ServiceCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        return Ok(ToResponse(s));
    }

    // US07 - Thêm danh mục dịch vụ (Admin) kèm giá cơ bản (US11)
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ServiceCategoryResponse>> Create(UpsertServiceCategoryRequest request)
    {
        ValidatePrice(request.BasePrice);
        await EnsureUniqueNameAsync(request.Name, null);

        var entity = new ServiceCategory
        {
            Name = request.Name.Trim(),
            Description = NullIfEmpty(request.Description),
            BasePrice = request.BasePrice,
            Unit = request.Unit,
            IconUrl = NullIfEmpty(request.IconUrl),
        };
        _db.ServiceCategories.Add(entity);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToResponse(entity));
    }

    // US08 - Sửa danh mục dịch vụ (Admin) — cũng dùng để thiết lập bảng giá (US11).
    // FR-07: đổi basePrice chỉ áp dụng cho đơn tạo SAU thời điểm đổi (đơn cũ giữ nguyên totalPrice – BR-B01).
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, UpsertServiceCategoryRequest request)
    {
        var entity = await _db.ServiceCategories.FindAsync(id);
        if (entity is null) return NotFound();
        ValidatePrice(request.BasePrice);
        await EnsureUniqueNameAsync(request.Name, id);

        entity.Name = request.Name.Trim();
        entity.Description = NullIfEmpty(request.Description);
        entity.BasePrice = request.BasePrice;
        entity.Unit = request.Unit;
        entity.IconUrl = NullIfEmpty(request.IconUrl);

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // US09 - Xóa danh mục dịch vụ (Admin) — soft delete (BR-S01)
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

    // US08 – không để 2 dịch vụ đang hoạt động trùng tên (tránh khách chọn nhầm)
    private async Task EnsureUniqueNameAsync(string name, int? exceptId)
    {
        var normalized = name.Trim().ToLower();
        var duplicated = await _db.ServiceCategories.AnyAsync(s =>
            s.IsActive && s.Name.ToLower() == normalized && (exceptId == null || s.Id != exceptId));
        if (duplicated) throw ApiException.Conflict("Tên dịch vụ đã tồn tại.");
    }

    private static void ValidatePrice(decimal price)
    {
        // SRS 6.1: basePrice là số nguyên VND, > 0 và ≤ 100.000.000
        if (price <= 0 || price > 100_000_000 || decimal.Truncate(price) != price)
            throw ApiException.BadRequest("Giá phải lớn hơn 0.");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ServiceCategoryResponse ToResponse(ServiceCategory s) =>
        new(s.Id, s.Name, s.Description, s.BasePrice, s.Unit, s.IconUrl, s.IsActive);
}
