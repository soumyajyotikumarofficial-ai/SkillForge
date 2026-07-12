using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    public CandidateController(
        SkillForgeDbContext dbContext, 
        AIService aiService, 
        ILogger<CandidateController> logger)
    {
        _dbContext = dbContext;
        _aiService = aiService;
        _logger = logger;
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

            // Resolve the target role query and location scope once so only matching DB jobs are surfaced.
            bool hasExplicitRole = preferences.HasRoleAspiration;
            string primaryQuerySkill = hasExplicitRole
                ? preferences.RoleAspiration!.Trim()
                : (localSkillNames.FirstOrDefault() ?? ".NET Developer");

            var normalizedCandidateSkills = localSkillNames
                .SelectMany(ExpandSkillKeywords)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string preferredCountry = !string.IsNullOrWhiteSpace(preferences.Country) ? preferences.Country!.Trim() : "IN";
            var preferredLocations = preferences.GetCleanLocations();
            if (preferredLocations.Count == 0)
            {
                preferredLocations.Add("India");
            }

            var roleKeywords = primaryQuerySkill
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(k => k.Length > 2)
                .ToList();

            var currentOpenJobs = (await _dbContext.Jobs.ToListAsync())
                .Where(job =>
                    MatchesJobByRoleOrSkills(job, hasExplicitRole, roleKeywords, normalizedCandidateSkills)
                    && preferredLocations.Any(loc => job.Location.Contains(loc, StringComparison.OrdinalIgnoreCase) || loc.Contains(job.Location, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(job.Country)
                        || job.Country.Contains(preferredCountry, StringComparison.OrdinalIgnoreCase)
                        || preferredCountry.Contains(job.Country, StringComparison.OrdinalIgnoreCase))
                )
                .ToList();

            var existingMatchDictionary = await _dbContext.JobMatches
                .Where(jm => jm.CandidateId == candidate.CandidateId)
                .ToDictionaryAsync(jm => jm.JobId);

            foreach (var job in currentOpenJobs)
            {
                var requiredSkills = ParseJobSkillsText(job.Description, job.Title);
                var matched = normalizedCandidateSkills
                    .Intersect(requiredSkills.Select(s => s.ToLower()), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var missing = requiredSkills
                    .Where(s => !normalizedCandidateSkills.Contains(s.ToLower(), StringComparer.OrdinalIgnoreCase))
                    .ToList();

                int calculatedMatchScore = requiredSkills.Count > 0 
                    ? (int)Math.Round((double)matched.Count / requiredSkills.Count * 100) 
                    : 0;

                if (calculatedMatchScore == 0 && (job.Title.ToLower().Contains("developer") || job.Title.ToLower().Contains("engineer") || job.Title.ToLower().Contains("analyst")))
                {
                    calculatedMatchScore = 45;
                }

                string matchedSkillsCsv = matched.Any() ? string.Join(", ", matched).ToUpperInvariant() : "NONE";
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
                    { "salaryRange", FormatSalary(job) },
                    { "currency", job.Currency },
                    { "country", job.Country },
                    { "applyUrl", job.ApplyUrl ?? "https://linkedin.com" },
                    { "benefits", job.Benefits ?? "" },
                    { "explanation", explanation },
                    { "MatchScore", currentScore },
                    { "MatchedSkills", matchRecord?.MatchedSkills ?? "NONE" },
                    { "MissingSkills", matchRecord?.MissingSkills ?? "NONE" },
                    { "JobTitle", job.Title },
                    { "CompanyName", job.CompanyName },
                    { "JobLocation", job.Location },
                    { "SalaryRange", FormatSalary(job) },
                    { "Currency", job.Currency },
                    { "Country", job.Country },
                    { "ApplyUrl", job.ApplyUrl ?? "https://linkedin.com" },
                    { "Benefits", job.Benefits ?? "" },
                    { "Explanation", explanation }
                };
            }).OrderByDescending(jm => (int)jm["matchScore"]).ToList();

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

    private static string FormatSalary(Job job)
    {
        if (string.IsNullOrWhiteSpace(job.SalaryRange) || job.SalaryRange.Equals("NA", StringComparison.OrdinalIgnoreCase))
        {
            return "NA";
        }

        if (ContainsExplicitCurrencyMarker(job.SalaryRange))
        {
            return job.SalaryRange;
        }

        if (!string.IsNullOrWhiteSpace(job.Currency) && !job.SalaryRange.StartsWith(job.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return $"{job.Currency} {job.SalaryRange}";
        }

        return job.SalaryRange;
    }

    private List<string> ParseJobSkillsText(string description, string title)
    {
        var discovered = new List<string>();
        string combined = $"{title} {description}".ToLower();

        var techDictionary = new List<(string Needle, string Canonical)> {
            ("azure devops", "AZURE"),
            (".net8", ".NET"),
            (".net 8", ".NET"),
            (".net core", ".NET"),
            ("asp.net", ".NET"),
            ("azure", "AZURE"),
            ("devops", "DEVOPS"),
            ("c#", "C#"),
            ("python", "PYTHON"),
            ("java", "JAVA"),
            ("javascript", "JAVASCRIPT"),
            ("typescript", "TYPESCRIPT"),
            ("sql", "SQL"),
            ("sqlite", "SQLITE"),
            ("postgresql", "POSTGRESQL"),
            ("docker", "DOCKER"),
            ("aws", "AWS"),
            ("git", "GIT"),
            ("html", "HTML"),
            ("css", "CSS"),
            ("angular", "ANGULAR"),
            ("react", "REACT")
        };

        foreach (var tech in techDictionary)
        {
            if (combined.Contains(tech.Needle))
            {
                discovered.Add(tech.Canonical);
            }
        }
        
        if (!discovered.Any()) discovered.AddRange(new[] { ".NET", "C#", "SQL" });
        return discovered.Distinct().ToList();
    }

    private static bool MatchesJobByRoleOrSkills(Job job, bool hasExplicitRole, List<string> roleKeywords, List<string> normalizedCandidateSkills)
    {
        var jobText = $"{job.Title} {job.Description}";

        if (hasExplicitRole)
        {
            return roleKeywords.Count == 0 || roleKeywords.Any(k => job.Title.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedCandidateSkills.Count > 0)
        {
            return normalizedCandidateSkills.Any(skill => jobText.Contains(skill, StringComparison.OrdinalIgnoreCase));
        }

        return roleKeywords.Count == 0 || roleKeywords.Any(k => jobText.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExpandSkillKeywords(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            yield break;
        }

        var normalized = skill.Trim().ToLowerInvariant();

        if (normalized.Contains("azure devops"))
        {
            yield return "azure";
            yield return "devops";
            yield break;
        }

        if (normalized.Contains(".net8") || normalized.Contains(".net 8") || normalized.Contains(".net core") || normalized.Contains("asp.net"))
        {
            yield return ".net";
        }

        yield return normalized;
    }

    private static bool ContainsExplicitCurrencyMarker(string salaryRange)
    {
        string[] markers = { "₹", "$", "€", "£", "rs", "inr", "usd", "eur", "gbp", "cad", "aud", "sgd", "aed", "jpy" };
        return markers.Any(marker => salaryRange.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}