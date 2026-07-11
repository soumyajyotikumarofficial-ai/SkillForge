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

namespace SkillForge.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidateController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly AIService _aiService;
    private readonly ILogger<CandidateController> _logger;
    private readonly IConfiguration _configuration;
    private static readonly HttpClient _httpClient = new HttpClient();

    public CandidateController(
        SkillForgeDbContext dbContext, 
        AIService aiService, 
        ILogger<CandidateController> logger,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _aiService = aiService;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("upload-resume")]
    public async Task<IActionResult> AnalyzeResume(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Upload-resume endpoint hit with an empty or missing file.");
            return BadRequest(new { error = "Please upload a valid resume file." });
        }

        _logger.LogInformation("Processing resume upload request: {FileName} ({Length} bytes)", file.FileName, file.Length);
        
        // 1. Invoke the service and receive the strongly-typed analysis object directly
        var analysisResult = await _aiService.ProcessAndAnalyzeResumeAsync(file);
        
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

            var currentOpenJobs = await _dbContext.Jobs.ToListAsync();
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
                string apiKey = _configuration["RapidAPI:Key"];
                if (!string.IsNullOrEmpty(apiKey))
                {
                    string primaryQuerySkill = localSkillNames.FirstOrDefault() ?? ".NET Developer";
                    
                    var request = new HttpRequestMessage
                    {
                        Method = HttpMethod.Get,
                        RequestUri = new Uri($"https://jsearch.p.rapidapi.com/search-v2?query={Uri.EscapeDataString(primaryQuerySkill)}&page=1&num_pages=1"),
                        Headers =
                        {
                            { "x-rapidapi-key", apiKey },
                            { "x-rapidapi-host", "jsearch.p.rapidapi.com" },
                        },
                    };

                    using var apiResponse = await _httpClient.SendAsync(request);
                    if (apiResponse.IsSuccessStatusCode)
                    {
                        var jsonString = await apiResponse.Content.ReadAsStringAsync();
                        _logger.LogInformation("=== RAW JSEARCH API RESPONSE (FROM CONTROLLER) ===\n{Response}", jsonString);
                        using var apiDoc = JsonDocument.Parse(jsonString);
                        
                        // Defensive Parsing: Look for both "data" and "results" structural arrays reactively
                        JsonElement resultsArray;
                        bool foundArray = false;

                        if (apiDoc.RootElement.TryGetProperty("data", out resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
                        {
                            foundArray = true;
                        }
                        else if (apiDoc.RootElement.TryGetProperty("results", out resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
                        {
                            foundArray = true;
                        }

                        if (foundArray)
                        {
                            int count = 0;
                            foreach (var job in resultsArray.EnumerateArray())
                            {
                                if (count >= 5) break;
                                string title = (job.TryGetProperty("job_title", out var titleProp) ? titleProp.GetString() : null) ?? (job.TryGetProperty("title", out var t2) ? t2.GetString() : null) ?? "Job Position";
                                string company = (job.TryGetProperty("employer_name", out var compProp) ? compProp.GetString() : null) ?? (job.TryGetProperty("company_name", out var c2) ? c2.GetString() : null) ?? "Unknown Employer";
                                string loc = job.TryGetProperty("job_city", out var cityProp) ? cityProp.GetString() : "Remote / Global";
                                string applyUrl = (job.TryGetProperty("job_apply_link", out var urlProp) ? urlProp.GetString() : null) ?? (job.TryGetProperty("apply_url", out var u2) ? u2.GetString() : null) ?? "#";
                                
                                int dynamicLiveScore = 95 - (count * 5);
                                string matchedCsv = primaryQuerySkill.ToUpper();

                                liveJobMatches.Add(new Dictionary<string, object> {
                                    { "matchScore", dynamicLiveScore },
                                    { "matchedSkills", matchedCsv },
                                    { "missingSkills", "NONE" },
                                    { "jobTitle", title },
                                    { "companyName", company },
                                    { "jobLocation", loc },
                                    { "salaryRange", "Market Rate (Live)" },
                                    { "applyUrl", applyUrl },
                                    { "explanation", "Live position correlated via JSearch API." },
                                    { "MatchScore", dynamicLiveScore },
                                    { "MatchedSkills", matchedCsv },
                                    { "MissingSkills", "NONE" },
                                    { "JobTitle", title },
                                    { "CompanyName", company },
                                    { "JobLocation", loc },
                                    { "SalaryRange", "Market Rate (Live)" },
                                    { "ApplyUrl", applyUrl },
                                    { "Explanation", "Live position correlated via JSearch API." }
                                });
                                count++;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [CONTROLLER JSEARCH] Inline search response body is missing both structural 'data' and 'results' property arrays.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect or parse response from JSearch RapidAPI within controller container context.");
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