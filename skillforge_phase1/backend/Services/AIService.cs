using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SkillForge.API.Services;

public class AIService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AIService> _logger;

    public AIService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AIService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    // Analyze resume: try Gemini if configured, otherwise fallback to local extraction
    public async Task<object> AnalyzeResumeAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new { error = "file not found" };

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string text = string.Empty;
        if (ext == ".txt")
        {
            text = await File.ReadAllTextAsync(filePath);
        }
        else
        {
            text = "(binary or unsupported format)";
        }

        // Local keyword list used as fallback and for scoring
        var keywords = new[] { "c#", "dotnet", "asp.net", "sql", "javascript", "typescript", "react", "angular", "python", "java" };

        // Attempt to call Gemini (if configured)
        try
        {
            var apiKey = _config["Gemini:ApiKey"];
            var endpoint = _config["Gemini:Endpoint"];
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(endpoint))
            {
                var client = _httpFactory.CreateClient();
                // Google Generative Language: pass API key as query param
                var sep = endpoint.Contains('?') ? '&' : '?';
                var url = endpoint + sep + "key=" + Uri.EscapeDataString(apiKey);

                var payload = new { prompt = new { text = text } };
                var json = JsonSerializer.Serialize(payload);
                var resp = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                var respText = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    // Best-effort parse: if response has a simple text field, try to extract it
                    string summary = respText;
                    try
                    {
                        using var doc = JsonDocument.Parse(respText);
                        // look for common fields
                        if (doc.RootElement.TryGetProperty("output", out var outElem)) summary = outElem.ToString();
                        else if (doc.RootElement.TryGetProperty("candidates", out var cand) && cand.GetArrayLength() > 0)
                        {
                            summary = cand[0].ToString();
                        }
                        else if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            if (choices[0].TryGetProperty("text", out var txt)) summary = txt.GetString() ?? summary;
                        }
                    }
                    catch { /* ignore parse errors, keep raw text */ }

                    var lower = text.ToLowerInvariant();
                    var skills = new List<string>();
                    foreach (var k in keywords)
                    {
                        if (lower.Contains(k)) skills.Add(k);
                    }

                    var score = CalculateScore(skills, text);

                    return new { summary, skills, score, raw = respText };
                }
                else
                {
                    _logger.LogWarning("Gemini call failed: {Status} {Body}", resp.StatusCode, respText);
                }
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Gemini call error");
        }

        // Fallback local analysis, but try to extract text from common formats (docx, pdf)
        if (ext == ".docx")
        {
            try
            {
                text = ExtractTextFromDocx(filePath);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Docx extraction failed");
            }
        }
        else if (ext == ".pdf")
        {
            try
            {
                text = ExtractTextFromPdf(filePath);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "PDF extraction failed");
            }
        }

        var lowerFallback = text.ToLowerInvariant();
        var skillsFallback = new List<string>();
        foreach (var k in keywords)
        {
            if (lowerFallback.Contains(k)) skillsFallback.Add(k);
        }

        var summaryFallback = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
        var scoreFallback = CalculateScore(skillsFallback, text);
        return new { summary = summaryFallback, skills = skillsFallback, score = scoreFallback };
    }

    private static string ExtractTextFromDocx(string path)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml");
        if (entry == null) return string.Empty;
        using var s = entry.Open();
        using var sr = new StreamReader(s);
        var xml = sr.ReadToEnd();
        // crude strip of tags
        var text = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
        return System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static string ExtractTextFromPdf(string path)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private int CalculateScore(List<string> skills, string text)
    {
        var baseScore = skills.Count * 15; // each matched skill adds weight
        var lengthFactor = Math.Min(20, text.Length / 100); // give some points for longer resumes
        var score = baseScore + lengthFactor;
        if (score > 100) score = 100;
        return score;
    }
}
