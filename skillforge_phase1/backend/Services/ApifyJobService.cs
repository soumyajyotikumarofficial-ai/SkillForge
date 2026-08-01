using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SkillForge.API.Services;

/// <summary>
/// Normalized live job record produced from a raw Apify Job Scraper actor dataset item,
/// regardless of the exact field names used by the underlying actor.
/// </summary>
public class ApifyJobResult
{
    public string Title { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Location { get; set; } = "";
    public string Country { get; set; } = "";
    public string Description { get; set; } = "";
    public string SalaryRange { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public DateTime? SourceCreatedAt { get; set; }
    public string ApplyUrl { get; set; } = "";
    public string FinalUrl { get; set; } = "";
    public string Benefits { get; set; } = ""; // Flattened, comma-separated
}

/// <summary>
/// Centralized client for the Apify Job Scraper actor. Replaces the legacy JSearch/RapidAPI
/// integration previously duplicated across AIService, LiveJobFetcherService and CandidateController.
/// </summary>
public class ApifyJobService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ApifyJobService> _logger;

    public ApifyJobService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ApifyJobService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Runs the configured Apify Job Scraper actor synchronously (run-sync-get-dataset-items) and
    /// returns the resulting dataset items mapped into normalized <see cref="ApifyJobResult"/> records.
    /// Returns an empty list (never null) if configuration is missing or the run fails.
    /// </summary>
    public async Task<List<ApifyJobResult>> FetchJobsAsync(string searchQuery, string location = "United States", string country = "US", int maxItems = 25)
    {
        var discoveredJobs = new List<ApifyJobResult>();

        var apiToken = _config["Apify:ApiToken"];
        var actorId = _config["Apify:ActorId"] ?? "misceres~indeed-scraper";
        var baseUrl = _config["Apify:BaseUrl"] ?? "https://api.apify.com/v2";

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogError("❌ [APIFY] Configuration Error: 'Apify:ApiToken' is empty or missing. Aborting synchronization.");
            return discoveredJobs;
        }

        try
        {
            _logger.LogInformation("🔄 [APIFY] Initializing live synchronization process for query: '{Query}' in '{Location}, {Country}'", searchQuery, location, country);

            var client = _httpFactory.CreateClient();
            var requestUrl = $"{baseUrl}/acts/{actorId}/run-sync-get-dataset-items?token={Uri.EscapeDataString(apiToken)}";

            // Defensive input shape: covers the commonly used field names across Apify job-scraper actors.
            // followApplyRedirects makes the Indeed scraper follow its own redirect chain and return the
            // true final destination (the company's own career site) instead of an indeed.com link.
            var inputPayload = new
            {
                position = searchQuery,
                query = searchQuery,
                search = searchQuery,
                location = location,
                country = country,
                maxItems = maxItems,
                maxResults = maxItems,
                followApplyRedirects = true
            };

            var json = JsonSerializer.Serialize(inputPayload);
            _logger.LogInformation("🌐 [APIFY] Dispatching actor run '{Actor}' to: {Url}", actorId, requestUrl);

            var response = await client.PostAsync(requestUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("❌ [APIFY] Actor run rejected. Status Code: {Code}, Server Message: {Message}", response.StatusCode, responseText);
                return discoveredJobs;
            }

            using var document = JsonDocument.Parse(responseText);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogError("❌ [APIFY DETAILED ERROR] Dataset payload was not the expected JSON array.\n🔴 RAW PAYLOAD FROM SERVER:\n{RawJson}", responseText);
                return discoveredJobs;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                discoveredJobs.Add(MapDatasetItem(item));
            }

            _logger.LogInformation("📊 [APIFY] Actor run complete. Extracted [{Count}] live jobs for query variant: '{Query}'", discoveredJobs.Count, searchQuery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [APIFY CRITICAL FAILURE] Actor invocation aborted due to an internal execution crash.");
        }

        return discoveredJobs;
    }

