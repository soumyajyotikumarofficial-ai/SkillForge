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
    private readonly ApifyJobService _apifyJobService;

    public LiveJobFetcherService(
        IServiceProvider serviceProvider, 
        IHttpClientFactory httpClientFactory, 
        ILogger<LiveJobFetcherService> logger,
        IConfiguration configuration,
        ApifyJobService apifyJobService)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _apifyJobService = apifyJobService;
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
        var liveJobs = await _apifyJobService.FetchJobsAsync(".NET Developer Python Developer Java");
        _logger.LogInformation($"Job Count Live:{liveJobs.Count}");
        if (liveJobs.Count == 0) return;

        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SkillForgeDbContext>();

            // Strict de-duplication requires evaluating Title, CompanyName, AND Location together.
            var existingJobs = await dbContext.Jobs
                .Select(j => new { Title = j.Title.Trim(), CompanyName = j.CompanyName.Trim(), Location = j.Location.Trim() })
                .ToListAsync();

            int ingestedCounter = 0;
            int skippedCounter = 0;

            foreach (var extJob in liveJobs)
            {
                string title = extJob.Title.Trim();
                string company = extJob.CompanyName.Trim();
                string location = extJob.Location.Trim();

                bool jobExists = existingJobs.Any(ej => ej.Title.Equals(title, StringComparison.OrdinalIgnoreCase)
                                                     && ej.CompanyName.Equals(company, StringComparison.OrdinalIgnoreCase)
                                                     && ej.Location.Equals(location, StringComparison.OrdinalIgnoreCase));
                if (jobExists)
                {
                    skippedCounter++;
                    continue;
                }

                dbContext.Jobs.Add(new Job
                {
                    Title = title,
                    CompanyName = company,
                    Location = location,
                    Country = extJob.Country,
                    Description = extJob.Description,
                    SalaryRange = string.IsNullOrWhiteSpace(extJob.SalaryRange) ? "Competitive / Market Rate" : extJob.SalaryRange,
                    ApplyUrl = extJob.ApplyUrl,
                    Benefits = extJob.Benefits
                });

                existingJobs.Add(new { Title = title, CompanyName = company, Location = location });
                ingestedCounter++;
            }

            if (ingestedCounter > 0)
            {
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("🚀 [APIFY SYNC] Ingested {Ingested} fresh unique jobs. Duplicates skipped: {Skipped}.", ingestedCounter, skippedCounter);
        }
    }
}
