using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkillForge.DTOs;

namespace SkillForge.API.Services;

public class AIService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AIService> _logger;

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AIService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AIService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<object> AnalyzeResumeAsync(string text)
{
    try
    {
        var apiKey = _config["Gemini:ApiKey"];
        var endpoint = _config["Gemini:Endpoint"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogError("Gemini API credentials not configured");
            return new { error = "API credentials missing" };
        }

        var client = _httpFactory.CreateClient();
        var url = endpoint + "?key=" + Uri.EscapeDataString(apiKey);

        // Pre-process the incoming text string to deterministically fix repeating units globally
        if (!string.IsNullOrWhiteSpace(text))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(year|years)\s+\1\b", "years", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        // 1. Generalized prompt explicitly addressing raw UPPERCASE run-ons and tokenizing bugs
        var promptText = $@"
You are an expert ATS resume parser specializing in recovering corrupted, unspaced text strings.
The provided resume text has lost its spacing layout (e.g., 'SOUMYAJYOTIKUMAR', 'SYSTEMENGINEER', or '3.5+YEARSOFPROVENEXPERIENCE').

 LINGUISTIC RECOVERY RULES:
1. **candidate.name**: Identify the individual name tokens hidden within the continuous uppercase string. Convert them to clean Title Case and separate with a single space (e.g., 'SOUMYAJYOTIKUMAR' must become 'Soumyajyoti Kumar').
2. **candidate.yearsOfExperience**: Extract the numeric value and the unit. Ensure you do NOT duplicate the word 'years' (absolutely NEVER output '3.5+ years years').
3. **Case Normalization**: For fields like names, job titles, and locations, convert solid UPPERCASE strings into standard Title Case with proper spacing. Do not pass run-on strings to the JSON.
4. **summary**: Synthesize a fluid, grammatically correct 2-3 sentence professional summary highlighting their core development stack with clean word spacing.

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
                        // FIXED: Now accurately delivering the rule prompt text alongside a unique cache buster
                        new { text = promptText },
                        new { text = $"[system_cache_refresh_token: {Guid.NewGuid()}]" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 2048,
                responseMimeType = "application/json",
                // Strict response schema definition ensuring data types map cleanly
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
                            description = "An overall suitability score from 0 to 100 based on the resume quality and completeness." 
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

        File.WriteAllText("gemini-response.json", responseText);
        _logger.LogInformation("FULL RESPONSE LENGTH: {Length}", responseText.Length);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API error: {StatusCode} {Body}", response.StatusCode, responseText);
            return new { error = "API error", details = responseText };
        }

        // Parse Gemini response
        using var doc = JsonDocument.Parse(responseText);

        var geminiText = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        _logger.LogInformation("========== RAW CLEANED JSON FROM GEMINI ==========");
        _logger.LogInformation(geminiText);
        _logger.LogInformation("==================================================");

        // Clean up text properties dynamically if a localized token slips through
        using var parsed = JsonDocument.Parse(geminiText);
        var root = parsed.RootElement;

        if (!root.TryGetProperty("candidate", out var candidateObj))
        {
            return new { error = "Candidate object missing", rawResponse = geminiText };
        }

        // Standard extraction helpers
        string CleanString(JsonElement element, string prop) => 
            element.TryGetProperty(prop, out var p) ? p.GetString()?.Replace("years years", "years", StringComparison.OrdinalIgnoreCase)?.Trim() ?? "" : "";

        var candidate = new
        {
            name = CleanString(candidateObj, "name"),
            email = CleanString(candidateObj, "email"),
            phone = CleanString(candidateObj, "phone"),
            location = CleanString(candidateObj, "location"),
            highestQualification = CleanString(candidateObj, "highestQualification"),
            yearsOfExperience = CleanString(candidateObj, "yearsOfExperience")
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

        int score = 50;
        if (root.TryGetProperty("score", out var scoreElement))
        {
            if (scoreElement.ValueKind == JsonValueKind.Number) score = scoreElement.GetInt32();
            else if (scoreElement.ValueKind == JsonValueKind.String && int.TryParse(scoreElement.GetString(), out var parsedScore)) score = parsedScore;
        }

        var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() ?? "" : "";

        return new
        {
            candidate,
            skills,
            score,
            summary
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error analyzing resume");
        return new { error = "Processing error", details = ex.Message };
    }
}
   private string GetStringValue(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var prop))
        return "N/A";

    return prop.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(prop.GetString())
            ? "N/A"
            : prop.GetString()!,

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
            var document = UglyToad.PdfPig.PdfDocument.Open(stream);
            var text = string.Join("\n", document.GetPages().Select(p => p.Text));
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting PDF");
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
                if (xmlPart == null) throw new InvalidOperationException("Invalid DOCX");

                using (var entryStream = xmlPart.Open())
                using (var reader = new StreamReader(entryStream))
                {
                    var xmlContent = reader.ReadToEnd();
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(xmlContent);
                    var textNodes = doc.GetElementsByTagName("w:t");
                    foreach (System.Xml.XmlElement node in textNodes)
                    {
                        text.Append(node.InnerText);
                    }
                }
            }
            return text.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting DOCX");
            throw;
        }
    }
}