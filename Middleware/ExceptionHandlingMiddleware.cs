using System.Net;
using System.Text.Json;
using HomeService.Api.Common;

namespace HomeService.Api.Middleware;

// NFR-07: mọi lỗi không mong muốn được bắt, ghi log và trả JSON thống nhất (không lộ stack trace).
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            // Lỗi nghiệp vụ: 400/409 có { message }, 403/404 trả body rỗng (SRS 8.6)
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = ex.StatusCode;
            if (!string.IsNullOrEmpty(ex.Message))
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = ex.Message }, JsonDefaults.Options));
            }
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // 2 thao tác cùng sửa 1 đơn/ví/thanh toán – thao tác đến sau bị từ chối, không ghi đè dữ liệu
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new { message = "Dữ liệu vừa được cập nhật bởi thao tác khác, vui lòng tải lại và thử lại." }, JsonDefaults.Options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            var payload = JsonSerializer.Serialize(new { message = "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau." }, JsonDefaults.Options);
            await context.Response.WriteAsync(payload);
        }
    }
}
