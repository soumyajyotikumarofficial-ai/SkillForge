using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillForge.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddLogging(config =>
{
    config.SetMinimumLevel(LogLevel.Information);
    config.AddConsole();
    config.AddDebug();
});

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<AIService>();

// Add CORS - Allow frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:3000",
                "http://127.0.0.1:3000"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Use CORS
app.UseCors("AllowFrontend");
app.UseRouting();

// Map controllers
app.MapControllers();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "OK" }));
app.MapGet("/", () => Results.Json(new { message = "SkillForge API Running" }));

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting SkillForge API");

app.Run();