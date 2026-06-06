using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});
builder.Services.AddSingleton<SkillForge.API.Services.AIService>();

var app = builder.Build();
app.UseCors();
app.MapControllers();
app.MapGet("/", () => Results.Json(new { message = "SkillForge API placeholder" }));
app.Run();
