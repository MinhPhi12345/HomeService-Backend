using HomeService.Api.Common;
using HomeService.Api.DTOs;
using HomeService.Api.Extensions;
using HomeService.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

// EP05 – Thanh toán (US21, US24, US36)
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly PaymentService _payments;

    public PaymentsController(PaymentService payments)
    {
        _payments = payments;
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
