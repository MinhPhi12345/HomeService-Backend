using HomeService.Api.DTOs;
using HomeService.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HomeService.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // US01 - Đăng ký tài khoản
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // US02 - Đăng nhập
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    // US03 - Đăng xuất (client-side: xóa token; nếu cần revoke thì bổ sung bảng RefreshToken)
    [HttpPost("logout")]
    public IActionResult Logout() => Ok(new { message = "Đăng xuất thành công." });
}
