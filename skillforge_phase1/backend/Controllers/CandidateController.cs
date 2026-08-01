using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
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
                    { "applyUrl", string.IsNullOrWhiteSpace(job.ApplyUrl) ? "" : job.ApplyUrl },
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
                    { "ApplyUrl", string.IsNullOrWhiteSpace(job.ApplyUrl) ? "" : job.ApplyUrl },
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

    /// <summary>
    /// Resolves (or lazily creates) the single Candidate profile tied to the authenticated user's JWT identity.
    /// </summary>
    private async Task<Candidate> GetOrCreateCandidateForCurrentUserAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Missing or invalid user identity.");
        }

        var candidate = await _dbContext.Candidates.FirstOrDefaultAsync(c => c.UserId == userId);
        if (candidate == null)
        {
            candidate = new Candidate { UserId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            _dbContext.Candidates.Add(candidate);
            await _dbContext.SaveChangesAsync();
        }
        return candidate;
    }

    // ===================== FEATURE 2: RESUME MANAGEMENT (max 2, JSON-only, duplicate-name guard) =====================

    private static readonly string[] AllowedResumeExtensions = { ".pdf", ".docx", ".txt" };

    [Authorize]
    [HttpPost("resumes")]
    public async Task<IActionResult> UploadManagedResume(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "Please upload a valid resume file." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedResumeExtensions.Contains(extension))
        {
            return BadRequest(new { error = "Unsupported file type. Please upload a PDF, DOCX, or TXT resume." });
        }

        Candidate candidate;
        try
        {
            candidate = await GetOrCreateCandidateForCurrentUserAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Please log in to upload a resume." });
        }

        var existingResumes = await _dbContext.CandidateResumes
            .Where(r => r.CandidateId == candidate.CandidateId)
            .ToListAsync();

        // Rule: candidates may keep at most 2 saved resume profiles.
        if (existingResumes.Count >= 2)
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                error = "You already have 2 saved resumes, which is the maximum allowed. Please delete an existing resume before uploading a new one.",
                requiresDeletion = true
            });
        }

        // Rule: reject duplicate file names against this candidate's existing resumes.
        var trimmedFileName = file.FileName.Trim();
        if (existingResumes.Any(r => string.Equals(r.FileName, trimmedFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { error = "A resume with this file name already exists. Please rename your file or delete the previous version." });
        }

        var analysis = await _aiService.ParseResumeToJsonAsync(file);
        if (analysis == null)
        {
            return StatusCode(503, new { error = "The AI parsing service is currently experiencing high demand or failed to parse the resume. Please try again." });
        }

        // Storage constraint: only the AI-parsed JSON is persisted. The raw file bytes are never written to disk.
        var resume = new CandidateResume
        {
            CandidateId = candidate.CandidateId,
            FileName = trimmedFileName,
            FileExtension = extension,
            ParsedResumeJson = JsonSerializer.Serialize(analysis),
            IsActive = existingResumes.Count == 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            _dbContext.CandidateResumes.Add(resume);
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { error = "A resume with this file name already exists. Please rename your file or delete the previous version." });
        }

        if (resume.IsActive)
        {
            candidate.ActiveResumeId = resume.CandidateResumeId;
            ApplyResumeDetailsToCandidate(candidate, analysis.Candidate);
            await _dbContext.SaveChangesAsync();
        }

        return Ok(new ResumeUploadResponseDto
        {
            ResumeId = resume.CandidateResumeId,
            FileName = resume.FileName,
            Score = analysis.Score,
            Summary = analysis.Summary,
            Skills = analysis.Skills,
            ResumeCount = existingResumes.Count + 1,
            IsActive = resume.IsActive
        });
    }

    [Authorize]
    [HttpGet("resumes")]
    public async Task<IActionResult> GetMyResumes()
    {
        Candidate candidate;
        try
        {
            candidate = await GetOrCreateCandidateForCurrentUserAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Please log in to view resumes." });
        }

        var resumes = await _dbContext.CandidateResumes
            .Where(r => r.CandidateId == candidate.CandidateId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var summaries = resumes.Select(r =>
        {
            var parsed = TryDeserializeResumeAnalysis(r.ParsedResumeJson);
            return new ResumeSummaryDto
            {
                ResumeId = r.CandidateResumeId,
                FileName = r.FileName,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
                DeducedRole = parsed?.Candidate?.Role,
                Score = parsed?.Score ?? 0
            };
        }).ToList();

        return Ok(summaries);
    }

    // Full parsed detail for one stored resume - lets the dashboard redisplay a candidate's data
    // (score/summary/skills/contact info) on every login without re-uploading/re-parsing the file.
    [Authorize]
    [HttpGet("resumes/{resumeId:int}")]
    public async Task<IActionResult> GetResumeDetail(int resumeId)
    {
        Candidate candidate;
        try
        {
            candidate = await GetOrCreateCandidateForCurrentUserAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Please log in to view resume details." });
        }

        var resume = await _dbContext.CandidateResumes
            .FirstOrDefaultAsync(r => r.CandidateResumeId == resumeId && r.CandidateId == candidate.CandidateId);

        if (resume == null)
        {
            return NotFound(new { error = "Resume not found for this candidate." });
        }

        var parsed = TryDeserializeResumeAnalysis(resume.ParsedResumeJson);

        return Ok(new ResumeDetailDto
        {
            ResumeId = resume.CandidateResumeId,
            FileName = resume.FileName,
            IsActive = resume.IsActive,
            CreatedAt = resume.CreatedAt,
            Score = parsed?.Score ?? 0,
            Summary = parsed?.Summary ?? "",
            Skills = parsed?.Skills ?? new List<string>(),
            Name = parsed?.Candidate?.Name ?? candidate.Name,
            Email = parsed?.Candidate?.Email ?? candidate.Email,
            Phone = parsed?.Candidate?.Phone ?? candidate.Phone,
            Location = parsed?.Candidate?.Location ?? candidate.Location,
            HighestQualification = parsed?.Candidate?.HighestQualification ?? candidate.HighestQualification,
            YearsOfExperience = parsed?.Candidate?.YearsOfExperience ?? candidate.YearsOfExperience
        });
    }

    // Login selection menu (Feature 3): candidate picks which of their 1-2 resumes is "active".
    [Authorize]
    [HttpPost("resumes/{resumeId:int}/activate")]
    public async Task<IActionResult> ActivateResume(int resumeId)
    {
        Candidate candidate;
        try
        {
            candidate = await GetOrCreateCandidateForCurrentUserAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Please log in to select an active resume." });
        }

        var resumes = await _dbContext.CandidateResumes
            .Where(r => r.CandidateId == candidate.CandidateId)
            .ToListAsync();

        var target = resumes.FirstOrDefault(r => r.CandidateResumeId == resumeId);
        if (target == null)
        {
            return NotFound(new { error = "Resume not found for this candidate." });
        }

        foreach (var r in resumes)
        {
            r.IsActive = r.CandidateResumeId == resumeId;
            r.UpdatedAt = DateTime.UtcNow;
        }
        candidate.ActiveResumeId = resumeId;
        candidate.UpdatedAt = DateTime.UtcNow;
        ApplyResumeDetailsToCandidate(candidate, TryDeserializeResumeAnalysis(target.ParsedResumeJson)?.Candidate);

        await _dbContext.SaveChangesAsync();
        return Ok(new { success = true, activeResumeId = resumeId });
    }

    [Authorize]
    [HttpDelete("resumes/{resumeId:int}")]
    public async Task<IActionResult> DeleteResume(int resumeId)
    {
        Candidate candidate;
        try
        {
            candidate = await GetOrCreateCandidateForCurrentUserAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Please log in to delete a resume." });
        }

        var target = await _dbContext.CandidateResumes
            .FirstOrDefaultAsync(r => r.CandidateResumeId == resumeId && r.CandidateId == candidate.CandidateId);

        if (target == null)
        {
            return NotFound(new { error = "Resume not found for this candidate." });
        }

        bool wasActive = target.IsActive;
        _dbContext.CandidateResumes.Remove(target);

        if (wasActive)
        {
            candidate.ActiveResumeId = null;
        }
        await _dbContext.SaveChangesAsync();

        if (wasActive)
        {
            var fallback = await _dbContext.CandidateResumes
                .Where(r => r.CandidateId == candidate.CandidateId)
                .OrderBy(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (fallback != null)
            {
                fallback.IsActive = true;
                candidate.ActiveResumeId = fallback.CandidateResumeId;
                await _dbContext.SaveChangesAsync();
            }
        }

        return Ok(new { success = true, deletedResumeId = resumeId });
    }

    // ===================== FEATURE 3: ACTIVE-RESUME + WORK-MODE FILTERED JOB SEARCH =====================

    [Authorize]
    [HttpGet("job-matches")]
    public async Task<IActionResult> GetJobMatchesForActiveResume(
        [FromQuery] string? workMode,
        [FromQuery] string? country,
        [FromQuery] string? location1,
        [FromQuery] string? location2,
        [FromQuery] string? location3,
        [FromQuery] string? roleAspiration)
    {
        Candidate candidate;
        try
        {
            candidate = await GetOrCreateCandidateForCurrentUserAsync();
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Please log in to view job matches." });
        }

        if (candidate.ActiveResumeId == null)
        {
            return BadRequest(new { error = "Select an active resume first." });
        }

        var activeResume = await _dbContext.CandidateResumes
            .FirstOrDefaultAsync(r => r.CandidateResumeId == candidate.ActiveResumeId);

        if (activeResume == null)
        {
            return BadRequest(new { error = "Active resume could not be located. Please re-select an active resume." });
        }

        var skills = TryDeserializeResumeAnalysis(activeResume.ParsedResumeJson)?.Skills ?? new List<string>();
        var normalizedSkills = skills.SelectMany(ExpandSkillKeywords).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Work Mode preference filter: WFH, Hybrid, or WFO.
        var validWorkModes = new[] { "WFH", "Hybrid", "WFO" };
        string? normalizedWorkMode = null;
        if (!string.IsNullOrWhiteSpace(workMode))
        {
            normalizedWorkMode = validWorkModes.FirstOrDefault(m => string.Equals(m, workMode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (normalizedWorkMode == null)
            {
                return BadRequest(new { error = "Invalid workMode. Expected one of: WFH, Hybrid, WFO." });
            }
        }

        // Optional country/location/role-aspiration preferences, re-collected on every search (not persisted).
        var preferredLocations = new List<string?> { location1, location2, location3 }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!.Trim())
            .ToList();
        string? preferredCountry = string.IsNullOrWhiteSpace(country) ? null : country.Trim();
        bool hasExplicitRole = !string.IsNullOrWhiteSpace(roleAspiration);
        var roleKeywords = hasExplicitRole
            ? roleAspiration!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(k => k.Length > 2).ToList()
            : new List<string>();

        // WorkMode isn't populated on every ingested job (legacy/seeded rows); treat unset the same
        // way Location/Country are treated below, so it doesn't silently zero out the whole list.
        var jobsQuery = _dbContext.Jobs.AsQueryable();
        if (normalizedWorkMode != null)
        {
            jobsQuery = jobsQuery.Where(j => j.WorkMode == normalizedWorkMode || string.IsNullOrWhiteSpace(j.WorkMode));
        }

        var jobs = (await jobsQuery.ToListAsync())
            .Where(job => preferredLocations.Count == 0
                || preferredLocations.Any(loc => job.Location.Contains(loc, StringComparison.OrdinalIgnoreCase) || loc.Contains(job.Location, StringComparison.OrdinalIgnoreCase)))
            .Where(job => preferredCountry == null
                || string.IsNullOrWhiteSpace(job.Country)
                || job.Country.Contains(preferredCountry, StringComparison.OrdinalIgnoreCase)
                || preferredCountry.Contains(job.Country, StringComparison.OrdinalIgnoreCase))
            .Where(job => !hasExplicitRole || roleKeywords.Count == 0 || roleKeywords.Any(k => job.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var ranked = jobs.Select(job =>
        {
            var requiredSkills = ParseJobSkillsText(job.Description, job.Title);
            var matched = normalizedSkills.Intersect(requiredSkills.Select(s => s.ToLower()), StringComparer.OrdinalIgnoreCase).ToList();
            int score = requiredSkills.Count > 0 ? (int)Math.Round((double)matched.Count / requiredSkills.Count * 100) : 0;

            return new
            {
                jobId = job.JobId,
                jobTitle = job.Title,
                companyName = job.CompanyName,
                location = job.Location,
                workMode = job.WorkMode,
                applyUrl = string.IsNullOrWhiteSpace(job.ApplyUrl) ? "" : job.ApplyUrl,
                matchScore = score,
                matchedSkills = matched
            };
        })
        .OrderByDescending(j => j.matchScore)
        .ToList();

        return Ok(new { activeResumeId = activeResume.CandidateResumeId, workModeFilter = normalizedWorkMode ?? "Any", jobMatches = ranked });
    }

    // Syncs contact/profile fields onto the Candidate row from the active resume's AI-parsed details,
    // so recruiter contact-reveal (post-selection) has real data instead of empty strings.
    private static void ApplyResumeDetailsToCandidate(Candidate candidate, CandidateDetails? details)
    {
        if (details == null) return;

        if (!string.IsNullOrWhiteSpace(details.Name)) candidate.Name = details.Name;
        if (!string.IsNullOrWhiteSpace(details.Email)) candidate.Email = details.Email;
        if (!string.IsNullOrWhiteSpace(details.Phone)) candidate.Phone = details.Phone;
        if (!string.IsNullOrWhiteSpace(details.Location)) candidate.Location = details.Location;
        if (!string.IsNullOrWhiteSpace(details.HighestQualification)) candidate.HighestQualification = details.HighestQualification;
        if (!string.IsNullOrWhiteSpace(details.YearsOfExperience)) candidate.YearsOfExperience = details.YearsOfExperience;
        candidate.UpdatedAt = DateTime.UtcNow;
    }

    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Case-insensitive so this reads both the current camelCase-agnostic serialization and any legacy rows.
    private static ResumeAnalysisResult? TryDeserializeResumeAnalysis(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ResumeAnalysisResult>(json, CaseInsensitiveJsonOptions);
        }
        catch (JsonException)
        {
            return null;
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