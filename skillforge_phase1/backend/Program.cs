using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using SkillForge.Data;
using SkillForge.API.Services;
using SkillForge.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== ENFORCE SYSTEM NETWORK BINDING HUB =====
var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "10000");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, port);
});

// ===== REGISTER MVC FRAMEWORK ENGINE BINDINGS =====
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<SkillForgeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=skillforge.db")
        .ConfigureWarnings(warnings => warnings.Ignore(
            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
);

builder.Services.AddHttpClient();
builder.Services.AddScoped<AIService>();
builder.Services.AddScoped<ApifyJobService>();
builder.Services.AddScoped<CandidateSeedDataService>();
builder.Services.AddScoped<CompanyCareerSeedDataService>();
builder.Services.AddScoped<ICandidateMatchingService, CandidateMatchingService>();
builder.Services.AddSingleton<LiveJobFetcherService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<LiveJobFetcherService>());

// Email and credential encryption services
builder.Services.AddScoped<IEmailService, GmailSmtpService>();
builder.Services.AddScoped<ICredentialEncryptionService, CredentialEncryptionService>();

// Logging configuration for email operations
builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.SetMinimumLevel(LogLevel.Information);
});
// ===== RECRUITER PORTAL AI & NOTIFICATION SERVICES (Feature 5) =====
builder.Services.AddScoped<RecruiterAIService>();
builder.Services.AddScoped<ICompanyDescriptionService>(sp => sp.GetRequiredService<RecruiterAIService>());
builder.Services.AddScoped<IProjectTeamPlannerService>(sp => sp.GetRequiredService<RecruiterAIService>());
builder.Services.AddScoped<IRecruiterCandidateMatchingService>(sp => sp.GetRequiredService<RecruiterAIService>());
builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();

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
builder.Services.AddAuthorization();
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

// ===== DATABASE SEED & ASSURANCE INITIALIZATION WITH STARTUP SYNC =====
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<SkillForgeDbContext>();
    var logger = services.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Apply pending migrations instead of EnsureCreated so schema changes reach the existing db file.
        db.Database.Migrate();
        logger.LogInformation("✅ Database initialized successfully");
        
        // Seed candidates with rich data
        var seedService = services.GetRequiredService<CandidateSeedDataService>();
        seedService.SeedCandidatesAsync().Wait();
        logger.LogInformation("✅ Candidate seed data completed");
        
        // Seed company career pages
        var companySeedService = services.GetRequiredService<CompanyCareerSeedDataService>();
        companySeedService.SeedCompanyCareersAsync().Wait();
        logger.LogInformation("✅ Company career pages seeded");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ [STARTUP] Initialization lifecycle critical failure encountered");
    }
}

app.Run();