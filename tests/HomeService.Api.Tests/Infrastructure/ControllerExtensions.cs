using System.Security.Claims;
using HomeService.Api.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Xunit;

namespace HomeService.Api.Tests.Infrastructure;

public static class ControllerExtensions
{
    /// <summary>Giả lập người dùng đã đăng nhập (claim giống JWT do TokenService phát hành).</summary>
    public static T AsUser<T>(this T controller, int userId, UserRole role, string fullName = "Người dùng test")
        where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Principal(userId, role, fullName) } };
        return controller;
    }

    public static T AsAnonymous<T>(this T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    public static ClaimsPrincipal Principal(int userId, UserRole role, string fullName = "Người dùng test") =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim("fullName", fullName),
        }, "Test"));

    /// <summary>Lấy giá trị của kết quả 200 OK.</summary>
    public static T OkValue<T>(this ActionResult<T> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<T>(ok.Value);
    }

    public static int? StatusOf(this IActionResult result) => result switch
    {
        IStatusCodeActionResult s => s.StatusCode,
        _ => null,
    };
}
