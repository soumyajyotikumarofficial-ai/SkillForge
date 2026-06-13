using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SkillForge.Models;
using SkillForge.Data;

namespace SkillForge.Services;

public class AuthService
{
    private readonly SkillForgeDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(SkillForgeDbContext context, IConfiguration config, ILogger<AuthService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    public async Task<(bool Success, string Message, User? User)> RegisterAsync(string username, string email, string password, string role = "Candidate")
    {
        try
        {
            // Check if user exists
            if (_context.Users.Any(u => u.Username == username))
                return (false, "Username already exists", null);

            // Hash password
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = passwordHash,
                Role = role
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User registered: {Username}", username);
            return (true, "Registration successful", user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration error");
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Success, string Token, string Message, User? User)> LoginAsync(string username, string password)
    {
        try
        {
            var user = _context.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
                return (false, "", "Invalid username or password", null);

            // Verify password
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return (false, "", "Invalid username or password", null);

            // Generate JWT token
            var token = GenerateJwtToken(user);

            _logger.LogInformation("User logged in: {Username}", username);
            return (true, token, "Login successful", user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            return (false, "", ex.Message, null);
        }
    }

    private string GenerateJwtToken(User user)
    {
        var key = _config["Jwt:Key"] ?? "your-secret-key-at-least-32-characters-long!";
        var issuer = _config["Jwt:Issuer"] ?? "SkillForge";
        var audience = _config["Jwt:Audience"] ?? "SkillForgeUsers";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}