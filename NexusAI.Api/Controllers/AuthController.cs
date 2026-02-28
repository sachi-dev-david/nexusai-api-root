using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusAI.Api.Models;
using NexusAI.Api.Services;

namespace NexusAI.Api.Controllers;

/// <summary>
/// 認證 API
/// POST /api/auth/login   — 登入取得 JWT
/// POST /api/auth/logout  — 登出
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>帳號密碼登入</summary>
    /// <remarks>
    /// Request body:
    /// {
    ///   "username": "employee_id",
    ///   "password": "password"
    /// }
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request.Username, request.Password);
            if (result is null)
                return Unauthorized(ApiResponse.Fail("帳號或密碼錯誤"));

            _logger.LogInformation("使用者 {Username} 登入成功", request.Username);
            return Ok(ApiResponse<LoginResponse>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登入發生錯誤");
            return StatusCode(500, ApiResponse.Fail("伺服器錯誤"));
        }
    }

    /// <summary>登出（由前端清除 Token，後端可加入 Token 黑名單）</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;
        await _authService.LogoutAsync(username);
        _logger.LogInformation("使用者 {Username} 登出", username);
        return Ok(ApiResponse.Ok());
    }
}
