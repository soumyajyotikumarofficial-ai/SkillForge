using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    public async Task<IActionResult> AnalyzeResume(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Upload-resume endpoint hit with an empty or missing file.");
            return BadRequest(new { error = "Please upload a valid resume file." });
        }

        _logger.LogInformation("Processing resume upload request: {FileName} ({Length} bytes)", file.FileName, file.Length);

        var aiResultObject = await _aiService.ProcessAndAnalyzeResumeAsync(file);
        if (aiResultObject == null)
        {
            _logger.LogError("AI Service processing completely failed or returned a null reference due to upstream API unavailability.");
            return StatusCode(503, new { error = "The AI parsing service is currently experiencing high demand. Please try again in a few moments." });
        }

        try
        {
            var rawJson = JsonSerializer.Serialize(aiResultObject);
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                _logger.LogWarning("AI Service structural internal error highlighted: {Error}", errorProp.GetString());
                return BadRequest(new { error = errorProp.GetString() });
            }

            if (!root.TryGetProperty("candidate", out var candidateObj))
            {
                _logger.LogError("Candidate object missing from analysis result payload hierarchy structures.");
                return StatusCode(500, new { error = "Malformed analysis payload architecture returned by AI processing frameworks." });
            }

            string name = candidateObj.GetProperty("name").GetString() ?? "";
            string email = candidateObj.GetProperty("email").GetString() ?? "";
            string phone = candidateObj.GetProperty("phone").GetString() ?? "";
            string location = candidateObj.GetProperty("location").GetString() ?? "";
            string qualification = candidateObj.GetProperty("highestQualification").GetString() ?? "";
            string experience = candidateObj.GetProperty("yearsOfExperience").GetString() ?? "";
            
            int score = root.TryGetProperty("score", out var scoreProp) ? scoreProp.GetInt32() : 0;
            string summary = root.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() ?? "" : "";

            // Database lookup
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
                    UserId = null,
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

            // Populate skills
            if (root.TryGetProperty("skills", out var skillsArray))
            {
                foreach (var skillElement in skillsArray.EnumerateArray())
                {
                    string extractedSkillName = skillElement.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(extractedSkillName))
                    {
                        var cleanSkill = new CandidateSkill
                        {
                            CandidateId = candidate.CandidateId,
                            SkillName = extractedSkillName.Trim(),
                            Proficiency = 3
                        };
                        _dbContext.CandidateSkills.Add(cleanSkill);
                    }
                }
                await _dbContext.SaveChangesAsync();
            }

            // Match Engine Loop
            var currentOpenJobs = await _dbContext.Jobs
                .Include(j => j.RequiredSkills)
                .ToListAsync();

            var candidateSkillNames = await _dbContext.CandidateSkills
                .Where(s => s.CandidateId == candidate.CandidateId)
                .Select(s => s.SkillName.ToLower().Trim())
                .ToListAsync();

            foreach (var job in currentOpenJobs)
            {
                var matches = job.RequiredSkills
                    .Where(rs => candidateSkillNames.Any(cs => cs.Contains(rs.SkillName.ToLower().Trim()) || rs.SkillName.ToLower().Trim().Contains(cs)))
                    .ToList();

                var missing = job.RequiredSkills.Except(matches).ToList();

                int calculatedMatchScore = job.RequiredSkills.Count > 0 
                    ? (int)Math.Round((double)matches.Count / job.RequiredSkills.Count * 100) 
                    : 0;

                string matchedSkillsCsv = string.Join(", ", matches.Select(m => m.SkillName));
                string missingSkillsCsv = string.Join(", ", missing.Select(m => m.SkillName));

                var existingMatchRecord = await _dbContext.JobMatches
                    .FirstOrDefaultAsync(jm => jm.JobId == job.JobId && jm.CandidateId == candidate.CandidateId);

                if (existingMatchRecord != null)
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
            _logger.LogInformation("✅ Job evaluation calculation sequence finished for Candidate Tracking Key: {Id}", candidate.CandidateId);

            // Build explicit payload mapped to the structure of candidate-dashboard.html JavaScript
            var safePayload = await _dbContext.Candidates
                .Where(c => c.CandidateId == candidate.CandidateId)
                .Select(c => new
                {
                    score = c.ResumeScore,
                    summary = c.Summary,
                    candidate = new
                    {
                        name = c.Name,
                        email = c.Email,
                        phone = c.Phone,
                        location = c.Location,
                        highestQualification = c.HighestQualification,
                        yearsOfExperience = c.YearsOfExperience
                    },
                    // FIX: Maps database complex skills entities directly to a clean array of string objects
                    skills = c.Skills.Select(s => s.SkillName).ToList(),
                    jobMatches = c.JobMatches.Select(jm => new
                    {
                        matchId = jm.MatchId,
                        matchScore = jm.MatchScore,
                        matchedSkills = jm.MatchedSkills,
                        missingSkills = jm.MissingSkills,
                        jobTitle = jm.Job.Title,
                        companyName = jm.Job.CompanyName,
                        jobLocation = jm.Job.Location,
                        salaryRange = jm.Job.SalaryRange
                    }).OrderByDescending(jm => jm.matchScore).ToList()
                })
                .FirstOrDefaultAsync();

            return Ok(safePayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal failure parsing and mapping system entities on resume ingestion pipelines.");
            return StatusCode(500, new { error = "Internal controller parsing error occurred.", details = ex.Message });
        }
    }
}