using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SkillForge.Models;
using SkillForge.API.Services;
using SkillForge.Data;
using SkillForge.DTOs;

namespace SkillForge.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidateController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly AIService _aiService;
    private readonly ILogger<CandidateController> _logger;
    private readonly IConfiguration _configuration;
    private readonly ApifyJobService _apifyJobService;

    public CandidateController(
        SkillForgeDbContext dbContext, 
        AIService aiService, 
        ILogger<CandidateController> logger,
        IConfiguration configuration,
        ApifyJobService apifyJobService)
    {
        _dbContext = dbContext;
        _aiService = aiService;
        _logger = logger;
        _configuration = configuration;
        _apifyJobService = apifyJobService;
    }

    [HttpPost("upload-resume")]
    public async Task<IActionResult> AnalyzeResume(
        IFormFile file,
        [FromForm] string? country,
        [FromForm] string? location1,
        [FromForm] string? location2,
        [FromForm] string? location3,
        [FromForm] string? roleAspiration)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Upload-resume endpoint hit with an empty or missing file.");
            return BadRequest(new { error = "Please upload a valid resume file." });
        }

        _logger.LogInformation("Processing resume upload request: {FileName} ({Length} bytes)", file.FileName, file.Length);

        // Build optional job-hunt preferences from the submitted form fields. Location preferences
        // are capped at 3, and role aspiration remains entirely optional (falls back to AI deduction).
        var preferences = new JobHuntPreferences
        {
            Country = country,
            RoleAspiration = roleAspiration,
            LocationPreferences = new List<string?> { location1, location2, location3 }
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l!.Trim())
                .ToList()
        };

        // 1. Invoke the service and receive the strongly-typed analysis object directly
        var analysisResult = await _aiService.ProcessAndAnalyzeResumeAsync(file, preferences);
        
        if (analysisResult == null || analysisResult.Candidate == null)
        {
            _logger.LogError("AI Service processing completely failed or returned a null candidate hierarchy.");
            return StatusCode(503, new { error = "The AI parsing service is currently experiencing high demand or failed to parse the profile layout. Please try again." });
        }

        try
        {
            // 2. Map structural values directly without round-trip JSON parsing reflection tricks
            string name = analysisResult.Candidate.Name ?? "";
            string email = analysisResult.Candidate.Email ?? "";
            string phone = analysisResult.Candidate.Phone ?? "";
            string location = analysisResult.Candidate.Location ?? "";
            string qualification = analysisResult.Candidate.HighestQualification ?? "";
            string experience = analysisResult.Candidate.YearsOfExperience ?? "";
            int score = analysisResult.Score;
            string summary = analysisResult.Summary ?? "";

            var candidate = await _dbContext.Candidates
                .Include(c => c.Skills)
                .FirstOrDefaultAsync(c => c.Name == name && c.Phone == phone);

            if (candidate != null)
            {
                _logger.LogInformation("Updating existing candidate record ID: {Id}", candidate.CandidateId);
                candidate.Email = email;
                candidate.Location = location;
                candidate.HighestQualification = qualification;
                candidate.YearsOfExperience = experience;
                candidate.ResumeScore = score;
                candidate.Summary = summary;
                candidate.UpdatedAt = DateTime.UtcNow;

                _dbContext.CandidateSkills.RemoveRange(candidate.Skills);
                candidate.Skills.Clear();
            }
            else
            {
                _logger.LogInformation("Creating new candidate instance entry in database: {Name}", name);
                candidate = new Candidate
                {
                    UserId = 1,
                    Name = name,
                    Email = email,
                    Phone = phone,
                    Location = location,
                    HighestQualification = qualification,
                    YearsOfExperience = experience,
                    ResumeScore = score,
                    Summary = summary,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.Candidates.Add(candidate);
            }

            await _dbContext.SaveChangesAsync();

            // 3. Process skills directly out of the strongly-typed array collection
            var localSkillNames = new List<string>();
            if (analysisResult.Skills != null && analysisResult.Skills.Any())
            {
                foreach (var extractedSkillName in analysisResult.Skills)
                {
                    if (!string.IsNullOrWhiteSpace(extractedSkillName))
                    {
                        string cleanName = extractedSkillName.Trim();
                        localSkillNames.Add(cleanName.ToLower());

                        var cleanSkill = new CandidateSkill
                        {
                            CandidateId = candidate.CandidateId,
                            SkillName = cleanName,
                            Proficiency = 3
                        };
                        _dbContext.CandidateSkills.Add(cleanSkill);
                    }
                }
                await _dbContext.SaveChangesAsync();
            }

            // Resolve the target role query and location scope once, shared by both the local DB
            // filter and the live Apify fetch below, so only relevant jobs are ever surfaced.
            string primaryQuerySkill = preferences.HasRoleAspiration
                ? preferences.RoleAspiration!.Trim()
                : (localSkillNames.FirstOrDefault() ?? ".NET Developer");

            string liveMatchCountry = !string.IsNullOrWhiteSpace(preferences.Country) ? preferences.Country!.Trim() : "US";
            var liveMatchLocations = preferences.GetCleanLocations();
            if (liveMatchLocations.Count == 0)
            {
                liveMatchLocations.Add("United States");
            }

            var roleKeywords = primaryQuerySkill
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(k => k.Length > 2)
                .ToList();

            // Only surface jobs whose title relates to the target role AND whose location overlaps
            // with one of the candidate's preferred locations — never the entire Jobs table.
            var currentOpenJobs = (await _dbContext.Jobs.ToListAsync())
                .Where(job =>
                    (roleKeywords.Count == 0 || roleKeywords.Any(k => job.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    && liveMatchLocations.Any(loc => job.Location.Contains(loc, StringComparison.OrdinalIgnoreCase) || loc.Contains(job.Location, StringComparison.OrdinalIgnoreCase))
                )
                .ToList();

            var existingMatchDictionary = await _dbContext.JobMatches
                .Where(jm => jm.CandidateId == candidate.CandidateId)
                .ToDictionaryAsync(jm => jm.JobId);

            foreach (var job in currentOpenJobs)
            {
                var requiredSkills = ParseJobSkillsText(job.Description, job.Title);
                var matched = localSkillNames
                    .Intersect(requiredSkills.Select(s => s.ToLower()), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var missing = requiredSkills
                    .Where(s => !localSkillNames.Contains(s.ToLower()))
                    .ToList();

                int calculatedMatchScore = requiredSkills.Count > 0 
                    ? (int)Math.Round((double)matched.Count / requiredSkills.Count * 100) 
                    : 0;

                if (calculatedMatchScore == 0 && (job.Title.ToLower().Contains("developer") || job.Title.ToLower().Contains("engineer") || job.Title.ToLower().Contains("analyst")))
                {
                    calculatedMatchScore = 45;
                }

                string matchedSkillsCsv = matched.Any() ? string.Join(", ", matched).ToUpper() : "NONE";
                string missingSkillsCsv = missing.Any() ? string.Join(", ", missing).ToUpper() : "NONE";

                if (existingMatchDictionary.TryGetValue(job.JobId, out var existingMatchRecord))
                {
                    existingMatchRecord.MatchScore = calculatedMatchScore;
                    existingMatchRecord.MatchedSkills = matchedSkillsCsv;
                    existingMatchRecord.MissingSkills = missingSkillsCsv;
                    existingMatchRecord.CreatedAt = DateTime.UtcNow;
                }
                else
                {
                    var newJobMatchEntry = new JobMatch
                    {
                        JobId = job.JobId,
                        CandidateId = candidate.CandidateId,
                        MatchScore = calculatedMatchScore,
                        MatchedSkills = matchedSkillsCsv,
                        MissingSkills = missingSkillsCsv,
                        CreatedAt = DateTime.UtcNow
                    };
                    _dbContext.JobMatches.Add(newJobMatchEntry);
                }
            }

            await _dbContext.SaveChangesAsync();

            var matchesUpdated = await _dbContext.JobMatches
                .Where(jm => jm.CandidateId == candidate.CandidateId)
                .ToListAsync();

            var allMatchedJobs = currentOpenJobs.Select(job => {
                var matchRecord = matchesUpdated.FirstOrDefault(m => m.JobId == job.JobId);
                int currentScore = matchRecord?.MatchScore ?? 35;
         
                string explanation = "Low match. Noticeable alignment variations relative to the target tool framework configuration details.";
                if (currentScore >= 80) explanation = "Excellent match! Strong alignment with your core technical expertise, satisfying critical requirements.";
                else if (currentScore >= 40) {
                    var parts = (matchRecord?.MissingSkills ?? "NONE").Split(", ");
                    explanation = $"Good match. Solid foundational architecture profile alignment, but you need to bridge the gap regarding: {parts.FirstOrDefault()}.";
                }

                return new Dictionary<string, object> {
                    { "matchScore", currentScore },
                    { "matchedSkills", matchRecord?.MatchedSkills ?? "NONE" },
                    { "missingSkills", matchRecord?.MissingSkills ?? "NONE" },
                    { "jobTitle", job.Title },
                    { "companyName", job.CompanyName },
                    { "jobLocation", job.Location },
                    { "salaryRange", job.SalaryRange },
                    { "applyUrl", job.ApplyUrl ?? "https://linkedin.com" },
                    { "explanation", explanation },
                    { "MatchScore", currentScore },
                    { "MatchedSkills", matchRecord?.MatchedSkills ?? "NONE" },
                    { "MissingSkills", matchRecord?.MissingSkills ?? "NONE" },
                    { "JobTitle", job.Title },
                    { "CompanyName", job.CompanyName },
                    { "JobLocation", job.Location },
                    { "SalaryRange", job.SalaryRange },
                    { "ApplyUrl", job.ApplyUrl ?? "https://linkedin.com" },
                    { "Explanation", explanation }
                };
            }).OrderByDescending(jm => (int)jm["matchScore"]).ToList();

            var liveJobMatches = new List<Dictionary<string, object>>();
            try
            {
                int count = 0;
                foreach (var searchLocation in liveMatchLocations)
                {
                    if (count >= 5) break;

                    var apifyResults = await _apifyJobService.FetchJobsAsync(primaryQuerySkill, searchLocation, liveMatchCountry);

                    foreach (var job in apifyResults)
                    {
                        if (count >= 5) break;

                        int dynamicLiveScore = 95 - (count * 5);
                        string matchedCsv = primaryQuerySkill.ToUpper();
                        string explanation = "Live position correlated via Apify Job Scraper.";

                        liveJobMatches.Add(new Dictionary<string, object> {
                            { "matchScore", dynamicLiveScore },
                            { "matchedSkills", matchedCsv },
                            { "missingSkills", "NONE" },
                            { "jobTitle", job.Title },
                            { "companyName", job.CompanyName },
                            { "jobLocation", job.Location },
                            { "salaryRange", job.SalaryRange },
                            { "applyUrl", job.ApplyUrl },
                            { "explanation", explanation },
                            { "MatchScore", dynamicLiveScore },
                            { "MatchedSkills", matchedCsv },
                            { "MissingSkills", "NONE" },
                            { "JobTitle", job.Title },
                            { "CompanyName", job.CompanyName },
                            { "JobLocation", job.Location },
                            { "SalaryRange", job.SalaryRange },
                            { "ApplyUrl", job.ApplyUrl },
                            { "Explanation", explanation }
                        });
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect or parse response from Apify Job Scraper within controller container context.");
            }

            allMatchedJobs.InsertRange(0, liveJobMatches);

            var frontendResponsePayload = new Dictionary<string, object>
            {
                { "score", candidate.ResumeScore },
                { "summary", candidate.Summary },
                { "candidate", new Dictionary<string, string> {
                    { "name", candidate.Name },
                    { "email", candidate.Email },
                    { "phone", candidate.Phone },
                    { "location", candidate.Location },
                    { "highestQualification", candidate.HighestQualification },
                    { "yearsOfExperience", candidate.YearsOfExperience }
                }},
                { "skills", localSkillNames.Select(s => s.ToUpper()).ToList() },
                { "jobMatches", allMatchedJobs }
            };
            return Ok(frontendResponsePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal failure parsing and mapping system entities on resume ingestion pipelines.");
            return StatusCode(500, new { error = "Internal controller parsing error occurred.", details = ex.Message });
        }
    }

    private List<string> ParseJobSkillsText(string description, string title)
    {
        var discovered = new List<string>();
        string combined = $"{title} {description}".ToLower();

        var techDictionary = new List<string> { 
            ".net", "c#", "python", "java", "javascript", "typescript", "sql", "sqlite", 
            "postgresql", "docker", "aws", "azure", "git", "html", "css", "angular", "react" 
        };

        foreach (var tech in techDictionary)
        {
            if (combined.Contains(tech))
            {
                discovered.Add(tech.ToUpper());
            }
        }
        
        if (!discovered.Any()) discovered.AddRange(new[] { ".NET", "C#", "SQL" });
        return discovered.Distinct().ToList();
    }
}