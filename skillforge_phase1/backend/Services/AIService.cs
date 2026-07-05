using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SkillForge.Data;
using SkillForge.Models;

namespace SkillForge.API.Services;

public class AIService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AIService> _logger;
    private readonly SkillForgeDbContext _dbContext;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AIService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AIService> logger, SkillForgeDbContext dbContext)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Unified entry-point: Receives the uploaded raw browser file, extracts its underlying content,
    /// and dispatches the structured string metrics straight into the Gemini AI Engine.
    /// </summary>
    public async Task<object?> ProcessAndAnalyzeResumeAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Empty or unreadable file container delivered to AI parsing pipeline.");
            return new { error = "No file data uploaded" };
        }

        try
        {
            string extractedText = string.Empty;
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            memoryStream.Position = 0; // Reset head stream layout positions for reader consumption

            // Step-routing handler checking file types
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
                return new { error = $"Unsupported document format layout: '{fileExtension}'." };
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return new { error = "Failed to parse character symbols out of the uploaded payload file." };
            }

            // Pull active real-time listings from 3rd party API to ensure fresh DB inventory before analysis
            await RefreshLiveJobBankAsync();

            // Route cleanly extracted textual data down to the Gemini analytics engine
            return await AnalyzeResumeAsync(extractedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal failure during document compilation phase processing on file: {Name}", file.FileName);
            return new { error = "Document file conversion processing exception", details = ex.Message };
        }
    }

    public async Task<object?> AnalyzeResumeAsync(string text)
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
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(year|years)\s+\1\b", "years", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            // MODIFIED PROMPT: Strengthened scoring constraints to ensure dynamic variation based on resume metrics
            var promptText = $@"
You are an expert HR Data Scientist, applicant screening engine, and ATS Resume Engineering Expert specializing in extracting clear structural metrics from engineering resumes.
Analyze the provided candidate resume text below and extract highly precise structured insights.

CRITICAL INSTRUCTIONS FOR DYNAMIC SCORING:
- Calculate a completely dynamic numerical suitability rating (""score"") between 1 and 100 based strictly on the individual candidate's profile.
- Do NOT reuse static placeholder values (like 50, 65, or 75) for every file.
- Base the score on: tech stack depth, projects listed, clear tool usage details, and overall completeness of data.
- A high score (85-100) must be reserved exclusively for candidates displaying exceptional specialization, framework mastery, or comprehensive real-world applications.
- Average or junior-level engineering resumes should scale proportionally between 45 and 75 based on experience gaps.

LINGUISTIC RECOVERY RULES:
1. **candidate.name**: Identify the individual name tokens hidden within continuous uppercase or unspaced strings. Convert them to clean Title Case with single spaces (e.g., 'SOUMYAJYOTIKUMAR' must become 'Soumyajyoti Kumar').
2. **candidate.yearsOfExperience**: Extract the numeric value and unit. Absolutely NEVER duplicate the word 'years' (never output '4 years years').
3. **Case Normalization**: For fields like names, job titles, and locations, convert solid UPPERCASE run-on strings into standard spaced Title Case.
4. **summary**: Synthesize a fluid, grammatically flawless 2-3 sentence technical professional summary highlighting their core development stack.

