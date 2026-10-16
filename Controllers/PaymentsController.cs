using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.DTOs;
using HomeService.Api.Enums;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Controllers;

// EP05 – Thanh toán (US21, US24, US36)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _payments;
    private readonly AppDbContext _db;

    public PaymentsController(PaymentService payments, AppDbContext db)
    {
        _payments = payments;
        _db = db;
    }

    // US24 - Lịch sử giao dịch: khách xem các khoản đã trả; Tasker xem các khoản của đơn mình làm (kèm chiết khấu)
    [HttpGet("my")]
    [Authorize(Roles = Roles.CustomerOrTasker)]
    public async Task<ActionResult<PagedResult<PaymentResponse>>> GetMine([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (p, size) = Paging.Normalize(page, pageSize);
        var userId = User.GetUserId();
        var query = _db.Payments.AsNoTracking();
        query = User.IsInRole(UserRole.Tasker)
            ? query.Where(x => x.Booking!.TaskerId == userId)
            : query.Where(x => x.Booking!.CustomerId == userId);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((p - 1) * size).Take(size)
            .Select(x => new { Payment = x, ServiceName = x.Booking!.ServiceCategory!.Name })
            .ToListAsync();
        var items = rows.Select(r => PaymentService.ToResponse(r.Payment, r.ServiceName)).ToList();
        return Ok(new PagedResult<PaymentResponse>(items, p, size, total));
    }

    // US21 - Khách thanh toán trước đơn online (ví/thẻ – mô phỏng); tiền được tạm giữ, đơn chuyển sang tìm Tasker
    [HttpPost]
    [Authorize(Roles = Roles.Customer)]
    public async Task<ActionResult<PaymentResponse>> Create(CreatePaymentRequest request)
    {
        var payment = await _payments.CreateAsync(User.GetUserId(), request);
        var serviceName = payment.Booking?.ServiceCategory?.Name ?? string.Empty;
        return StatusCode(StatusCodes.Status201Created, PaymentService.ToResponse(payment, serviceName));
    }
}
