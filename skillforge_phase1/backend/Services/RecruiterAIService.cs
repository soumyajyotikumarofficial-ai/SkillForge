using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkillForge.Data;

namespace SkillForge.API.Services;

/// <summary>
/// Shared Gemini-backed intelligence for the recruiter portal: company descriptions,
/// project team/skill planning, and candidate-to-requirement matching with explanations.
/// All methods are defensive - AI/network failures degrade to deterministic fallbacks
/// rather than breaking the recruiter workflow.
/// </summary>
public class RecruiterAIService : ICompanyDescriptionService, IProjectTeamPlannerService, ICandidateMatchingService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RecruiterAIService> _logger;
    private readonly SkillForgeDbContext _dbContext;

    public RecruiterAIService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<RecruiterAIService> logger, SkillForgeDbContext dbContext)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<string> GenerateCompanyDescriptionAsync(string companyName, string jobContext)
    {
        var safeCompanyName = string.IsNullOrWhiteSpace(companyName) ? "The Company" : companyName.Trim();

        var prompt = $@"Write a concise, professional company description for a company named '{safeCompanyName}'.
Context about the role/project being hired for: {jobContext}

Strict rules:
- Strictly under 200 words.
- Professional, corporate tone suitable for a job posting shown to candidates.
- Do not invent specific financial figures, founding dates, or legal claims you cannot verify.
- Return plain text only, no markdown, no headings, no bullet points.";

        var text = await CallGeminiTextAsync(prompt);
        if (string.IsNullOrWhiteSpace(text))
        {
            return TrimToWordLimit(
                $"{safeCompanyName} is a growing organization focused on delivering high-quality solutions for its customers. " +
                "The team values collaboration, technical excellence, and continuous learning, offering professionals a supportive " +
                "environment to grow their careers while contributing to meaningful, impactful work.", 200);
        }

        return TrimToWordLimit(text, 200);
    }

    public async Task<string> GenerateTeamBreakdownAsync(string projectDescription, DateTime deadline)
    {
        var prompt = $@"You are a technical staffing planner. Based on the project description and deadline below,
recommend a specific team composition (roles, seniority levels, and headcount) needed to deliver the project on time.

Project description: {projectDescription}
Deadline: {deadline:yyyy-MM-dd}

Return one short paragraph, e.g. ""Based on your project scope and deadline, we recommend 2 Senior .NET Engineers and 1 React Frontend Developer.""
Keep it under 120 words. Return plain text only, no markdown.";

        var text = await CallGeminiTextAsync(prompt);
        return string.IsNullOrWhiteSpace(text)
            ? "Based on the project scope and deadline provided, we recommend at least 1 Senior Engineer and 1 Mid-level Engineer covering the listed required skills. Adjust headcount as needed before proceeding to candidate matching."
            : text.Trim();
    }

    public async Task<List<CandidateMatchResult>> MatchCandidatesAsync(CandidateMatchCriteria criteria, int topN = 5)
    {
        var resumes = await _dbContext.CandidateResumes
            .Include(r => r.Candidate)
            .Where(r => r.IsActive && r.Candidate != null)
            .ToListAsync();

        var requiredNormalized = criteria.RequiredSkills
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var scored = new List<(SkillForge.Models.CandidateResume Resume, int Score, List<string> Matched, List<string> Missing)>();

        foreach (var resume in resumes)
        {
            var candidateSkills = ExtractSkillsFromJson(resume.ParsedResumeJson)
                .Select(s => s.ToLowerInvariant())
                .ToList();

            var matched = requiredNormalized
                .Where(req => candidateSkills.Any(cs => cs.Contains(req) || req.Contains(cs)))
                .ToList();
            var missing = requiredNormalized.Except(matched).ToList();

            int score = requiredNormalized.Count > 0
                ? (int)Math.Round((double)matched.Count / requiredNormalized.Count * 100)
                : 0;

            scored.Add((resume, score, matched, missing));
        }

        var topCandidates = scored
            .OrderByDescending(s => s.Score)
            .Take(topN)
            .Where(s => s.Score > 0)
            .ToList();

        if (topCandidates.Count == 0)
        {
            return new List<CandidateMatchResult>();
        }

        var explanations = await GenerateMatchExplanationsAsync(criteria, topCandidates);

        var results = new List<CandidateMatchResult>();
        for (int i = 0; i < topCandidates.Count; i++)
        {
            var (resume, score, matched, missing) = topCandidates[i];
            results.Add(new CandidateMatchResult
            {
                CandidateId = resume.CandidateId,
                CandidateName = resume.Candidate?.Name ?? "Candidate",
                MatchScore = score,
                MatchedSkills = matched,
                MissingSkills = missing,
                Explanation = explanations.ElementAtOrDefault(i) ?? BuildFallbackExplanation(score, matched)
            });
        }

        return results;
    }

    private async Task<List<string>> GenerateMatchExplanationsAsync(
        CandidateMatchCriteria criteria,
        List<(SkillForge.Models.CandidateResume Resume, int Score, List<string> Matched, List<string> Missing)> candidates)
    {
        try
        {
            var candidateSummaries = candidates.Select((c, idx) => new
            {
                index = idx,
                name = c.Resume.Candidate?.Name ?? "Candidate",
                matchScore = c.Score,
                matchedSkills = c.Matched,
                missingSkills = c.Missing
            });

            var prompt = $@"You are a recruiting assistant. For the role '{criteria.RoleTitle}' requiring skills [{string.Join(", ", criteria.RequiredSkills)}]
and {criteria.YearsOfExperience} years of experience, write a short 1-2 sentence explanation of why each candidate below is favored (or not) for the role,
referencing their matched/missing skills. Be specific and professional.

Candidates: {JsonSerializer.Serialize(candidateSummaries)}

Return a JSON array of strings, one explanation per candidate, in the same order as the input array. Return JSON only, no markdown.";

            var text = await CallGeminiTextAsync(prompt);
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            var cleaned = StripMarkdownFences(text);
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to deterministic candidate match explanations - AI explanation generation failed.");
        }

        return new List<string>();
    }

    private static string BuildFallbackExplanation(int score, List<string> matched)
    {
        if (score >= 80)
        {
            return $"Strong match ({score}%) - covers the core required skills: {string.Join(", ", matched)}.";
        }
        if (score >= 40)
        {
            return $"Reasonable match ({score}%) - partially aligned on: {string.Join(", ", matched)}.";
        }
        return $"Limited overlap ({score}%) with the stated requirements.";
    }

    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Case-insensitive so this reads both the current serialization and any legacy rows.
    private static List<string> ExtractSkillsFromJson(string parsedResumeJson)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ResumeAnalysisResult>(parsedResumeJson, CaseInsensitiveJsonOptions);
            return parsed?.Skills ?? new List<string>();
        }
        catch (JsonException)
        {
            // Malformed/legacy JSON payload - treat as no extractable skills rather than throwing.
            return new List<string>();
        }
    }

    /// <summary>Generic plain-text Gemini call shared by description, team-planning, and explanation prompts.</summary>
    private async Task<string?> CallGeminiTextAsync(string promptText)
    {
        try
        {
            var apiKey = _config["Gemini:ApiKey"];
            var endpoint = _config["Gemini:Endpoint"];

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogWarning("Gemini API credentials not configured - using deterministic fallback text.");
                return null;
            }

            var client = _httpFactory.CreateClient();
            var url = endpoint + "?key=" + Uri.EscapeDataString(apiKey);

            var payload = new
            {
                contents = new[]
                {
                    new { role = "user", parts = new[] { new { text = promptText } } }
                },
                generationConfig = new { temperature = 0.4, maxOutputTokens = 1000 }
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
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini text generation call failed.");
            return null;
        }
    }

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            text = Regex.Replace(text, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s*```$", "");
        }
        return text.Trim();
    }

    private static string TrimToWordLimit(string text, int maxWords)
    {
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text.Trim() : string.Join(' ', words.Take(maxWords)) + "...";
    }
}
