using System;
using System.Collections.Generic;
using System.Linq;
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
    private sealed record GeoTarget(string City, params string[] StateHints);
    public sealed record JobSyncResult(
        int InsertedCount,
        int SkippedCount,
        int FilteredByDateCount,
        int DuplicateCount,
        int NonItFilteredCount,
        int GeoMismatchCount,
        int ApiFetchedCount,
        int TargetCombinationCount,
        DateTime ExecutedAtUtc,
        bool ForcedFullFetch);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LiveJobFetcherService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private static readonly Dictionary<string, GeoTarget[]> CountryTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IN"] = new[]
        {
            new GeoTarget("Bengaluru", "Karnataka"),
            new GeoTarget("Chennai", "Tamil Nadu"),
            new GeoTarget("Gurugram", "Haryana"),
            new GeoTarget("Hyderabad", "Telangana"),
            new GeoTarget("Kolkata", "West Bengal"),
            new GeoTarget("Mumbai", "Maharashtra"),
            new GeoTarget("Noida", "Uttar Pradesh"),
            new GeoTarget("Pune", "Maharashtra")
        },
        ["US"] = new[]
        {
            new GeoTarget("Austin", "Texas"),
            new GeoTarget("New York", "New York"),
            new GeoTarget("San Francisco", "California"),
            new GeoTarget("Seattle", "Washington")
        },
        ["GB"] = new[] { new GeoTarget("London", "England") },
        ["CA"] = new[]
        {
            new GeoTarget("Toronto", "Ontario"),
            new GeoTarget("Vancouver", "British Columbia")
        },
        ["DE"] = new[]
        {
            new GeoTarget("Berlin", "Berlin"),
            new GeoTarget("Munich", "Bavaria")
        },
        ["SG"] = new[] { new GeoTarget("Singapore", "Singapore") }
    };

    public LiveJobFetcherService(
        IServiceProvider serviceProvider, 
        ILogger<LiveJobFetcherService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
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
                var delay = GetDelayUntilNextNineAm();
                _logger.LogInformation("Next scheduled job synchronization will run in {Delay}.", delay);
                await Task.Delay(delay, stoppingToken);

                _logger.LogInformation("Running scheduled 9 AM background sync execution stream...");
                await RunSyncAsync(stoppingToken);
                _logger.LogInformation("Daily job synchronization complete.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating live job entries.");
            }
        }
    }

    public Task<JobSyncResult> RunManualSyncAsync(CancellationToken cancellationToken = default)
    {
        return RunSyncAsync(cancellationToken);
    }

    public Task<JobSyncResult> RunManualSyncAsync(bool forceFullFetch, CancellationToken cancellationToken = default)
    {
        return RunSyncAsync(cancellationToken, forceFullFetch);
    }

    private async Task<JobSyncResult> RunSyncAsync(CancellationToken cancellationToken, bool forceFullFetch = false)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SkillForgeDbContext>();
        var apifyJobService = scope.ServiceProvider.GetRequiredService<ApifyJobService>();

        var defaultQueries = (_configuration["Apify:DailySearchQueries"]
            ?? "Software Engineer,.NET Developer,Java Developer,Python Developer,Full Stack Developer,Backend Developer,Frontend Developer,DevOps Engineer,Data Engineer,QA Engineer,Automation Tester,React Developer,Angular Developer")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var configuredLocations = (_configuration["Apify:DailyLocations"] ?? "Kolkata,Bengaluru,Hyderabad,Pune,Chennai,Gurugram,Noida,Mumbai")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var targetCountries = (_configuration["Apify:DailyCountries"] ?? _configuration["Apify:DailyCountry"] ?? "IN")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        var maxItems = _configuration.GetValue<int?>("Apify:DailyFetchLimit") ?? 100;

        var lastFetchUtc = await dbContext.JobFetchHistories
            .OrderByDescending(h => h.LastSuccessfulFetchUtc)
            .Select(h => (DateTime?)h.LastSuccessfulFetchUtc)
            .FirstOrDefaultAsync();

        var existingJobs = await dbContext.Jobs
            .Select(j => new { Title = j.Title.Trim(), CompanyName = j.CompanyName.Trim(), Location = j.Location.Trim() })
            .ToListAsync();

        var insertedCount = 0;
        var skippedCount = 0;
        var filteredByDateCount = 0;
        var duplicateCount = 0;
        var nonItFilteredCount = 0;
        var geoMismatchCount = 0;
        var apiFetchedCount = 0;
        var targetCombinationCount = 0;

        foreach (var searchQuery in defaultQueries)
        {
            foreach (var targetCountry in targetCountries)
            {
                var allowedTargets = GetTargetsForCountry(targetCountry, configuredLocations);
                if (allowedTargets.Count == 0)
                {
                    continue;
                }

                foreach (var geoTarget in allowedTargets)
                {
                    if (insertedCount >= maxItems)
                    {
                        goto SyncComplete;
                    }

                    targetCombinationCount++;
                    var liveJobs = await apifyJobService.FetchJobsAsync(searchQuery, geoTarget.City, targetCountry, maxItems);
                    apiFetchedCount += liveJobs.Count;

                    foreach (var extJob in liveJobs)
                    {
                        if (insertedCount >= maxItems)
                        {
                            goto SyncComplete;
                        }

                        if (!IsItRelatedJob(extJob))
                        {
                            nonItFilteredCount++;
                            skippedCount++;
                            continue;
                        }

                        if (!MatchesGeoTarget(extJob, targetCountry, geoTarget))
                        {
                            geoMismatchCount++;
                            skippedCount++;
                            continue;
                        }

                        if (!forceFullFetch && lastFetchUtc.HasValue && extJob.SourceCreatedAt.HasValue && extJob.SourceCreatedAt.Value <= lastFetchUtc.Value)
                        {
                            filteredByDateCount++;
                            continue;
                        }

                        string title = extJob.Title.Trim();
                        string company = extJob.CompanyName.Trim();
                        string locationName = extJob.Location.Trim();

                        bool jobExists = existingJobs.Any(ej => ej.Title.Equals(title, StringComparison.OrdinalIgnoreCase)
                                                             && ej.CompanyName.Equals(company, StringComparison.OrdinalIgnoreCase)
                                                             && ej.Location.Equals(locationName, StringComparison.OrdinalIgnoreCase));
                        if (jobExists)
                        {
                            duplicateCount++;
                            skippedCount++;
                            continue;
                        }

                        dbContext.Jobs.Add(new Job
                        {
                            Title = title,
                            CompanyName = company,
                            Location = locationName,
                            Country = string.IsNullOrWhiteSpace(extJob.Country) ? targetCountry : extJob.Country,
                            Description = extJob.Description,
                            SalaryRange = string.IsNullOrWhiteSpace(extJob.SalaryRange) ? "NA" : extJob.SalaryRange,
                            Currency = string.IsNullOrWhiteSpace(extJob.Currency) ? "INR" : extJob.Currency,
                            ApplyUrl = string.IsNullOrWhiteSpace(extJob.ApplyUrl) ? extJob.FinalUrl : extJob.ApplyUrl,
                            FinalUrl = string.IsNullOrWhiteSpace(extJob.FinalUrl) ? extJob.ApplyUrl : extJob.FinalUrl,
                            WorkMode = DetermineWorkMode(extJob.Description, locationName),
                            Benefits = extJob.Benefits,
                            CreatedAt = DateTime.UtcNow,
                            FetchedAtUtc = DateTime.UtcNow,
                            SourceCreatedAt = extJob.SourceCreatedAt,
                            RequiredSkills = new List<JobSkill>()
                        });

                        existingJobs.Add(new { Title = title, CompanyName = company, Location = locationName });
                        insertedCount++;
                    }
                }
            }
        }

