using System.Security.Claims;
using HomeService.Api.Enums;

namespace HomeService.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Id người dùng đang đăng nhập (claim "sub" trong JWT).</summary>
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : 0;
    }

    public static UserRole? GetRole(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(value, out var role) ? role : null;
    }

    public static bool IsInRole(this ClaimsPrincipal user, UserRole role) => user.GetRole() == role;
}
