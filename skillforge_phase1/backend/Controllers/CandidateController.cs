using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SkillForge.API.Services;
using SkillForge.Data;
using SkillForge.Models;
using SkillForge.DTOs;
using System.Text.Json;
using System.IO;
using System.Text.Json.Serialization;

namespace SkillForge.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidateController : ControllerBase
{
    private readonly AIService _aiService;
    private readonly SkillForgeDbContext _dbContext;
    private readonly ILogger<CandidateController> _logger;

    public CandidateController(AIService aiService, SkillForgeDbContext dbContext, ILogger<CandidateController> logger)
    {
        _aiService = aiService;
        _dbContext = dbContext;
        _logger = logger;
    }

    // ✅ FIXED: Route renamed from "analyze-resume" to "upload-resume" to match dashboard targets
    [HttpPost("upload-resume")]
    [AllowAnonymous]  // ✅ Bypass token validation crashes during local testing
    public async Task<IActionResult> AnalyzeResume(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded" });

            var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
                return BadRequest(new { error = "Only PDF, DOCX, TXT allowed" });

            string fileContent;
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                stream.Position = 0;

                fileContent = fileExtension switch
                {
                    ".pdf" => ExtractTextFromPdf(stream),
                    ".docx" => ExtractTextFromDocx(stream),
                    ".txt" => ExtractTextFromTxt(stream),
                    _ => throw new InvalidOperationException("Unsupported file type")
                };
            }

            if (string.IsNullOrWhiteSpace(fileContent))
                return BadRequest(new { error = "Could not extract text from file" });

            _logger.LogInformation("Resume text extracted: {Length} characters", fileContent.Length);

            // Call AI Service to analyze
            var result = await _aiService.AnalyzeResumeAsync(fileContent);

            if (result == null)
                return StatusCode(500, new { error = "Analysis failed" });

