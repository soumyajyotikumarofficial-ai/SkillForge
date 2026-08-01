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

    public async Task<string> GenerateTeamBreakdownAsync(string projectDescription, string techStack, DateTime deadline, string companyName, string companyType)
    {
        var prompt = $@"You are an expert technical hiring strategist and engineering team architect with 15+ years of experience building teams for top-tier technology companies.
Scientifically calculate the optimal delivery team for the inputs below. Internally score scope complexity, technical difficulty, timeline pressure, and scale/performance from 1-10. Use those scores to calibrate headcount, but do not output the scores.

INPUTS
Project description: {projectDescription}
Tech stack: {techStack}
Project deadline: {deadline:yyyy-MM-dd}
Company name: {companyName}
Company type: {companyType}

HEADCOUNT RULES
- Base team size on the project complexity, technical difficulty, deadline pressure, and scale.
- Never understaff critical backend, ML/AI, QA, DevOps, or security work when the project requires it.
- For every 3 developers, include at least 1 QA engineer.
- Above 8 total people, include a Project Manager. Above 12, include a Tech Lead or Engineering Manager.
- Keep mobile separate from web frontend. Keep ML/AI separate from backend. DevOps/Infrastructure is mandatory for cloud, containers, or queues.
- Do not assign all implementation skills to a manager. Every role must own a clear delivery area.

SENIORITY AND STACK RULES
- High complexity with a tight deadline requires more Senior/Lead engineers.
- Startups prefer a lean Mid + Senior mix; enterprises can use more Juniors with senior oversight.
- Map every technology in the supplied stack to at least one role's required_skills. Do not leave any technology unassigned.
- Flag conflicting technologies, unrealistic deadlines, skill gaps, contractor opportunities, and phased hiring recommendations.

Return only valid JSON using this exact shape, with no markdown or commentary:
{{
  ""project_summary"": {{ ""company"": """", ""complexity_level"": ""Low | Medium | High | Very High"", ""estimated_effort_person_months"": 0, ""feasibility"": ""Feasible | Tight | At Risk | Not Feasible"", ""feasibility_reason"": """" }},
  ""team"": [{{ ""role"": """", ""headcount"": 1, ""seniority"": ""Junior | Mid | Senior | Lead"", ""required_skills"": [], ""nice_to_have_skills"": [], ""responsibility"": """", ""reason_for_headcount"": """" }}],
  ""total_headcount"": 0,
  ""warnings"": [""at least one useful observation""],
  ""recommendations"": [""at least one actionable suggestion""]
}}

Use positive whole-number headcounts, specific responsibilities and headcount reasons, and never make a Senior Manager the only role for an engineering project. Every technology in the stack must appear in required_skills.";

        var text = await CallGeminiTextAsync(prompt);
        var normalized = TryNormalizeTeamBreakdown(text);
        if (normalized != null) return normalized;

        _logger.LogWarning("Team planner returned an invalid JSON response. Using the deterministic fallback team plan.");
        return BuildFallbackTeamBreakdown(projectDescription, techStack, deadline, companyName, companyType);
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

        // Required skills are a hard gate. Partial matches belong in a talent-gap
        // report, not in the qualified shortlist returned to recruiters.
        var qualifiedCandidates = requiredNormalized.Count == 0
            ? scored
            : scored.Where(s => s.Missing.Count == 0);

        var topCandidates = qualifiedCandidates
            .OrderByDescending(s => s.Score)
            .Take(topN)
            .Where(s => requiredNormalized.Count == 0 || s.Score == 100)
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
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 4096,
                    responseMimeType = "application/json"
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

    private static string? TryNormalizeTeamBreakdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = StripMarkdownFences(text);
        var start = cleaned.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < cleaned.Length; index++)
        {
            var character = cleaned[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }

            if (character == '"') inString = true;
            else if (character == '{') depth++;
            else if (character == '}' && --depth == 0)
            {
                var candidate = cleaned[start..(index + 1)];
                try
                {
                    using var document = JsonDocument.Parse(candidate);
                    if (document.RootElement.TryGetProperty("team", out var team) && team.ValueKind == JsonValueKind.Array)
                    {
                        return candidate;
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
                break;
            }
        }

        return null;
    }

    private static string BuildFallbackTeamBreakdown(string projectDescription, string techStack, DateTime deadline, string companyName, string companyType)
    {
        var roles = new object[]
        {
            new { role = "Product Manager", headcount = 1, seniority = "Senior", required_skills = new[] { "Product discovery", "Fintech workflows", "Agile delivery" }, nice_to_have_skills = new[] { "Merchant analytics" }, responsibility = "Own scope, delivery priorities, and stakeholder alignment.", reason_for_headcount = "One product owner is needed to keep the broad platform scope and four-month deadline aligned." },
            new { role = "Tech Lead", headcount = 1, seniority = "Lead", required_skills = new[] { "System architecture", "REST", "GraphQL", "Security design" }, nice_to_have_skills = new[] { "Fintech compliance" }, responsibility = "Own cross-service architecture, technical decisions, and integration standards.", reason_for_headcount = "A lead is required to coordinate multiple specialist workstreams and protect the latency target." },
            new { role = "Frontend Engineer", headcount = 2, seniority = "Senior", required_skills = new[] { "React", "TypeScript", "GraphQL", "Analytics dashboards" }, nice_to_have_skills = new[] { "Data visualization" }, responsibility = "Build the real-time monitoring dashboard and merchant analytics portal.", reason_for_headcount = "Two engineers can deliver the dashboard and portal in parallel within four months." },
            new { role = "Backend Engineer", headcount = 3, seniority = "Senior", required_skills = new[] { "Node.js", "REST API", "PostgreSQL", "Redis", "Kafka" }, nice_to_have_skills = new[] { "Nginx" }, responsibility = "Build transaction services, API gateway integrations, and low-latency business APIs.", reason_for_headcount = "Three engineers are needed for transaction processing, merchant APIs, and third-party integrations at five million transactions per day." },
            new { role = "ML Engineer", headcount = 2, seniority = "Senior", required_skills = new[] { "Python", "TensorFlow", "Anomaly detection", "Feature engineering" }, nice_to_have_skills = new[] { "Fraud modeling" }, responsibility = "Develop, evaluate, and serve fraud and anomaly-detection models.", reason_for_headcount = "Two ML engineers are needed to develop the model while establishing evaluation and production inference paths." },
            new { role = "Data Engineer", headcount = 1, seniority = "Senior", required_skills = new[] { "Kafka", "Python", "PostgreSQL", "Streaming data" }, nice_to_have_skills = new[] { "Data quality" }, responsibility = "Own transaction data pipelines, feature data, and analytics-ready datasets.", reason_for_headcount = "A dedicated data owner is needed to make high-volume streaming data reliable for both ML and reporting." },
            new { role = "Mobile Engineer", headcount = 1, seniority = "Mid", required_skills = new[] { "Flutter", "Mobile notifications", "Secure storage" }, nice_to_have_skills = new[] { "Push notification services" }, responsibility = "Build the merchant mobile alert and notification experience.", reason_for_headcount = "One focused mobile engineer can deliver the Flutter alert workflow without competing with web delivery." },
            new { role = "QA Engineer", headcount = 2, seniority = "Senior", required_skills = new[] { "API testing", "Performance testing", "Automation testing", "Security testing" }, nice_to_have_skills = new[] { "Kafka testing" }, responsibility = "Validate functional behavior, latency, scale, security, and regression coverage.", reason_for_headcount = "Two QA engineers are required to test parallel web, mobile, ML, and high-throughput API workstreams." },
            new { role = "DevOps / Cloud Engineer", headcount = 2, seniority = "Senior", required_skills = new[] { "Docker", "Kubernetes", "AWS", "Nginx", "CI/CD", "Observability" }, nice_to_have_skills = new[] { "Infrastructure as code" }, responsibility = "Build secure AWS environments, deployment automation, scaling, and service observability.", reason_for_headcount = "Cloud, containers, and a five-million-transaction daily target require dedicated infrastructure ownership and production support coverage." },
            new { role = "Security Engineer", headcount = 1, seniority = "Senior", required_skills = new[] { "API security", "Threat modeling", "Identity and access management", "Fintech security" }, nice_to_have_skills = new[] { "Compliance controls" }, responsibility = "Review gateway, data, authentication, and payment-related security risks.", reason_for_headcount = "A security specialist is needed because the platform processes sensitive financial transactions and third-party integrations." }
        };

        var plan = new
        {
            project_summary = new
            {
                company = companyName,
                complexity_level = "Very High",
                estimated_effort_person_months = 66,
                feasibility = "At Risk",
                feasibility_reason = "A five-million-transaction daily fintech platform with ML, web, mobile, integrations, and sub-200ms latency is a very large four-month delivery. The team can build a focused first release, but scope must be phased and performance risk tested early."
            },
            team = roles,
            total_headcount = 16,
            warnings = new[]
            {
                "Four months is aggressive for the requested scope and sub-200ms latency target.",
                "The first release should prioritize transaction monitoring, fraud scoring, and critical merchant alerts before secondary reporting features.",
                $"AI planning was unavailable, so this baseline was generated from the supplied stack and {companyType} context."
            },
            recommendations = new[]
            {
                "Run a two-week architecture and performance spike before committing to the full scope.",
                "Deliver in phases: core transaction/API platform, fraud model, dashboards, then mobile and advanced reports.",
                "Confirm ownership of every supplied technology before candidate matching."
            }
        };

        return JsonSerializer.Serialize(plan);
    }

    private static string TrimToWordLimit(string text, int maxWords)
    {
        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text.Trim() : string.Join(' ', words.Take(maxWords)) + "...";
    }
}
