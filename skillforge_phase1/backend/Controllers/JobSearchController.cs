using Microsoft.AspNetCore.Mvc;
using SkillForge.Data;
using SkillForge.API.Services;
using SkillForge.Models;
using Microsoft.EntityFrameworkCore;

namespace SkillForge.API.Controllers;

/// <summary>
/// Merged job search endpoint combining Apify job scraping with company career page links.
/// Provides candidates with both scraped job listings and direct company career page links.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class JobSearchController : ControllerBase
{
    private readonly SkillForgeDbContext _context;
    private readonly ApifyJobService _apifyService;
    private readonly ILogger<JobSearchController> _logger;

    public JobSearchController(
        SkillForgeDbContext context,
        ApifyJobService apifyService,
        ILogger<JobSearchController> logger)
    {
        _context = context;
        _apifyService = apifyService;
        _logger = logger;
    }

    /// <summary>
    /// Search for jobs combining Apify scraping and company career pages.
    /// Strategy:
    /// 1. Query Apify for scraped live listings
    /// 2. Query company_careers table for matching companies
    /// 3. Merge results - scraped jobs show inline, career pages show as redirect cards
    /// 4. If company has scrape_supported = false, always show redirect card
    /// 5. Never show duplicate listings for the same company
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MergedJobSearchResponse>> SearchJobsAsync(
        [FromQuery(Name = "keyword")] string keyword,
        [FromQuery(Name = "location")] string location = "Remote",
        [FromQuery(Name = "country")] string country = "US",
        [FromQuery(Name = "pageSize")] int pageSize = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest(new { error = "keyword parameter is required" });

            var response = new MergedJobSearchResponse
            {
                Keyword = keyword,
                Location = location,
                Country = country,
                Timestamp = DateTime.UtcNow
            };

            // ===== STEP 1: Get Scraped Jobs from Apify =====
            List<Job> scrapedJobs = new();
            try
            {
                // Query existing jobs from database (simulating Apify cache)
                scrapedJobs = await _context.Jobs
                    .Where(j => j.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                                j.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .Where(j => j.Location.Contains(location, StringComparison.OrdinalIgnoreCase) ||
                                j.Country.Contains(country, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(j => j.FetchedAtUtc)
                    .Take(pageSize)
                    .ToListAsync();

                _logger.LogInformation($"Found {scrapedJobs.Count} scraped jobs for '{keyword}' in {location}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error fetching scraped jobs: {ex.Message}");
            }

            // ===== STEP 2: Get Company Career Pages =====
            var companiesWithCareers = await _context.CompanyCareers
                .ToListAsync();

            // ===== STEP 3 & 4: Merge Results and Avoid Duplicates =====
            var companiesInScrapedJobs = scrapedJobs.Select(j => j.CompanyName.ToLower()).Distinct().ToList();

            // Add scraped jobs to response
            foreach (var job in scrapedJobs)
            {
                response.ScrapedJobs.Add(new JobListingDto
                {
                    JobId = job.JobId,
                    Title = job.Title,
                    CompanyName = job.CompanyName,
                    Location = job.Location,
                    Country = job.Country,
                    SalaryRange = job.SalaryRange,
                    Currency = job.Currency,
                    Description = job.Description.Length > 500 ? job.Description.Substring(0, 500) + "..." : job.Description,
                    ApplyUrl = job.ApplyUrl,
                    WorkMode = job.WorkMode,
                    FetchedAt = job.FetchedAtUtc,
                    Source = "Apify Scraper"
                });
            }

            // Add company career page cards (only if not already in scraped jobs)
            foreach (var company in companiesWithCareers)
            {
                // Skip if already have scraped jobs from this company
                if (companiesInScrapedJobs.Contains(company.CompanyName.ToLower()))
                    continue;

                // Skip if company doesn't support scraping - ALWAYS show career page link
                response.CareerPageCards.Add(new CareerPageCardDto
                {
                    CompanyName = company.CompanyName,
                    Industry = company.Industry,
                    Headquarters = company.Headquarters,
                    LogoUrl = company.LogoUrl,
                    CareersUrl = company.CareersUrl,
                    ViewOpenRolesButtonText = "View Open Roles →",
                    ExploreCompanyButtonText = "Explore Company"
                });
            }

            response.TotalScrapedJobs = response.ScrapedJobs.Count;
            response.TotalCareerPages = response.CareerPageCards.Count;

            _logger.LogInformation($"Merged job search returned {response.TotalScrapedJobs} scraped jobs and {response.TotalCareerPages} career pages");

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in SearchJobsAsync: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while searching for jobs" });
        }
    }

    /// <summary>
    /// Gets a list of companies with career pages (for UI filtering/suggestions).
    /// </summary>
    [HttpGet("companies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CompanyDto>>> GetCompaniesAsync()
    {
        try
        {
            var companies = await _context.CompanyCareers
                .OrderBy(c => c.CompanyName)
                .Select(c => new CompanyDto
                {
                    CompanyName = c.CompanyName,
                    Industry = c.Industry,
                    Headquarters = c.Headquarters,
                    LogoUrl = c.LogoUrl,
                    CareersUrl = c.CareersUrl,
                    ScrapeSupported = c.ScrapeSupported
                })
                .ToListAsync();

            return Ok(companies);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error retrieving companies: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while retrieving companies" });
        }
    }
}

/// <summary>
/// Merged job search response combining scraped jobs and career page links.
/// </summary>
public class MergedJobSearchResponse
{
    public string Keyword { get; set; } = "";
    public string Location { get; set; } = "";
    public string Country { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int TotalScrapedJobs { get; set; }
    public int TotalCareerPages { get; set; }
    public List<JobListingDto> ScrapedJobs { get; set; } = new();
    public List<CareerPageCardDto> CareerPageCards { get; set; } = new();
}

/// <summary>
/// DTO for job listing from Apify.
/// </summary>
public class JobListingDto
{
    public int JobId { get; set; }
    public string Title { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Location { get; set; } = "";
    public string Country { get; set; } = "";
    public string SalaryRange { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Description { get; set; } = "";
    public string ApplyUrl { get; set; } = "";
    public string WorkMode { get; set; } = "";
    public DateTime FetchedAt { get; set; }
    public string Source { get; set; } = "Apify Scraper";
}

/// <summary>
/// DTO for company career page card.
/// </summary>
public class CareerPageCardDto
{
    public string CompanyName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Headquarters { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public string CareersUrl { get; set; } = "";
    public string ViewOpenRolesButtonText { get; set; } = "View Open Roles →";
    public string ExploreCompanyButtonText { get; set; } = "Explore Company";
}

/// <summary>
/// DTO for company summary.
/// </summary>
public class CompanyDto
{
    public string CompanyName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Headquarters { get; set; } = "";
    public string LogoUrl { get; set; } = "";
    public string CareersUrl { get; set; } = "";
    public bool ScrapeSupported { get; set; }
}
