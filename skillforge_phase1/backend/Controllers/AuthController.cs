using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SkillForge.API.Models;
using SkillForge.Data;
using User = SkillForge.Models.User;

namespace SkillForge.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(SkillForgeDbContext dbContext, IConfiguration configuration, ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required." });
        }

        var role = string.Equals(request.Role, "Recruiter", StringComparison.OrdinalIgnoreCase) ? "Recruiter" : "Candidate";
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // Preserve the candidate-chosen username when supplied; otherwise fall back to the email's local part.
        var username = string.IsNullOrWhiteSpace(request.Name) ? normalizedEmail : request.Name.Trim();

        if (_dbContext.Users.Any(u => u.Email.ToLower() == normalizedEmail))
        {
            return Conflict(new { error = "A user with this email already exists." });
        }

        if (_dbContext.Users.Any(u => u.Username.ToLower() == username.ToLower()))
        {
            return Conflict(new { error = "A user with this username already exists." });
        }

        var user = new SkillForge.Models.User
        {
            Username = username,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Registered new user {Email} with role {Role}", normalizedEmail, role);
        return Ok(new { success = true, userId = user.UserId, role = user.Role });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required." });
        }

        // Accept either the account email or the chosen username in the same "Email" field,
        // since the login form only presents a single identifier input.
        var normalizedIdentifier = request.Email.Trim().ToLowerInvariant();
        var user = _dbContext.Users.FirstOrDefault(u =>
            u.Email.ToLower() == normalizedIdentifier || u.Username.ToLower() == normalizedIdentifier);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid email/username or password." });
        }

        var token = GenerateJwtToken(user);
        return Ok(new { success = true, token, userId = user.UserId, role = user.Role });
    }

    private string GenerateJwtToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? "your-secret-key-must-be-at-least-32-characters-long-here!!!!";
        var jwtIssuer = _configuration["Jwt:Issuer"] ?? "SkillForge";
        var jwtAudience = _configuration["Jwt:Audience"] ?? "SkillForgeUsers";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