SyncComplete:

        dbContext.JobFetchHistories.Add(new JobFetchHistory
        {
            LastSuccessfulFetchUtc = DateTime.UtcNow,
            LastQuery = string.Join(", ", defaultQueries),
            LastLocation = string.Join(", ", configuredLocations),
            LastCountry = string.Join(", ", targetCountries),
            InsertedCount = insertedCount,
            SkippedCount = skippedCount + filteredByDateCount,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
        _logger.LogInformation("🚀 [APIFY SYNC] Ingested {Inserted} fresh IT jobs. API fetched: {ApiFetched}. Duplicates: {Duplicates}. Non-IT filtered: {NonIt}. Geo mismatches: {GeoMismatch}. Older jobs filtered: {FilteredByDate}. Forced: {Forced}", insertedCount, apiFetchedCount, duplicateCount, nonItFilteredCount, geoMismatchCount, filteredByDateCount, forceFullFetch);
        return new JobSyncResult(
            insertedCount,
            skippedCount,
            filteredByDateCount,
            duplicateCount,
            nonItFilteredCount,
            geoMismatchCount,
            apiFetchedCount,
            targetCombinationCount,
            DateTime.UtcNow,
            forceFullFetch);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private static TimeSpan GetDelayUntilNextNineAm()
    {
        var now = DateTime.Now;
        var nextRun = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0, now.Kind);

        if (now >= nextRun)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun - now;
    }

    private static List<GeoTarget> GetTargetsForCountry(string countryCode, IReadOnlyCollection<string> configuredLocations)
    {
        if (!CountryTargets.TryGetValue(countryCode, out var countryTargets))
        {
            return new List<GeoTarget>();
        }

        return countryTargets
            .Where(target => configuredLocations.Contains(target.City, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool MatchesGeoTarget(ApifyJobResult job, string targetCountry, GeoTarget geoTarget)
    {
        if (!string.IsNullOrWhiteSpace(job.Country)
            && !job.Country.Equals(targetCountry, StringComparison.OrdinalIgnoreCase)
            && !job.Country.Contains(targetCountry, StringComparison.OrdinalIgnoreCase)
            && !targetCountry.Contains(job.Country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var locationText = job.Location ?? string.Empty;
        if (locationText.Contains(geoTarget.City, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return geoTarget.StateHints.Any(state => locationText.Contains(state, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsItRelatedJob(ApifyJobResult job)
    {
        var text = $"{job.Title} {job.Description}".ToLowerInvariant();
        string[] keywords =
        {
            "developer", "engineer", "software", "frontend", "backend", "full stack", "fullstack",
            "qa", "tester", "automation", "devops", "cloud", "data", "analytics", "ai", "ml",
            "python", "java", ".net", "react", "angular", "node", "sql", "mobile", "ios", "android"
        };

        return keywords.Any(text.Contains);
    }

    private static string DetermineWorkMode(string description, string locationName)
    {
        var normalized = $"{description} {locationName}".ToLowerInvariant();
        if (normalized.Contains("remote") || normalized.Contains("work from home") || normalized.Contains("wfh"))
        {
            return "WFH";
        }

        if (normalized.Contains("hybrid") || normalized.Contains("partial remote") || normalized.Contains("some office"))
        {
            return "Hybrid";
        }

        return "WFO";
    }
}
