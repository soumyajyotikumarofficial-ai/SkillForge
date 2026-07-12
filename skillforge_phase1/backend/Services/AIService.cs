using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SkillForge.Data;
using SkillForge.Models;
using SkillForge.DTOs;

namespace SkillForge.API.Services;

public class ResumeAnalysisResult
{
    public CandidateDetails Candidate { get; set; } = new();
    public List<string> Skills { get; set; } = new();
    public int Score { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class CandidateDetails
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string HighestQualification { get; set; } = string.Empty;
    public string YearsOfExperience { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; 
}

public class AIService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AIService> _logger;
    private readonly SkillForgeDbContext _dbContext;
    private readonly ApifyJobService _apifyJobService;

    public AIService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AIService> logger, SkillForgeDbContext dbContext, ApifyJobService apifyJobService)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
        _dbContext = dbContext;
        _apifyJobService = apifyJobService;
    }

    /// <summary>
    /// Unified entry-point: Parses the resume via Gemini first, matches targeted role keywords
    /// (unless an explicit <paramref name="preferences"/> role aspiration is supplied), 
    /// and invokes the Apify Job Scraper sync engine using the dynamically derived target query
    /// across up to 3 candidate-supplied location preferences.
    /// Returns null if the parsing or processing pipeline encounters an error.
    /// </summary>
    public async Task<ResumeAnalysisResult?> ProcessAndAnalyzeResumeAsync(IFormFile file, JobHuntPreferences? preferences = null)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Empty or unreadable file container delivered to AI parsing pipeline.");
            return null;
        }

        try
        {
            string extractedText = string.Empty;
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            if (fileExtension == ".pdf")
            {
                extractedText = ExtractTextFromPdf(memoryStream);
            }
            else if (fileExtension == ".docx")
            {
                extractedText = ExtractTextFromDocx(memoryStream);
            }
            else if (fileExtension == ".txt")
            {
                using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                extractedText = await reader.ReadToEndAsync();
            }
            else
            {
                _logger.LogWarning("Unsupported document format layout: '{Extension}'", fileExtension);
                return null;
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("Failed to parse character symbols out of the uploaded payload file.");
                return null;
            }

            // 1. Analyze the resume text first to isolate the candidate profile and role
            var analysisResult = await AnalyzeResumeAsync(extractedText);

            if (analysisResult == null || analysisResult.Candidate == null || string.IsNullOrWhiteSpace(analysisResult.Candidate.Role))
            {
                _logger.LogError("❌ [ANALYSIS FAILURE] AI engine failed to extract or deduce a valid professional role.");
                return null;
            }

            string searchJobQuery;

            // If the candidate explicitly supplied a role aspiration, honor it directly and skip AI role deduction.
            if (preferences != null && preferences.HasRoleAspiration)
            {
                searchJobQuery = preferences.RoleAspiration!.Trim();
                _logger.LogInformation("🎯 [ROLE ASPIRATION] Using candidate-supplied target role: '{Role}'", searchJobQuery);
            }
            else
            {
                string extractedRole = analysisResult.Candidate.Role;
                _logger.LogInformation("🔍 [ROLE DEDUCTION] AI determined candidate role title: '{Role}'", extractedRole);

                // Isolate the target keyword phrase (developer/tester/engineer) and what comes before it
                var match = Regex.Match(extractedRole, @".*?\b(developer|tester|engineer)\b", RegexOptions.IgnoreCase);

                searchJobQuery = match.Success ? match.Value.Trim() : extractedRole.Trim();
                _logger.LogInformation("🎯 [QUERY MATCH] Formulated Apify actor query string: '{Query}'", searchJobQuery);
            }

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal failure during document compilation phase processing on file: {Name}", file.FileName);
            return null;
        }
    }

    /// <summary>
    /// Transmits extracted resume text to the Gemini Engine using explicit structural JSON schema definitions.
    /// </summary>
    public async Task<ResumeAnalysisResult?> AnalyzeResumeAsync(string text)
    {
        try
        {
            var apiKey = _config["Gemini:ApiKey"];
            var endpoint = _config["Gemini:Endpoint"];

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogError("Gemini API credentials not configured");
                return null;
            }

            var client = _httpFactory.CreateClient();
            var url = endpoint + "?key=" + Uri.EscapeDataString(apiKey);

            if (!string.IsNullOrWhiteSpace(text))
            {
                text = Regex.Replace(text, @"\b(year|years)\s+\1\b", "years", RegexOptions.IgnoreCase);
            }

            var promptText = $@"
You are an expert HR Data Scientist, applicant screening engine, and ATS Resume Engineering Expert specializing in extracting clear structural metrics from engineering resumes.
Analyze the provided candidate resume text below and extract highly precise structured insights.

CRITICAL INSTRUCTIONS FOR DYNAMIC SCORING:
- Calculate a completely dynamic numerical suitability rating (""score"") between 1 and 100 based strictly on the individual candidate's profile.
- Do NOT reuse static placeholder values (like 50, 65, or 75) for every file.
- Base the score on: tech stack depth, projects listed, clear tool usage details, and overall completeness of data.

ROLE EXTRACTION & ANALYSIS RULES:
1. **candidate.role**: Identify the candidate's explicit job title from the text (e.g., ""Java Developer"", ""Automation Tester"", ""DevOps Engineer"").
2. **CRITICAL MANDATE**: If no clear role or job title is directly written or found in the resume text, you MUST analyze their tech stack, listed tools, frameworks, and project descriptions to deduce their role. Synthesize a professional title ending with keywords like 'Developer', 'Tester', or 'Engineer' based on your contextual analysis (e.g., if you see C#, ASP.NET Core, SQL, output ""DotNet Developer""). Do not leave this empty.

LINGUISTIC RECOVERY RULES:
1. **candidate.name**: Identify the individual name tokens hidden within continuous uppercase or unspaced strings. Convert them to clean Title Case with single spaces.
2. **candidate.yearsOfExperience**: Extract the numeric value and unit. Absolutely NEVER duplicate the word 'years'.
3. **Case Normalization**: Convert solid UPPERCASE run-on strings into standard spaced Title Case.
4. **summary**: Synthesize a fluid, grammatically flawless 2-3 sentence technical professional summary highlighting their core development stack.

Resume Text to Repair and Parse:
{text}
";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = promptText },
                            new { text = $"[system_cache_refresh_token: {Guid.NewGuid()}]" }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 4000,
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            candidate = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string" },
                                    email = new { type = "string" },
                                    phone = new { type = "string" },
                                    location = new { type = "string" },
                                    highestQualification = new { type = "string" },
                                    yearsOfExperience = new { type = "string" },
                                    role = new { type = "string", description = "The explicit job title or contextually deduced professional title based on tech stack analysis (e.g., DotNet Developer, Automation Tester, Software Engineer)." }
                                },
                                required = new[] { "name", "email", "phone", "location", "highestQualification", "yearsOfExperience", "role" }
                            },
                            skills = new
                            {
                                type = "array",
                                items = new { type = "string" }
                            },
                            score = new 
                            { 
                                type = "integer", 
                                description = "An overall dynamic suitability score from 1 to 100 based strictly on resume quality and details provided." 
                            },
                            summary = new { type = "string" }
                        },
                        required = new[] { "candidate", "skills", "score", "summary" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} {Body}", response.StatusCode, responseText);
                return null;
            }

            using var doc = JsonDocument.Parse(responseText);

            var geminiText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "{}";

            // Defensive Sanitization: Strip any markdown code fences (```json ... ```) if Gemini includes them
            geminiText = geminiText.Trim();
            if (geminiText.StartsWith("```"))
            {
                geminiText = Regex.Replace(geminiText, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
                geminiText = Regex.Replace(geminiText, @"\s*```$", "");
                geminiText = geminiText.Trim();
            }

            using var parsed = JsonDocument.Parse(geminiText);
            var root = parsed.RootElement;

            if (!root.TryGetProperty("candidate", out var candidateObj))
            {
                _logger.LogError("❌ [PARSING ERROR] Candidate object missing from inner parse extraction payload. Raw text: {RawText}", geminiText);
                return null;
            }

            var result = new ResumeAnalysisResult();
            result.Candidate = new CandidateDetails
            {
                Name = GetStringValue(candidateObj, "name").Replace("years years", "years", StringComparison.OrdinalIgnoreCase).Trim(),
                Email = GetStringValue(candidateObj, "email").Trim(),
                Phone = GetStringValue(candidateObj, "phone").Trim(),
                Location = GetStringValue(candidateObj, "location").Trim(),
                HighestQualification = GetStringValue(candidateObj, "highestQualification").Trim(),
                YearsOfExperience = GetStringValue(candidateObj, "yearsOfExperience").Replace("years years", "years", StringComparison.OrdinalIgnoreCase).Trim(),
                Role = GetStringValue(candidateObj, "role").Trim()
            };

            if (root.TryGetProperty("skills", out var skillsArray))
            {
                foreach (var skill in skillsArray.EnumerateArray())
                {
                    var skillText = skill.GetString();
                    if (!string.IsNullOrWhiteSpace(skillText)) result.Skills.Add(skillText);
                }
            }

            var heuristicScore = CalculateHeuristicScore(result, text);
            int? modelScore = TryParseModelScore(root);

            if (modelScore.HasValue)
            {
                // Keep model score as a weak signal; raw resume-derived heuristic drives final variance.
                var modelWeight = IsCommonStaticScore(modelScore.Value) ? 0.15 : 0.25;
                var blendedScore = (int)Math.Round((modelWeight * modelScore.Value) + ((1 - modelWeight) * heuristicScore));
                result.Score = Math.Clamp(blendedScore, 1, 100);
            }
            else
            {
                result.Score = heuristicScore;
            }

            result.Summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() ?? "" : "";

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing resume text parsing structure strings.");
            return null;
        }
    }

    /// <summary>
    /// Parameterless overload used by external triggers (like app startup background synchronizations or manual sync tasks)
    /// to clean up compiler complaints without interfering with the targeted resume pipeline workflows.
    /// </summary>
    public async Task RefreshLiveJobBankAsync()
    {
        await RefreshLiveJobBankAsync("Developer");
    }

    /// <summary>
    /// Exclusively requests live roles from the Apify Job Scraper actor using the dynamically isolated query parameter.
    /// </summary>
    public async Task RefreshLiveJobBankAsync(string searchQuery)
    {
        await RefreshLiveJobBankAsync(searchQuery, "United States", "US");
    }

    /// <summary>
    /// Requests live roles from the Apify Job Scraper actor for a specific target query, location and country
    /// (e.g. as supplied via candidate job-hunt preferences), applying strict Title+Company+Location de-duplication.
    /// </summary>
    public async Task RefreshLiveJobBankAsync(string searchQuery, string location, string country)
    {
        try
        {
            var apifyJobs = await _apifyJobService.FetchJobsAsync(searchQuery, location, country);

            _logger.LogInformation("📊 [APIFY] Extracted [{Count}] live jobs matching query variant: '{Query}' in '{Location}, {Country}'", apifyJobs.Count, searchQuery, location, country);

            if (apifyJobs.Count == 0)
            {
                _logger.LogWarning("⚠️ [APIFY] The actor run returned zero matching records for query: '{Terms}'", searchQuery);
                return;
            }

            int ingestedCounter = 0;
            int skippedCounter = 0;

            // Strict de-duplication requires evaluating Title, CompanyName, AND Location together.
            var existingJobs = await _dbContext.Jobs
                .Select(j => new { Title = j.Title.Trim(), CompanyName = j.CompanyName.Trim(), Location = j.Location.Trim() })
                .ToListAsync();

            _logger.LogInformation("🗄️ [LOCAL DB] Found {Count} total pre-existing items in local SQLite cache.", existingJobs.Count);

            foreach (var externalJob in apifyJobs)
            {
                string title = externalJob.Title.Trim();
                string company = externalJob.CompanyName.Trim();
                string jobLocation = externalJob.Location.Trim();

                _logger.LogInformation("📥 [APIFY EVALUATION] Examining item row: '{Title}' at '{Company}' ({Location})...", title, company, jobLocation);

                bool jobExists = existingJobs.Any(ej => ej.Title.Equals(title, StringComparison.OrdinalIgnoreCase)
                                                     && ej.CompanyName.Equals(company, StringComparison.OrdinalIgnoreCase)
                                                     && ej.Location.Equals(jobLocation, StringComparison.OrdinalIgnoreCase));

                if (jobExists)
                {
                    _logger.LogInformation("⏭️ [APIFY SKIP] Record variant already caught in indexing tables. Dropping row entry.");
                    skippedCounter++;
                    continue;
                }

                var newJobRecord = new Job
                {
                    Title = title,
                    CompanyName = company,
                    Location = jobLocation,
                    Country = externalJob.Country,
                    SalaryRange = string.IsNullOrWhiteSpace(externalJob.SalaryRange) ? "Competitive / Market Rate" : externalJob.SalaryRange,
                    Description = externalJob.Description,
                    ApplyUrl = externalJob.ApplyUrl,
                    Benefits = externalJob.Benefits,
                    CreatedAt = DateTime.UtcNow,
                    RequiredSkills = new List<JobSkill>()
                };

                _dbContext.Jobs.Add(newJobRecord);
                existingJobs.Add(new { Title = title, CompanyName = company, Location = jobLocation });
                ingestedCounter++;

                _logger.LogInformation("✅ [APIFY STAGE] Successfully added '{Title}' by '{Company}' to tracking context batch.", title, company);
            }

            if (ingestedCounter > 0)
            {
                _logger.LogInformation("💾 [LOCAL DB] Flushing tracked batch collection to database storage files...");
                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("🚀 [APIFY PROCESS COMPLETE] Loop finished. Ingested completely fresh unique items: {Ingested}. Duplicates skipped: {Skipped}.", ingestedCounter, skippedCounter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [APIFY CRITICAL FAILURE] Thread aborted due to an internal execution crash.");
        }
    }

    private string GetStringValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return "N/A";

        return prop.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(prop.GetString()) ? "N/A" : prop.GetString()!,
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "N/A"
        };
    }

    private static int? TryParseModelScore(JsonElement root)
    {
        if (!root.TryGetProperty("score", out var scoreElement))
        {
            return null;
        }

        if (scoreElement.ValueKind == JsonValueKind.Number && scoreElement.TryGetInt32(out var numericScore))
        {
            return Math.Clamp(numericScore, 1, 100);
        }

        if (scoreElement.ValueKind == JsonValueKind.String)
        {
            var scoreText = scoreElement.GetString() ?? string.Empty;
            var match = Regex.Match(scoreText, @"\d{1,3}");
            if (match.Success && int.TryParse(match.Value, out var parsedScore))
            {
                return Math.Clamp(parsedScore, 1, 100);
            }
        }

        return null;
    }

    private static bool IsCommonStaticScore(int score)
    {
        return score == 50 || score == 65 || score == 70 || score == 75;
    }

    private static int CalculateHeuristicScore(ResumeAnalysisResult result, string rawText)
    {
        var normalizedText = rawText?.ToLowerInvariant() ?? string.Empty;

        var uniqueSkills = result.Skills
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .Count();

        var skillPoints = Math.Min(25, uniqueSkills * 2);

        var experienceYears = ExtractYears(result.Candidate.YearsOfExperience);
        var experiencePoints = Math.Min(20, (int)Math.Round(experienceYears * 2.5));

        var qualification = (result.Candidate.HighestQualification ?? string.Empty).ToLowerInvariant();
        var qualificationPoints = qualification switch
        {
            var q when q.Contains("phd") || q.Contains("doctor") => 15,
            var q when q.Contains("master") || q.Contains("m.tech") || q.Contains("mba") => 12,
            var q when q.Contains("bachelor") || q.Contains("b.tech") || q.Contains("b.e") => 10,
            var q when q.Contains("diploma") => 6,
            _ => 4
        };

        var rolePoints = string.IsNullOrWhiteSpace(result.Candidate.Role) ? 0 : 6;

        var summaryWords = Regex.Matches(result.Summary ?? string.Empty, @"\b\w+\b").Count;
        var summaryPoints = Math.Min(8, summaryWords / 10);

        var rawTechDictionary = new[]
        {
            ".net", "asp.net", "c#", "java", "python", "sql", "azure", "aws", "docker", "kubernetes",
            "react", "angular", "node", "typescript", "javascript", "microservices", "devops", "ci/cd", "terraform", "git"
        };
        var rawTechHits = rawTechDictionary.Count(token => normalizedText.Contains(token, StringComparison.Ordinal));
        var rawTechPoints = Math.Min(20, rawTechHits * 2);

        var projectSignalDictionary = new[] { "project", "implemented", "developed", "designed", "deployed", "internship", "experience", "github", "production" };
        var projectSignalHits = projectSignalDictionary.Count(token => normalizedText.Contains(token, StringComparison.Ordinal));
        var projectSignalPoints = Math.Min(8, projectSignalHits);

        var wordCount = Regex.Matches(rawText ?? string.Empty, @"\b\w+\b").Count;
        var densityPoints = Math.Min(8, wordCount / 120);

        var baseScore = 12;
        var heuristic = baseScore
            + skillPoints
            + experiencePoints
            + qualificationPoints
            + rolePoints
            + summaryPoints
            + rawTechPoints
            + projectSignalPoints
            + densityPoints
            + ComputeDeterministicOffset(rawText ?? string.Empty);

        return Math.Clamp(heuristic, 1, 100);
    }

    private static int ComputeDeterministicOffset(string source)
    {
        var text = source ?? string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return (bytes[0] % 5) - 2; // deterministic offset in range [-2, +2]
    }

    private static double ExtractYears(string yearsText)
    {
        if (string.IsNullOrWhiteSpace(yearsText))
        {
            return 0;
        }

        var match = Regex.Match(yearsText, @"\d+(?:\.\d+)?");
        if (!match.Success)
        {
            return 0;
        }

        return double.TryParse(match.Value, out var years) ? years : 0;
    }

    private string ExtractTextFromPdf(MemoryStream stream)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(stream);
            var text = string.Join("\n", document.GetPages().Select(p => p.Text));
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text strings out of target PDF structure components.");
            throw;
        }
    }

    private string ExtractTextFromDocx(MemoryStream stream)
    {
        try
        {
            var text = new StringBuilder();
            using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read))
            {
                var xmlPart = archive.Entries.FirstOrDefault(e => e.FullName == "word/document.xml");
                if (xmlPart == null) throw new InvalidOperationException("Invalid open-xml structures detected for target DOCX file validation.");

                using (var entryStream = xmlPart.Open())
                using (var reader = new StreamReader(entryStream))
                {
                    var xmlContent = reader.ReadToEnd();
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlContent);
                    var textNodes = doc.GetElementsByTagName("w:t");
                    foreach (System.Xml.XmlElement node in textNodes)
                    {
                        text.Append(node.InnerText + " ");
                    }
                }
            }
            return text.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing parsing sequences on target OpenXML package elements.");
            throw;
        }
    }
}