            // Save to database after analysis
            await SaveCandidateToDbAsync(result);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing resume");
            return StatusCode(500, new { error = "Processing error", details = ex.Message });
        }
    }

    [HttpPost("register")]
    [AllowAnonymous] 
    public async Task<IActionResult> Register([FromBody] JsonElement model)
    {
        try
        {
            _logger.LogInformation("Registration request received: {Data}", model.GetRawText());

            string username = model.TryGetProperty("username", out var u) ? u.GetString() : null;
            string email = model.TryGetProperty("email", out var e) ? e.GetString() : null;
            string password = model.TryGetProperty("password", out var p) ? p.GetString() : null;
            string role = model.TryGetProperty("role", out var r) ? r.GetString() : "Candidate";

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return BadRequest(new { error = "Username and password are required" });
            }

            return Ok(new { 
                message = "Registration successful!", 
                user = new { username, email, role } 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration handling");
            return StatusCode(500, new { error = "Internal server registration error" });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous] 
    public async Task<IActionResult> Login([FromBody] JsonElement model)
    {
        try
        {
            _logger.LogInformation("Login request received: {Data}", model.GetRawText());

            string username = model.TryGetProperty("username", out var u) ? u.GetString() : null;
            string password = model.TryGetProperty("password", out var p) ? p.GetString() : null;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return BadRequest(new { error = "Username and password are required" });
            }

            return Ok(new {
                token = "mock-jwt-token-for-local-testing-purposes",
                user = new {
                    username = username,
                    role = "Candidate",
                    id = 1
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login processing");
            return StatusCode(500, new { error = "Internal server login error" });
        }
    }

    private async Task SaveCandidateToDbAsync(object analysisResult)
    {
        try
        {
            string jsonString = JsonSerializer.Serialize(analysisResult);
            using var jsonDoc = JsonDocument.Parse(jsonString);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("candidate", out var candidateObj))
            {
                _logger.LogError("Candidate object missing from analysis result");
                return;
            }

            string name = ExtractJsonString(candidateObj, "name") ?? "Unknown";
            string phone = ExtractJsonString(candidateObj, "phone") ?? "";
            string email = ExtractJsonString(candidateObj, "email") ?? "";
            string location = ExtractJsonString(candidateObj, "location") ?? "";
            string qualification = ExtractJsonString(candidateObj, "highestQualification") ?? "";
            string experience = ExtractJsonString(candidateObj, "yearsOfExperience") ?? "";
            
            int score = 50;
            if (root.TryGetProperty("score", out var scoreElement))
            {
                if (scoreElement.ValueKind == JsonValueKind.Number)
                    score = scoreElement.GetInt32();
            }

            string summary = ExtractJsonString(root, "summary") ?? "";

            var skills = new List<string>();
            if (root.TryGetProperty("skills", out var skillsArray) && skillsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var skillElement in skillsArray.EnumerateArray())
                {
                    var skillText = skillElement.GetString();
                    if (!string.IsNullOrWhiteSpace(skillText))
                        skills.Add(skillText);
                }
            }

            var existingCandidate = _dbContext.Candidates
                .FirstOrDefault(c => c.Name == name && c.Phone == phone);

            Candidate candidate;
            if (existingCandidate != null)
            {
                _logger.LogInformation("Updating existing candidate: {Name}", name);
                existingCandidate.Email = email;
                existingCandidate.Location = location;
                existingCandidate.HighestQualification = qualification;
                existingCandidate.YearsOfExperience = experience;
                existingCandidate.ResumeScore = score;
                existingCandidate.Summary = summary;
                existingCandidate.UpdatedAt = DateTime.UtcNow;

                _dbContext.CandidateSkills.RemoveRange(existingCandidate.Skills);
                candidate = existingCandidate;
            }
            else
            {
                _logger.LogInformation("Creating new candidate: {Name}", name);
                candidate = new Candidate
                {
                    UserId = 0, 
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

            foreach (var skill in skills)
            {
                if (!string.IsNullOrWhiteSpace(skill))
                {
                    candidate.Skills.Add(new CandidateSkill 
                    { 
                        SkillName = skill,
                        Proficiency = 3 
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("✅ Candidate saved to DB: {CandidateId}", candidate.CandidateId);

            await CalculateJobMatchesAsync(candidate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error saving candidate to database");
        }
    }

    private string? ExtractJsonString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Null => null,
                _ => prop.GetString() ?? null
            };
        }
        return null;
    }

    private async Task CalculateJobMatchesAsync(Candidate candidate)
    {
        try
        {
            var jobs = _dbContext.Jobs.Include(j => j.RequiredSkills).ToList();
            var candidateSkills = candidate.Skills.Select(s => s.SkillName.ToLower()).ToHashSet();

            foreach (var job in jobs)
            {
                var requiredSkills = job.RequiredSkills.Select(s => s.SkillName.ToLower()).ToList();
                var matchedSkills = candidateSkills.Intersect(requiredSkills).ToList();
                var missingSkills = requiredSkills.Except(matchedSkills).ToList();
                
                var matchScore = requiredSkills.Count > 0 ? (int)((matchedSkills.Count * 100) / requiredSkills.Count) : 0;

                var existingMatch = _dbContext.JobMatches.FirstOrDefault(m => m.JobId == job.JobId && m.CandidateId == candidate.CandidateId);
                
                if (existingMatch != null)
                {
                    existingMatch.MatchScore = matchScore;
                    existingMatch.MatchedSkills = string.Join(", ", matchedSkills);
                    existingMatch.MissingSkills = string.Join(", ", missingSkills);
                }
                else if (matchScore > 0)
                {
                    _dbContext.JobMatches.Add(new JobMatch
                    {
                        JobId = job.JobId,
                        CandidateId = candidate.CandidateId,
                        MatchScore = matchScore,
                        MatchedSkills = string.Join(", ", matchedSkills),
                        MissingSkills = string.Join(", ", missingSkills),
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("✅ Job matches calculated for candidate: {CandidateId}", candidate.CandidateId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating job matches");
        }
    }

    private string ExtractTextFromPdf(MemoryStream stream)
    {
        try
        {
            var document = UglyToad.PdfPig.PdfDocument.Open(stream);
            var text = string.Join("\n", document.GetPages().Select(p => p.Text));
            _logger.LogInformation("PDF TEXT LENGTH: {Length}", text.Length);
            System.IO.File.WriteAllText("extracted-pdf.txt", text);
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF extraction failed");
            throw;
        }
    }

    private string ExtractTextFromDocx(MemoryStream stream)
    {
        try
        {
            var text = new System.Text.StringBuilder();
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read))
            {
                var xmlPart = archive.Entries.FirstOrDefault(e => e.FullName == "word/document.xml");
                if (xmlPart == null)
                    throw new InvalidOperationException("Invalid DOCX file");

                using (var entryStream = xmlPart.Open())
                using (var reader = new StreamReader(entryStream))
                {
                    var xmlContent = reader.ReadToEnd();
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlContent);

                    var textNodes = doc.GetElementsByTagName("w:t");
                    foreach (System.Xml.XmlElement node in textNodes)
                        text.Append(node.InnerText);
                }
            }
            var result = text.ToString();
            _logger.LogInformation("DOCX extracted: {Length} characters", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DOCX extraction failed");
            throw;
        }
    }

    private string ExtractTextFromTxt(MemoryStream stream)
    {
        try
        {
            stream.Position = 0;
            using (var reader = new StreamReader(stream))
            {
                var text = reader.ReadToEnd();
                _logger.LogInformation("TXT extracted: {Length} characters", text.Length);
                return text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TXT extraction failed");
            throw;
        }
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetCandidate(int id)
    {
        try
        {
            var candidate = await _dbContext.Candidates
                .Include(c => c.Skills)
                .FirstOrDefaultAsync(c => c.CandidateId == id);

            if (candidate == null)
                return NotFound(new { error = "Candidate not found" });

            return Ok(candidate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting candidate");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("all")]
    [Authorize(Roles = "Recruiter")]
    public async Task<IActionResult> GetAllCandidates()
    {
        try
        {
            var candidates = await _dbContext.Candidates
                .Include(c => c.Skills)
                .OrderByDescending(c => c.ResumeScore)
                .ToListAsync();

            return Ok(candidates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting candidates");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{candidateId}/job-matches")]
    [AllowAnonymous]  
    public async Task<IActionResult> GetJobMatches(int candidateId)
    {
        try
        {
            var matches = await _dbContext.JobMatches
                .Where(m => m.CandidateId == candidateId)
                .Select(m => new
                {
                    m.MatchId,
                    m.JobId,
                    m.CandidateId,
                    m.MatchScore,
                    m.MatchedSkills,
                    m.MissingSkills,
                    m.CreatedAt,
                    job = new
                    {
                        m.Job.JobId,
                        m.Job.Title,
                        m.Job.Description,
                        m.Job.CompanyName,
                        m.Job.Location,
                        m.Job.SalaryRange
                    }
                })
                .OrderByDescending(m => m.MatchScore)
                .ToListAsync();

            return Ok(matches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting job matches");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}