Resume Text to Repair and Parse:
{text}
";

            // Enforce structural JSON output guarantee using explicit Gemini response schema rules
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
                    temperature = 0.3, // Slightly bumped to allow better score calculation elasticity
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
                                    yearsOfExperience = new { type = "string" }
                                },
                                required = new[] { "name", "email", "phone", "location", "highestQualification", "yearsOfExperience" }
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

            _logger.LogInformation("========== RAW CLEANED JSON FROM GEMINI ==========");
            _logger.LogInformation("{Text}", geminiText);
            _logger.LogInformation("==================================================");

            using var parsed = JsonDocument.Parse(geminiText);
            var root = parsed.RootElement;

            if (!root.TryGetProperty("candidate", out var candidateObj))
            {
                _logger.LogError("Candidate object missing from inner parse extraction payload.");
                return null;
            }

            var candidateResult = new
            {
                name = GetStringValue(candidateObj, "name").Replace("years years", "years", StringComparison.OrdinalIgnoreCase).Trim(),
                email = GetStringValue(candidateObj, "email").Trim(),
                phone = GetStringValue(candidateObj, "phone").Trim(),
                location = GetStringValue(candidateObj, "location").Trim(),
                highestQualification = GetStringValue(candidateObj, "highestQualification").Trim(),
                yearsOfExperience = GetStringValue(candidateObj, "yearsOfExperience").Replace("years years", "years", StringComparison.OrdinalIgnoreCase).Trim()
            };

            var skills = new List<string>();
            if (root.TryGetProperty("skills", out var skillsArray))
            {
                foreach (var skill in skillsArray.EnumerateArray())
                {
                    var skillText = skill.GetString();
                    if (!string.IsNullOrWhiteSpace(skillText)) skills.Add(skillText);
                }
            }

            // FIX: Removed strict default value locks that normalized variations down to 65
            int score = 70; 
            if (root.TryGetProperty("score", out var scoreElement))
            {
                if (scoreElement.ValueKind == JsonValueKind.Number) 
                {
                    score = scoreElement.GetInt32();
                }
                else if (scoreElement.ValueKind == JsonValueKind.String && int.TryParse(scoreElement.GetString(), out var parsedScore)) 
                {
                    score = parsedScore;
                }
            }

            var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() ?? "" : "";

            return new
            {
                candidate = candidateResult,
                skills,
                score,
                summary
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing resume text parsing structure strings.");
            return null;
        }
    }

    /// <summary>
    /// Fetches live engineering jobs from the free Arbeitnow Job Board API and dynamically ingests them into the internal SQLite DB.
    /// </summary>
    public async Task RefreshLiveJobBankAsync()
    {
        try
        {
            _logger.LogInformation("🔄 Initializing dynamic live job ingestion synchronizer from external API...");
            var client = _httpFactory.CreateClient();
            var apiResponse = await client.GetAsync("https://www.arbeitnow.com/api/job-board-api");
            
            if (!apiResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("External Jobs feed returned status code {Code}. Defaulting to existing local database indexes.", apiResponse.StatusCode);
                return;
            }

            string rawJsonData = await apiResponse.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(rawJsonData);
            
            if (!document.RootElement.TryGetProperty("data", out var jobsArray) || jobsArray.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("External jobs feed payload did not match expected structural specifications.");
                return;
            }

            int ingestedCounter = 0;

            foreach (var externalJob in jobsArray.EnumerateArray())
            {
                string slugToken = externalJob.GetProperty("slug").GetString() ?? Guid.NewGuid().ToString();
                string title = externalJob.GetProperty("title").GetString() ?? "Software Engineer";
                string company = externalJob.GetProperty("company_name").GetString() ?? "Global Tech Corp";
                string location = externalJob.GetProperty("location").GetString() ?? "Remote";
                
                // Duplicate protection check
                bool jobExists = await _dbContext.Jobs.AnyAsync(j => j.Title == title && j.CompanyName == company);
                if (jobExists) continue;

                var newJobRecord = new Job
                {
                    Title = title,
                    CompanyName = company,
                    Location = location,
                    SalaryRange = "$85,000 - $125,000", 
                    Description = $"Live vacancy at {company}. Target tracking slug: {slugToken}.",
                    CreatedAt = DateTime.UtcNow,
                    RequiredSkills = new List<JobSkill>() 
                };

                // Heuristic mapping to seed required skills relations based on vacancy keywords
                var inferredSkills = new List<string> { "Git" };
                string lowerTitle = title.ToLower();

                if (lowerTitle.Contains("dot") || lowerTitle.Contains(".net") || lowerTitle.Contains("c#")) 
                    inferredSkills.AddRange(new[] { "C#", ".NET Core", "ASP.NET Core" });
                if (lowerTitle.Contains("python") || lowerTitle.Contains("data") || lowerTitle.Contains("ai") || lowerTitle.Contains("learning")) 
                    inferredSkills.AddRange(new[] { "Python", "Machine Learning", "Data Analytics" });
                if (lowerTitle.Contains("java") && !lowerTitle.Contains("script")) 
                    inferredSkills.AddRange(new[] { "Java", "Spring Boot" });
                if (lowerTitle.Contains("react") || lowerTitle.Contains("javascript") || lowerTitle.Contains("frontend") || lowerTitle.Contains("node")) 
                    inferredSkills.AddRange(new[] { "JavaScript", "React", "HTML5", "CSS3" });
                if (lowerTitle.Contains("cloud") || lowerTitle.Contains("devops") || lowerTitle.Contains("aws") || lowerTitle.Contains("docker")) 
                    inferredSkills.AddRange(new[] { "AWS", "Docker", "Cloud Architecture" });
                if (lowerTitle.Contains("sql") || lowerTitle.Contains("backend") || lowerTitle.Contains("database")) 
                    inferredSkills.AddRange(new[] { "SQL Server", "Database Design" });

                foreach (var skillName in inferredSkills.Distinct())
                {
                    newJobRecord.RequiredSkills.Add(new JobSkill
                    {
                        SkillName = skillName
                    });
                }

                _dbContext.Jobs.Add(newJobRecord);
                await _dbContext.SaveChangesAsync();
                ingestedCounter++;
                
                if (ingestedCounter >= 10) break; // Limit batch cycles to protect thread performance
            }

            _logger.LogInformation("✅ Success! Ingested {Count} fresh real live vacancies into database tables.", ingestedCounter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing external third party live sync operations.");
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