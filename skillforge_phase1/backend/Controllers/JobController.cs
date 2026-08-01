using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SkillForge.API.Services;
using SkillForge.Data;
using SkillForge.Models;
using System.Security.Claims;

namespace SkillForge.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly ILogger<JobController> _logger;
    private readonly LiveJobFetcherService _liveJobFetcherService;

    public JobController(SkillForgeDbContext dbContext, ILogger<JobController> logger, LiveJobFetcherService liveJobFetcherService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _liveJobFetcherService = liveJobFetcherService;
    }

    [HttpPost("run-daily-sync")]
    [AllowAnonymous]
    public async Task<IActionResult> RunDailySync([FromQuery] bool force = false)
    {
        try
        {
            var result = await _liveJobFetcherService.RunManualSyncAsync(force, HttpContext.RequestAborted);
            return Ok(new
            {
                message = "Manual daily sync completed.",
                result.ForcedFullFetch,
                result.InsertedCount,
                result.SkippedCount,
                result.FilteredByDateCount,
                result.DuplicateCount,
                result.NonItFilteredCount,
                result.GeoMismatchCount,
                result.ApiFetchedCount,
                result.TargetCombinationCount,
                result.ExecutedAtUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running manual daily sync");
            return StatusCode(500, new { error = "Manual daily sync failed.", details = ex.Message });
        }
    }

    [HttpGet("live-sync-status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLiveSyncStatus()
    {
        try
        {
            var latest = await _dbContext.JobFetchHistories
                .OrderByDescending(h => h.LastSuccessfulFetchUtc)
                .Select(h => new
                {
                    h.LastSuccessfulFetchUtc,
                    h.InsertedCount,
                    h.SkippedCount,
                    h.LastQuery,
                    h.LastLocation,
                    h.LastCountry,
                    h.CreatedAt
                })
                .FirstOrDefaultAsync();

            var recentRuns = await _dbContext.JobFetchHistories
                .OrderByDescending(h => h.LastSuccessfulFetchUtc)
                .Take(5)
                .Select(h => new
                {
                    h.LastSuccessfulFetchUtc,
                    h.InsertedCount,
                    h.SkippedCount,
                    h.LastCountry
                })
                .ToListAsync();

            var totalJobs = await _dbContext.Jobs.CountAsync();
            var jobsLast24h = await _dbContext.Jobs.CountAsync(j => j.FetchedAtUtc >= DateTime.UtcNow.AddHours(-24));

            return Ok(new
            {
                totalJobs,
                jobsLast24h,
                latestRun = latest,
                recentRuns
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching live sync status");
            return StatusCode(500, new { error = "Failed to fetch live sync status.", details = ex.Message });
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllJobs()
    {
        try
        {
            var jobs = await _dbContext.Jobs
                .Select(j => new 
                {
                    j.JobId,
                    j.Title,
                    j.Description,
                    j.CompanyName,
                    j.Location,
                    j.SalaryRange,
                    j.Currency,
                    j.WorkMode,
                    j.ApplyUrl,
                    j.FinalUrl,
                    j.Country,
                    j.Benefits,
                    j.CreatedAt,
                    RequiredSkills = j.RequiredSkills.Select(s => new 
                    {
                        s.Id,
                        s.SkillName,
                        s.IsRequired,
                        s.ProficiencyLevel
                    }).ToList()
                })
                .ToListAsync();

            return Ok(jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting jobs");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetJob(int id)
    {
        try
        {
            var job = await _dbContext.Jobs
                .Where(j => j.JobId == id)
                .Select(j => new 
                {
                    j.JobId,
                    j.Title,
                    j.Description,
                    j.CompanyName,
                    j.Location,
                    j.SalaryRange,
                    j.Currency,
                    j.WorkMode,
                    j.ApplyUrl,
                    j.FinalUrl,
                    j.Country,
                    j.Benefits,
                    j.CreatedAt,
                    RequiredSkills = j.RequiredSkills.Select(s => new 
                    {
                        s.Id,
                        s.SkillName,
                        s.IsRequired,
                        s.ProficiencyLevel
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (job == null)
                return NotFound(new { error = "Job not found" });

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{id}/redirect")]
    [AllowAnonymous]
    public async Task<IActionResult> RedirectToApplyUrl(int id)
    {
        try
        {
            var job = await _dbContext.Jobs
                .AsNoTracking()
                .Where(j => j.JobId == id)
                .Select(j => new { j.ApplyUrl, j.FinalUrl })
                .FirstOrDefaultAsync();

            if (job == null)
                return NotFound(new { error = "Job not found" });

            var targetUrl = string.IsNullOrWhiteSpace(job.FinalUrl) ? job.ApplyUrl : job.FinalUrl;

            if (string.IsNullOrWhiteSpace(targetUrl))
                return BadRequest(new { error = "No apply URL is configured for this job." });

            if (!Uri.IsWellFormedUriString(targetUrl, UriKind.Absolute))
                return BadRequest(new { error = "The job apply URL is not valid." });

            return Redirect(targetUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redirecting to job apply URL");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { error = "Request data is required." });
        }

        try
        {
            var job = new Job
            {
                Title = request.Title,
                Description = request.Description,
                CompanyName = request.CompanyName ?? "Company",
                Location = request.Location ?? "Remote",
                SalaryRange = request.SalaryRange ?? "Negotiable",
                Currency = request.Country != null ? GetCurrencyFromCountry(request.Country) : "USD",
                ApplyUrl = request.ApplyUrl ?? string.Empty,
                FinalUrl = request.FinalUrl ?? request.ApplyUrl ?? string.Empty,
                WorkMode = string.IsNullOrWhiteSpace(request.WorkMode) ? "Hybrid" : request.WorkMode,
                Country = request.Country ?? string.Empty,
                Benefits = request.Benefits ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                FetchedAtUtc = DateTime.UtcNow,
                RequiredSkills = new List<JobSkill>()
            };

            _dbContext.Jobs.Add(job);
            await _dbContext.SaveChangesAsync();

            if (request.RequiredSkills != null && request.RequiredSkills.Any())
            {
                foreach (var skill in request.RequiredSkills)
                {
                    _dbContext.JobSkills.Add(new JobSkill
                    {
                        JobId = job.JobId,
                        SkillName = skill,
                        IsRequired = true,
                        ProficiencyLevel = 3
                    });
                }

                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("Job created: {JobId}", job.JobId);
            return Ok(new { success = true, jobId = job.JobId, job });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating job");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static string GetCurrencyFromCountry(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return "USD";

        return country.Trim().ToUpperInvariant() switch
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

    [HttpGet("{jobId}/candidates")]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> GetJobCandidates(int jobId)
    {
        try
        {
            // ✅ FIX: Return clean data
            var matches = await _dbContext.JobMatches
                .Where(m => m.JobId == jobId)
                .Select(m => new
                {
                    m.MatchId,
                    m.JobId,
                    m.CandidateId,
                    m.MatchScore,
                    m.MatchedSkills,
                    m.MissingSkills,
                    m.CreatedAt,
                    Candidate = new
                    {
                        m.Candidate.CandidateId,
                        m.Candidate.Name,
                        m.Candidate.Email,
                        m.Candidate.Phone,
                        m.Candidate.Location,
                        m.Candidate.ResumeScore,
                        Skills = m.Candidate.Skills.Select(s => new
                        {
                            s.Id,
                            s.SkillName,
                            s.Proficiency
                        }).ToList()
                    }
                })
                .OrderByDescending(m => m.MatchScore)
                .ToListAsync();

            return Ok(matches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job candidates");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("skills/count")]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> GetSkillsCount()
    {
        try
        {
            var skillsCount = await _dbContext.CandidateSkills
                .GroupBy(s => s.SkillName)
                .Select(g => new { skill = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync();

            return Ok(skillsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting skills count");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("analysis/by-technology")]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> GetTechnologyAnalysis()
    {
        try
        {
            var allSkills = await _dbContext.CandidateSkills
                .AsNoTracking()
                .GroupBy(s => s.SkillName)
                .Select(g => new 
                { 
                    technology = g.Key, 
                    totalCandidates = g.Count(),
                    averageScore = g.Select(s => s.Candidate.ResumeScore).Average()
                })
                .OrderByDescending(x => x.totalCandidates)
                .ToListAsync();

            return Ok(new 
            { 
                totalTechnologies = allSkills.Count, 
                technologies = allSkills 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing technologies");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> DeleteJob(int id)
    {
        try
        {
            var job = await _dbContext.Jobs.FirstOrDefaultAsync(j => j.JobId == id);
            if (job == null)
                return NotFound(new { error = "Job not found" });

            _dbContext.Jobs.Remove(job);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Job deleted: {JobId}", id);
            return Ok(new { message = "Job deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting job");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> UpdateJob(int id, [FromBody] UpdateJobRequest request)
    {
        try
        {
            var job = await _dbContext.Jobs
                .Include(j => j.RequiredSkills)
                .FirstOrDefaultAsync(j => j.JobId == id);

            if (job == null)
                return NotFound(new { error = "Job not found" });

            job.Title = request.Title ?? job.Title;
            job.Description = request.Description ?? job.Description;
            job.CompanyName = request.CompanyName ?? job.CompanyName;
            job.Location = request.Location ?? job.Location;
            job.SalaryRange = request.SalaryRange ?? job.SalaryRange;

            // Update skills if provided
            if (request.RequiredSkills != null && request.RequiredSkills.Count > 0)
            {
                _dbContext.JobSkills.RemoveRange(job.RequiredSkills);
                
                foreach (var skill in request.RequiredSkills)
                {
                    _dbContext.JobSkills.Add(new JobSkill
                    {
                        JobId = job.JobId,
                        SkillName = skill,
                        IsRequired = true,
                        ProficiencyLevel = 3
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Job updated: {JobId}", id);
            return Ok(new { success = true, job });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating job");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class CreateJobRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? Location { get; set; }
    public string? SalaryRange { get; set; }
    public string? Country { get; set; }
    public string? WorkMode { get; set; }
    public string? ApplyUrl { get; set; }
    public string? FinalUrl { get; set; }
    public string? Benefits { get; set; }
    public List<string> RequiredSkills { get; set; } = new();
}

public class UpdateJobRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? CompanyName { get; set; }
    public string? Location { get; set; }
    public string? SalaryRange { get; set; }
    public string? Country { get; set; }
    public string? WorkMode { get; set; }
    public string? ApplyUrl { get; set; }
    public string? FinalUrl { get; set; }
    public string? Benefits { get; set; }
    public List<string>? RequiredSkills { get; set; }
}