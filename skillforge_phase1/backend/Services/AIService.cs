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

    public async Task<ResumeResult> AnalyzeResumeAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new ResumeResult { ScoreLabel = "File missing" };

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string text = ext switch
        {
            ".txt" => await File.ReadAllTextAsync(filePath),
            ".docx" => ExtractTextFromDocx(filePath),
            ".pdf" => ExtractTextFromPdf(filePath),
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ResumeResult
            {
                Candidate = new ResumeCandidate(),
                Skills = new List<string>(),
                Score = 0,
                ScoreLabel = "No text extracted",
                Summary = "Unable to read the resume content."
            };
        }

        var result = await TryAnalyzeWithGeminiAsync(text);
        if (result != null)
        {
            result.ScoreLabel = GetScoreLabel(result.Score);
            return result;
        }

        return BuildFallbackResult(text);
    }

    private async Task<ResumeResult?> TryAnalyzeWithGeminiAsync(string text)
    {
        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var model = _config["Gemini:ChatModel"] ?? "gemini-1.5-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var prompt = BuildPrompt(text);
        var body = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            },
            generationConfig = new { temperature = 0.1, maxOutputTokens = 1024 }
        };

        try
        {
            var client = _httpFactory.CreateClient();
            var payload = JsonSerializer.Serialize(body, _serializerOptions);
            var response = await client.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
            var raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini call failed: {Status} {Body}", response.StatusCode, raw);
                return null;
            }

            using var doc = JsonDocument.Parse(raw);
            var geminiText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            geminiText = StripMarkdownFence(geminiText);
            if (string.IsNullOrWhiteSpace(geminiText))
            {
                _logger.LogWarning("Gemini returned empty parsed text");
                return null;
            }

            var result = JsonSerializer.Deserialize<ResumeResult>(geminiText, _serializerOptions);
            if (result == null)
            {
                _logger.LogWarning("Gemini response could not be deserialized: {Raw}", geminiText);
                return null;
            }

            result.Skills ??= new List<string>();
            result.Candidate ??= new ResumeCandidate();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini response");
            return null;
        }
    }

    private static ResumeResult BuildFallbackResult(string text)
    {
        var candidate = ParseResumeFields(text);
        var keywords = new[] { "c#", "dotnet", "asp.net", "sql", "javascript", "typescript", "react", "angular", "python", "java" };
        var lower = text.ToLowerInvariant();
        var skills = new List<string>();
        foreach (var keyword in keywords)
        {
            if (lower.Contains(keyword)) skills.Add(keyword);
        }

        var score = CalculateScore(skills, text, keywords.Length);
        return new ResumeResult
        {
            Candidate = candidate,
            Skills = skills,
            Score = score,
            ScoreLabel = GetScoreLabel(score),
            Summary = text.Length > 240 ? text[..240].Trim() + "..." : text.Trim()
        };
    }

    private static string BuildPrompt(string resumeText)
    {
        return "You are a resume parser. Extract information from the resume below and return ONLY a JSON object.\n"
             + "No markdown, no explanation, no preamble. Return ONLY the JSON.\n\n"
             + "Return this exact structure:\n"
             + "{\n"
             + "  \"candidate\": {\n"
             + "    \"name\": \"full name or empty string\",\n"
             + "    \"email\": \"email or empty string\",\n"
             + "    \"phone\": \"phone or empty string\",\n"
             + "    \"highestQualification\": \"e.g. B.Tech, MBA, MSc or empty string\",\n"
             + "    \"yearsOfExperience\": \"e.g. 3.5 or 0 if fresher\",\n"
             + "    \"location\": \"city or empty string\"\n"
             + "  },\n"
             + "  \"skills\": [\"skill1\", \"skill2\", \"skill3\"],\n"
             + "  \"score\": 0,\n"
             + "  \"summary\": \"2-3 sentence professional summary of this candidate\"\n"
             + "}\n\n"
             + "Rules for score:\n"
             + "- 85-100: senior with strong skills and clear experience\n"
             + "- 70-84: mid-level with good skills\n"
             + "- 55-69: junior or fresher with decent skills\n"
             + "- 40-54: limited skills or unclear resume\n"
             + "- below 40: very weak resume\n\n"
             + "RESUME TEXT:\n"
             + resumeText;
    }

    private static string StripMarkdownFence(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```")) text = text[(text.IndexOf('\n') + 1)..];
        if (text.EndsWith("```")) text = text[..text.LastIndexOf("```")];
        return text.Trim();
    }

    private static ResumeCandidate ParseResumeFields(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var candidate = new ResumeCandidate();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(candidate.Name) && trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4)
            {
                var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var capitalized = 0;
                foreach (var word in words)
                {
                    if (word.Length > 0 && char.IsUpper(word[0])) capitalized++;
                }
                if (capitalized >= Math.Min(2, words.Length)) candidate.Name = trimmed;
            }

            if (string.IsNullOrEmpty(candidate.Email))
            {
                var emailMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "[\\w\\.-]+@[\\w\\.-]+\\.[A-Za-z]{2,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (emailMatch.Success) candidate.Email = emailMatch.Value;
            }

            if (string.IsNullOrEmpty(candidate.Phone))
            {
                var phoneMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "(\\+?\\d[\\d \\-.()]{6,}\\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (phoneMatch.Success) candidate.Phone = phoneMatch.Value.Trim();
            }

            if (string.IsNullOrEmpty(candidate.Location) && trimmed.Length >= 5)
            {
                if (trimmed.Contains(",") || trimmed.ToLowerInvariant().Contains("city") || trimmed.ToLowerInvariant().Contains("street") || trimmed.ToLowerInvariant().Contains("road"))
                {
                    candidate.Location = trimmed;
                }
            }

            if (string.IsNullOrEmpty(candidate.HighestQualification))
            {
                var lowLine = trimmed.ToLowerInvariant();
                if (lowLine.Contains("phd") || lowLine.Contains("doctor") || lowLine.Contains("mba") || lowLine.Contains("m.sc") || lowLine.Contains("msc") || lowLine.Contains("m.tech") || lowLine.Contains("master") || lowLine.Contains("b.tech") || lowLine.Contains("bsc") || lowLine.Contains("b.sc") || lowLine.Contains("bachelor"))
                {
                    candidate.HighestQualification = trimmed;
                }
            }

            if (string.IsNullOrEmpty(candidate.YearsOfExperience))
            {
                var expMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "(\\d+(?:\\.\\d+)?)\\s+(?:years|yrs)\\s+of\\s+experience|(\\d+(?:\\.\\d+)?)\\s+years|experience\\s*[:\\-]\\s*(\\d+(?:\\.\\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (expMatch.Success)
                {
                    for (int i = 1; i < expMatch.Groups.Count; i++)
                    {
                        if (expMatch.Groups[i].Success)
                        {
                            candidate.YearsOfExperience = expMatch.Groups[i].Value;
                            break;
                        }
                    }
                }
            }
        }

        return candidate;
    }

    private static string ExtractTextFromDocx(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null) return string.Empty;
        using var s = entry.Open();
        using var sr = new StreamReader(s);
        var xml = sr.ReadToEnd();
        var text = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string ExtractTextFromPdf(string path)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages()) sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static int CalculateScore(List<string> skills, string text, int totalKeywordCount)
    {
        if (totalKeywordCount <= 0) totalKeywordCount = 10;
        var skillRatio = skills.Count / (double)totalKeywordCount;
        var skillPoints = (int)Math.Round(skillRatio * 70);
        var lengthPoints = Math.Min(30, text.Length / 200);
        var score = skillPoints + lengthPoints;
        return Math.Clamp(score, 0, 100);
    }

    private static string GetScoreLabel(int score) => score switch
    {
        >= 85 => "Excellent",
        >= 70 => "Strong",
        >= 55 => "Good",
        >= 40 => "Fair",
        _ => "Needs Work"
    };
}
