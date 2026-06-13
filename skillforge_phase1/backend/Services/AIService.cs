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

            var promptText = $@"You are a professional resume parser. Extract ONLY the following information from the resume.
Return ONLY valid JSON with these exact keys. No other text.

{{
  ""candidate"": {{
    ""name"": ""Full name or 'Unknown'"",
    ""email"": ""Email address or 'N/A'"",
    ""phone"": ""Phone number or 'N/A'"",
    ""location"": ""City/Location or 'N/A'"",
    ""highestQualification"": ""Degree like B.Tech, MBA, etc or 'N/A'"",
    ""yearsOfExperience"": ""Number like 3.5 or 5 or 'N/A'""
  }},
  ""skills"": [""skill1"", ""skill2"", ""skill3""],
  ""score"": 75,
  ""summary"": ""Brief 2-sentence professional summary""
}}

RESUME:
{text}";

            var payload = new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = promptText } } }
                },
                generationConfig = new { temperature = 0.1, maxOutputTokens = 1024 }
            };

            var json = JsonSerializer.Serialize(payload);
            var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var responseText = await response.Content.ReadAsStringAsync();

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

_logger.LogInformation("========== RAW GEMINI OUTPUT ==========");
_logger.LogInformation(geminiText);
_logger.LogInformation("======================================");

// Remove markdown fences if Gemini adds them
geminiText = geminiText.Trim();

if (geminiText.StartsWith("```json"))
{
    geminiText = geminiText.Substring(7);
}

if (geminiText.StartsWith("```"))
{
    geminiText = geminiText.Substring(3);
}

if (geminiText.EndsWith("```"))
{
    geminiText = geminiText.Substring(0, geminiText.LastIndexOf("```"));
}

geminiText = geminiText.Trim();

// Find actual JSON object
var startIndex = geminiText.IndexOf('{');
var endIndex = geminiText.LastIndexOf('}');

if (startIndex >= 0 && endIndex > startIndex)
{
    geminiText = geminiText.Substring(
        startIndex,
        endIndex - startIndex + 1
    );
}

_logger.LogInformation("========== CLEANED JSON ==========");
_logger.LogInformation(geminiText);
_logger.LogInformation("==================================");

JsonDocument parsed;

try
{
    parsed = JsonDocument.Parse(geminiText);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to parse Gemini JSON");
    _logger.LogError("Gemini returned:");
    _logger.LogError(geminiText);

    return new
    {
        error = "Invalid JSON returned by Gemini",
        rawResponse = geminiText
    };
}

using (parsed)
{
    var root = parsed.RootElement;

    if (!root.TryGetProperty("candidate", out var candidateObj))
    {
        return new
        {
            error = "Candidate object missing",
            rawResponse = geminiText
        };
    }

    var candidate = new
    {
        name = GetStringValue(candidateObj, "name"),
        email = GetStringValue(candidateObj, "email"),
        phone = GetStringValue(candidateObj, "phone"),
        location = GetStringValue(candidateObj, "location"),
        highestQualification = GetStringValue(candidateObj, "highestQualification"),
        yearsOfExperience = GetStringValue(candidateObj, "yearsOfExperience")
    };

    var skills = new List<string>();

    if (root.TryGetProperty("skills", out var skillsArray))
    {
        foreach (var skill in skillsArray.EnumerateArray())
        {
            var skillText = skill.GetString();

            if (!string.IsNullOrWhiteSpace(skillText))
            {
                skills.Add(skillText);
            }
        }
    }

    int score = 50;

    if (root.TryGetProperty("score", out var scoreElement))
    {
        if (scoreElement.ValueKind == JsonValueKind.Number)
        {
            score = scoreElement.GetInt32();
        }
        else if (scoreElement.ValueKind == JsonValueKind.String)
        {
            int.TryParse(scoreElement.GetString(), out score);
        }
    }

    var summary = GetStringValue(root, "summary");

    return new
    {
        candidate,
        skills,
        score,
        summary
    };
}
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