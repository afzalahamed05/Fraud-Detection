using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FraudDetection.Api.Configuration;
using FraudDetection.Api.DTOs;
using FraudDetection.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FraudDetection.Api.Controllers;

/// <summary>
/// Single seeded admin account, no user table -- see AuthOptions for why. Issues a JWT
/// that [Authorize] on mutating endpoints (create transaction, update alert status) checks.
/// Read endpoints stay anonymous so the dashboard works without a login wall.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthOptions _options;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IOptions<AuthOptions> options, ILogger<AuthController> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Login(LoginRequestDto request)
    {
        var validUsername = string.Equals(request.Username, _options.AdminUsername, StringComparison.Ordinal);
        var validPassword = validUsername &&
            PasswordHasher.Verify(request.Password, _options.AdminPasswordSalt, _options.AdminPasswordHash);

        if (!validUsername || !validPassword)
        {
            _logger.LogWarning("Failed login attempt for username {Username}", request.Username);
            return Unauthorized(new { message = "Invalid username or password" });
        }

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.TokenExpiryMinutes);
        var token = IssueToken(request.Username, expiresAtUtc);

        _logger.LogInformation("User {Username} logged in", request.Username);

        return Ok(new LoginResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            Username = request.Username
        });
    }

    private string IssueToken(string username, DateTime expiresAtUtc)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[] { new Claim(ClaimTypes.Name, username) };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
