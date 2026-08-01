using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SkillForge.Data;
using SkillForge.DTOs;
using SkillForge.Models;

namespace SkillForge.Controllers;

/// <summary>
/// Feature 4: recruiter authentication, fully separated from the candidate Auth routes/table.
/// </summary>
[ApiController]
[Route("api/recruiter/auth")]
public class RecruiterAuthController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecruiterAuthController> _logger;

    public RecruiterAuthController(SkillForgeDbContext dbContext, IConfiguration configuration, ILogger<RecruiterAuthController> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RecruiterRegisterDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required." });
        }
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest(new { error = "Full name and company name are required." });
        }
        if (request.Password.Length < 6)
        {
            return BadRequest(new { error = "Password must be at least 6 characters long." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (_dbContext.Recruiters.Any(r => r.Email.ToLower() == normalizedEmail))
        {
            return Conflict(new { error = "A recruiter account with this email already exists." });
        }

        var recruiter = new Recruiter
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CompanyName = request.CompanyName.Trim(),
            Designation = request.Designation?.Trim() ?? "",
            PhoneNumber = request.PhoneNumber?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Recruiters.Add(recruiter);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Registered new recruiter {Email} for company {Company}", normalizedEmail, recruiter.CompanyName);
        return Ok(new { success = true, recruiterId = recruiter.Id });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] RecruiterLoginDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Email and password are required." });
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var recruiter = _dbContext.Recruiters.FirstOrDefault(r => r.Email.ToLower() == normalizedEmail);

        if (recruiter == null || !BCrypt.Net.BCrypt.Verify(request.Password, recruiter.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        var token = GenerateJwtToken(recruiter);
        return Ok(new
        {
            success = true,
            token,
            recruiterId = recruiter.Id,
            fullName = recruiter.FullName,
            companyName = recruiter.CompanyName
        });
    }

    private string GenerateJwtToken(Recruiter recruiter)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? "your-secret-key-must-be-at-least-32-characters-long-here!!!!";
        var jwtIssuer = _configuration["Jwt:Issuer"] ?? "SkillForge";
        var jwtAudience = _configuration["Jwt:Audience"] ?? "SkillForgeUsers";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, recruiter.Id.ToString()),
            new Claim(ClaimTypes.Name, recruiter.FullName),
            new Claim(ClaimTypes.Email, recruiter.Email),
            new Claim(ClaimTypes.Role, "Recruiter"),
            new Claim("companyName", recruiter.CompanyName)
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
