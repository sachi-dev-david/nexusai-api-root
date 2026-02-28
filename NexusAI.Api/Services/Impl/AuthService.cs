using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NexusAI.Api.Data;
using NexusAI.Api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NexusAI.Api.Services.Impl;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    public async Task<LoginResponse?> LoginAsync(string username, string password)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user is null)
        {
            _logger.LogWarning("登入失敗：找不到帳號 {Username}", username);
            return null;
        }

        // BCrypt 驗證密碼
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("登入失敗：密碼錯誤 {Username}", username);
            return null;
        }

        // 更新最後登入時間
        await _db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LastLoginAt, DateTime.UtcNow));

        var expiresHours = _config.GetValue<int>("Jwt:ExpiresHours", 8);
        var expiresAt    = DateTime.UtcNow.AddHours(expiresHours);
        var token        = GenerateJwtToken(user.Username, user.Id.ToString(), user.Role, expiresAt);

        _logger.LogInformation("使用者 {Username} 登入成功", username);

        return new LoginResponse
        {
            Token     = token,
            Username  = user.DisplayName ?? user.Username,
            Role      = user.Role,
            ExpiresAt = expiresAt
        };
    }

    public async Task LogoutAsync(string? username)
    {
        _logger.LogInformation("使用者 {Username} 已登出", username);
        await Task.CompletedTask;
    }

    private string GenerateJwtToken(string username, string userId, string role, DateTime expiresAt)
    {
        var key         = _config["Jwt:Key"]!;
        var issuer      = _config["Jwt:Issuer"]!;
        var audience    = _config["Jwt:Audience"]!;
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name,           username),
            new Claim(ClaimTypes.Role,           role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
