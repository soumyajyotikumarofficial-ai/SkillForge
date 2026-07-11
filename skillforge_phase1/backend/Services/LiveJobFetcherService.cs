using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SkillForge.Data;
using SkillForge.Models;

namespace SkillForge.API.Services;

public class LiveJobFetcherService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LiveJobFetcherService> _logger;
    private readonly IConfiguration _configuration;

    public LiveJobFetcherService(
        IServiceProvider serviceProvider, 
        IHttpClientFactory httpClientFactory, 
        ILogger<LiveJobFetcherService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Live Job Fetcher Background Worker initialized.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Waking up daily background sync execution stream...");
                await RefreshLiveJobsDatabaseAsync();
                _logger.LogInformation("Daily job synchronization complete.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating live job entries.");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RefreshLiveJobsDatabaseAsync()
    {
         _logger.LogInformation("Job Count Live");
        var liveJobs = await FetchJobsFromExternalApiAsync(".NET Developer Python Developer Java");
         _logger.LogInformation($"Job Count Live:{liveJobs}");
        if (liveJobs.Count == 0) return;

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SkillForgeDbContext>();
            
            var existingJobs = await dbContext.Jobs.ToListAsync();
            dbContext.Jobs.RemoveRange(existingJobs);
            await dbContext.SaveChangesAsync();

            foreach (var extJob in liveJobs)
            {
                dbContext.Jobs.Add(new Job
                {
                    Title = extJob.Title,
                    CompanyName = extJob.Company,
                    Location = extJob.Location,
                    Description = extJob.Description,
                    SalaryRange = extJob.Salary,
                    ApplyUrl = extJob.ApplyUrl 
                });
            }

            await dbContext.SaveChangesAsync();
        }
    }

    
   private async Task<List<ExternalApiJobDto>> FetchJobsFromExternalApiAsync(string searchKeywords)
{
    var discoveredJobs = new List<ExternalApiJobDto>();
    var client = _httpClientFactory.CreateClient();
    
    // 1. Build the URI - ensure no extra spaces
    var url = $"https://jsearch.p.rapidapi.com/search-v2?query={Uri.EscapeDataString(searchKeywords)}&num_pages=1&country=us&date_posted=all";
    var request = new HttpRequestMessage(HttpMethod.Get, url);

    // 2. Add headers explicitly (safer than initializer syntax)
    var apiKey = _configuration["RapidAPI:Key"];
    
    if (string.IsNullOrEmpty(apiKey))
    {
        _logger.LogError("RapidAPI Key is null or empty in appsettings.json!");
        return discoveredJobs;
    }

    request.Headers.Add("x-rapidapi-key", apiKey);
    request.Headers.Add("x-rapidapi-host", "jsearch.p.rapidapi.com");

    // 3. Debug logging to verify the request
    _logger.LogInformation("Sending API request to: {Url}", url);

    try
    {
        using (var response = await client.SendAsync(request))
        {
            // Log the status code immediately to help debug the 404
            _logger.LogInformation("API response status: {StatusCode}", response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var jsonDocument = JsonDocument.Parse(responseContent);
                
                // Ensure "data" exists and is an array
                if (jsonDocument.RootElement.TryGetProperty("data", out var dataRoot) && dataRoot.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in dataRoot.EnumerateArray())
                    {
                        discoveredJobs.Add(new ExternalApiJobDto
                        {
                            Title = element.TryGetProperty("job_title", out var t) ? t.GetString() : "Software Engineer",
                            Company = element.TryGetProperty("employer_name", out var c) ? c.GetString() : "Tech Company",
                            Location = element.TryGetProperty("job_city", out var l) ? l.GetString() : "Remote",
                            Description = element.TryGetProperty("job_description", out var d) ? d.GetString() : "",
                            Salary = element.TryGetProperty("job_min_salary", out var s) ? $"${s.GetRawText()}/yr" : "Competitive",
                            ApplyUrl = element.TryGetProperty("job_apply_link", out var urlProp) ? urlProp.GetString() : "https://linkedin.com"
                        });
                    }
                }
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("API Error (Status {Status}): {Body}", response.StatusCode, errorBody);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Exception occurred during API request.");
    }

    return discoveredJobs;
}
}
public class ExternalApiJobDto
{
    public string Title { get; set; }
    public string Company { get; set; }
    public string Location { get; set; }
    public string Description { get; set; }
    public string Salary { get; set; }
    public string ApplyUrl { get; set; }
}
