using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using SkillForge.Data;
using SkillForge.API.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== ENFORCE SYSTEM NETWORK BINDING HUB =====
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5123, listenOptions => listenOptions.UseHttps());
    options.ListenLocalhost(5000);
});

// ===== REGISTER MVC FRAMEWORK ENGINE BINDINGS =====
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<SkillForgeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=skillforge.db")
);

builder.Services.AddHttpClient();
builder.Services.AddScoped<AIService>();

// ===== AUTHENTICATION MIDDLEWARE SCHEMAS =====
var jwtKey = builder.Configuration["Jwt:Key"] ?? "your-secret-key-must-be-at-least-32-characters-long-here!!!!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SkillForge";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SkillForgeUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

// ===== CROSS ORIGIN COMPONENT POOL SETUP =====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.AddDebug();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// FIX: Disabled to prevent HTTP 307 redirects from breaking local CORS preflight OPTIONS handshakes
// app.UseHttpsRedirection();

// ===== APPLICATION MIDDLEWARE EXECUTION PIPELINE =====
// 1. Establish the endpoint routing map
app.UseRouting();

// 2. Apply CORS policy directly onto the routing endpoints
app.UseCors("AllowFrontend");

// 3. Authenticate and authorize the context
app.UseAuthentication();
app.UseAuthorization();

// 4. Map the API endpoints to their controllers
app.MapControllers();

// ===== DATABASE SEED & ASSURANCE INITIALIZATION =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SkillForgeDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        db.Database.EnsureCreated();
        logger.LogInformation("✅ Database initialized successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Database initialization error");
    }
}

app.Run();