    /// <summary>
    /// Defensively maps a single raw Apify dataset item into a normalized job record, tolerating
    /// the differing field naming conventions used across various Apify job-scraper actors.
    /// </summary>
    private ApifyJobResult MapDatasetItem(JsonElement item)
    {
        string title = GetFirstString(item, "positionName", "title", "jobTitle", "job_title") ?? "Unknown Position";
        string company = GetFirstString(item, "company", "companyName", "employer_name") ?? "Unknown Company";
        string city = GetFirstString(item, "location", "city", "job_city") ?? "";
        string country = GetFirstString(item, "country", "job_country") ?? "";
        string location = !string.IsNullOrWhiteSpace(city)
            ? city
            : (!string.IsNullOrWhiteSpace(country) ? country : "Remote");

        string description = GetFirstString(item, "description", "descriptionText", "job_description") ?? "";
        string salary = GetFirstString(item, "salary", "salaryRange", "job_salary_string") ?? "";

        // Feature 1: only ever surface a genuine direct-to-company application link. "url"/"finalUrl" for this
        // actor point back at the indeed.com listing itself, so they are deliberately excluded here - if no
        // externalApplyLink is present (or it still resolves to an aggregator domain), ApplyUrl is left empty
        // rather than silently redirecting candidates to Indeed/LinkedIn/etc.
        string rawApplyUrl = GetFirstString(item, "externalApplyLink", "applyUrl", "job_apply_link") ?? "";
        string applyUrl = IsAggregatorUrl(rawApplyUrl) ? "" : rawApplyUrl;

        string benefits = "";
        if (TryGetArray(item, out var benefitsArray, "benefits", "job_benefits", "perks"))
        {
            var flattened = benefitsArray.EnumerateArray()
                .Select(b => b.ValueKind == JsonValueKind.String ? b.GetString() : b.ToString())
                .Where(b => !string.IsNullOrWhiteSpace(b));
            benefits = string.Join(", ", flattened);
        }

        return new ApifyJobResult
        {
            Title = title.Trim(),
            CompanyName = company.Trim(),
            Location = location.Trim(),
            Country = country.Trim(),
            Description = description,
            SalaryRange = string.IsNullOrWhiteSpace(salary) ? "NA" : salary.Trim(),
            Currency = GetCurrencyFromCountry(country),
            SourceCreatedAt = ParseSourceCreatedAt(item),
            ApplyUrl = applyUrl.Trim(),
            FinalUrl = applyUrl.Trim(),
            Benefits = benefits
        };
    }

    private static string? GetFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
            {
                var value = prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }

    // Job aggregator/board domains that should never be surfaced as a "direct apply" URL.
    private static readonly string[] AggregatorDomains =
    {
        "indeed.com", "linkedin.com", "glassdoor.com", "ziprecruiter.com",
        "monster.com", "simplyhired.com", "careerbuilder.com", "ncr.indeed.com"
    };

    private static bool IsAggregatorUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return true;

        var host = uri.Host.ToLowerInvariant();
        return AggregatorDomains.Any(domain => host == domain || host.EndsWith("." + domain));
    }

    private static DateTime? ParseSourceCreatedAt(JsonElement element)
    {
        foreach (var name in new[] { "postedAt", "datePosted", "createdAt", "jobPostedDate", "publishedDate" })
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var rawValue = prop.GetString();
                if (!string.IsNullOrWhiteSpace(rawValue) && DateTime.TryParse(rawValue, out var parsed))
                {
                    return parsed.ToUniversalTime();
                }
            }

            if (element.TryGetProperty(name, out prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var epoch))
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static string GetCurrencyFromCountry(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "USD";
        }

        var normalized = country.Trim().ToUpperInvariant();
        return normalized switch
        {
            "IN" or "INDIA" => "INR",
            "US" or "UNITED STATES" => "USD",
            "GB" or "UK" or "UNITED KINGDOM" => "GBP",
            "CA" or "CANADA" => "CAD",
            "AU" or "AUSTRALIA" => "AUD",
            "DE" or "GERMANY" or "FR" or "FRANCE" or "ES" or "SPAIN" or "IT" or "ITALY" or "NL" or "NETHERLANDS" => "EUR",
            "SG" or "SINGAPORE" => "SGD",
            "AE" or "UNITED ARAB EMIRATES" => "AED",
            "JP" or "JAPAN" => "JPY",
            _ => "USD"
        };
    }

    private static bool TryGetArray(JsonElement element, out JsonElement result, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                result = prop;
                return true;
            }
        }
        result = default;
        return false;
    }
}
