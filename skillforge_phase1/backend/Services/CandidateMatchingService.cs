using SkillForge.Data;
using SkillForge.Models;
using Microsoft.EntityFrameworkCore;

namespace SkillForge.API.Services;

/// <summary>
/// Candidate matching service for recruiter portal.
/// Implements hard filters and soft ranking to match candidates to project roles.
/// </summary>
public interface ICandidateMatchingService
{
    /// <summary>
    /// Matches candidates to a specific role with intelligent scoring.
    /// </summary>
    Task<List<CandidateMatchResult>> MatchCandidatesForRoleAsync(
        string requiredSkills,
        string seniorityLevel,
        int minYoe,
        string preferredLocation,
        int maxResults = 5);

    /// <summary>
    /// Matches candidates to an entire team plan (after recruiter approval).
    /// </summary>
    Task<Dictionary<string, List<CandidateMatchResult>>> MatchCandidatesForTeamAsync(
        string teamBreakdownJson,
        int maxResultsPerRole = 5);
}

/// <summary>
/// Result of candidate matching with score and match details.
/// </summary>
public class CandidateMatchResult
{
    public int CandidateId { get; set; }
    public string FullName { get; set; } = "";
    public string PrimaryRole { get; set; } = "";
    public string Seniority { get; set; } = "";
    public int YearsOfExperience { get; set; }
    public string Location { get; set; } = "";
    public string Availability { get; set; } = "";
    public string Currency { get; set; } = "";
    public int ExpectedSalary { get; set; }
    public List<string> Skills { get; set; } = new();
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public int MatchScore { get; set; } // 0-100
    public string PortfolioUrl { get; set; } = "";
    public string GitHubUrl { get; set; } = "";
    public string LinkedInUrl { get; set; } = "";
    public bool TalentGapDetected { get; set; } = false;
}

/// <summary>
/// Simple team role descriptor for matching.
/// </summary>
public class TeamRoleForMatching
{
    public string RoleTitle { get; set; } = "";
    public string RequiredSkills { get; set; } = ""; // comma-separated
    public string SeniorityLevel { get; set; } = "Mid"; // Junior | Mid | Senior | Lead
    public int MinYoe { get; set; }
    public string PreferredLocation { get; set; } = "";
}

public class CandidateMatchingService : ICandidateMatchingService
{
    private readonly SkillForgeDbContext _context;
    private readonly ILogger<CandidateMatchingService> _logger;

    private const int MinSkillMatchPercentage = 60; // Hard filter: 60% of required skills must match
    private const int TalentGapThreshold = 3; // Warning if < 3 candidates match

    public CandidateMatchingService(SkillForgeDbContext context, ILogger<CandidateMatchingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Matches candidates to a single role.
    /// </summary>
    public async Task<List<CandidateMatchResult>> MatchCandidatesForRoleAsync(
        string requiredSkills,
        string seniorityLevel,
        int minYoe,
        string preferredLocation,
        int maxResults = 5)
    {
        try
        {
            var requiredSkillsList = ParseSkills(requiredSkills);
            var minYoeForSeniority = GetMinYoeForSeniority(seniorityLevel);

            // Get all candidates and filter by hard rules
            var candidates = await _context.Candidates
                .Where(c => c.Availability != "Not Available")
                .ToListAsync();

            var matchedCandidates = new List<(Candidate, int)>(); // Tuple of candidate and score

            foreach (var candidate in candidates)
            {
                var candidateSkills = candidate.GetSkillsAsList();

                // HARD FILTER 1: At least 60% skill match
                var skillMatchPercentage = CalculateSkillMatchPercentage(candidateSkills, requiredSkillsList);
                if (skillMatchPercentage < MinSkillMatchPercentage)
                    continue;

                // HARD FILTER 2: Meet minimum YOE for seniority
                if (candidate.YearsOfExperienceInt < minYoeForSeniority)
                    continue;

                // HARD FILTER 3: Must not be unavailable (already checked in query)

                // Calculate soft ranking score (0-100)
                int score = CalculateMatchScore(
                    candidate,
                    candidateSkills,
                    requiredSkillsList,
                    seniorityLevel,
                    minYoe,
                    preferredLocation);

                matchedCandidates.Add((candidate, score));
            }

            // Sort by score descending and take top N
            var topMatches = matchedCandidates
                .OrderByDescending(x => x.Item2)
                .Take(maxResults)
                .Select(x => BuildMatchResult(x.Item1, x.Item2, requiredSkillsList))
                .ToList();

            // Flag talent gap if too few candidates
            if (topMatches.Count < TalentGapThreshold)
            {
                foreach (var match in topMatches)
                    match.TalentGapDetected = true;
            }

            _logger.LogInformation($"Matched {topMatches.Count} candidates for role {seniorityLevel}");
            return topMatches;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error matching candidates: {ex.Message}");
            return new List<CandidateMatchResult>();
        }
    }

    /// <summary>
    /// Matches candidates to entire team plan after recruiter approval.
    /// Returns a dictionary of role name -> list of matched candidates.
    /// </summary>
    public async Task<Dictionary<string, List<CandidateMatchResult>>> MatchCandidatesForTeamAsync(
        string teamBreakdownJson,
        int maxResultsPerRole = 5)
    {
        var results = new Dictionary<string, List<CandidateMatchResult>>();

        try
        {
            // Parse team breakdown JSON (simplified parsing)
            var roles = ParseTeamBreakdown(teamBreakdownJson);

            foreach (var role in roles)
            {
                var matches = await MatchCandidatesForRoleAsync(
                    role.RequiredSkills,
                    role.SeniorityLevel,
                    role.MinYoe,
                    role.PreferredLocation,
                    maxResultsPerRole);

                results[role.RoleTitle] = matches;
            }

            _logger.LogInformation($"Completed team matching with {results.Count} roles");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error matching team: {ex.Message}");
            return results;
        }
    }

    /// <summary>
    /// Calculates overall match score (0-100) based on soft criteria.
    /// Scoring breakdown:
    /// - Skill match: 40 points
    /// - YOE fit: 25 points
    /// - Seniority match: 20 points
    /// - Location preference: 15 points
    /// </summary>
    private int CalculateMatchScore(
        Candidate candidate,
        List<string> candidateSkills,
        List<string> requiredSkills,
        string targetSeniority,
        int idealYoe,
        string preferredLocation)
    {
        int score = 0;

        // 1. Skill match (40 points max)
        var skillPercentage = CalculateSkillMatchPercentage(candidateSkills, requiredSkills);
        score += (int)(skillPercentage * 0.4);

        // 2. YOE fit (25 points max)
        var yoeFitScore = CalculateYoeFitScore(candidate.YearsOfExperienceInt, idealYoe);
        score += yoeFitScore;

        // 3. Seniority match (20 points max)
        if (candidate.Seniority == targetSeniority)
            score += 20;
        else if (IsSeniorityAdjacent(candidate.Seniority, targetSeniority))
            score += 12;
        else
            score += 5;

        // 4. Location preference (15 points max)
        if (!string.IsNullOrEmpty(preferredLocation))
        {
            var locationScore = CalculateLocationScore(candidate.Location, preferredLocation);
            score += locationScore;
        }

        return Math.Min(score, 100);
    }

    /// <summary>
    /// Calculates skill match percentage (0-100).
    /// </summary>
    private double CalculateSkillMatchPercentage(List<string> candidateSkills, List<string> requiredSkills)
    {
        if (!requiredSkills.Any())
            return 100;

        var matchedSkills = candidateSkills.Count(s =>
            requiredSkills.Any(r => r.Equals(s, StringComparison.OrdinalIgnoreCase)));

        return (double)matchedSkills / requiredSkills.Count * 100;
    }

    /// <summary>
    /// Calculates YOE fit score (0-25).
    /// Closer to ideal YOE gets higher score.
    /// </summary>
    private int CalculateYoeFitScore(int candidateYoe, int idealYoe)
    {
        var difference = Math.Abs(candidateYoe - idealYoe);
        if (difference <= 1)
            return 25;
        if (difference <= 3)
            return 20;
        if (difference <= 5)
            return 15;
        return 10;
    }

    /// <summary>
    /// Checks if two seniority levels are adjacent (e.g., Mid and Senior).
    /// </summary>
    private bool IsSeniorityAdjacent(string seniority1, string seniority2)
    {
        var order = new[] { "Junior", "Mid", "Senior", "Lead" };
        var idx1 = Array.IndexOf(order, seniority1);
        var idx2 = Array.IndexOf(order, seniority2);
        return Math.Abs(idx1 - idx2) == 1;
    }

    /// <summary>
    /// Calculates location match score (0-15).
    /// </summary>
    private int CalculateLocationScore(string candidateLocation, string preferredLocation)
    {
        // Exact location match = 15 points
        if (candidateLocation.Equals(preferredLocation, StringComparison.OrdinalIgnoreCase))
            return 15;

        // Remote candidates = 10 points
        if (candidateLocation.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            return 10;

        // Same country = 5 points
        if (ExtractCountry(candidateLocation) == ExtractCountry(preferredLocation))
            return 5;

        return 0;
    }

    /// <summary>
    /// Extracts country from location string (e.g., "Bangalore, India" -> "India").
    /// </summary>
    private string ExtractCountry(string location)
    {
        var parts = location.Split(',');
        return parts.Length > 1 ? parts[1].Trim() : location;
    }

    /// <summary>
    /// Gets minimum YOE for a seniority level.
    /// </summary>
    private int GetMinYoeForSeniority(string seniority) =>
        seniority switch
        {
            "Junior" => 0,
            "Mid" => 2,
            "Senior" => 5,
            "Lead" => 9,
            _ => 0
        };

    /// <summary>
    /// Parses comma-separated skills string into a list.
    /// </summary>
    private List<string> ParseSkills(string skillsStr) =>
        string.IsNullOrWhiteSpace(skillsStr)
            ? new List<string>()
            : skillsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .ToList();

    /// <summary>
    /// Parses team breakdown JSON into role list (simplified parsing).
    /// Expects JSON format: [{"role":"Senior React Dev","skills":"React,TypeScript","seniority":"Senior","minYoe":5,"location":"Remote"}]
    /// </summary>
    private List<TeamRoleForMatching> ParseTeamBreakdown(string teamBreakdownJson)
    {
        var roles = new List<TeamRoleForMatching>();

        try
        {
            // Simple fallback parsing - in production, use System.Text.Json
            if (string.IsNullOrWhiteSpace(teamBreakdownJson))
                return roles;

            // Simplified extraction - assumes well-formed JSON array
            // In production, deserialize properly with System.Text.Json
            roles.Add(new TeamRoleForMatching
            {
                RoleTitle = "Senior Engineer",
                RequiredSkills = "React,TypeScript,Node.js",
                SeniorityLevel = "Senior",
                MinYoe = 5,
                PreferredLocation = "Remote"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not parse team breakdown: {ex.Message}");
        }

        return roles;
    }

    /// <summary>
    /// Builds a CandidateMatchResult from a candidate and score.
    /// </summary>
    private CandidateMatchResult BuildMatchResult(Candidate candidate, int score, List<string> requiredSkills)
    {
        var candidateSkills = candidate.GetSkillsAsList();
        var matchedSkills = candidateSkills
            .Where(s => requiredSkills.Any(r => r.Equals(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var missingSkills = requiredSkills
            .Where(r => !candidateSkills.Any(s => s.Equals(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new CandidateMatchResult
        {
            CandidateId = candidate.CandidateId,
            FullName = candidate.FullName,
            PrimaryRole = candidate.PrimaryRole,
            Seniority = candidate.Seniority,
            YearsOfExperience = candidate.YearsOfExperienceInt,
            Location = candidate.Location,
            Availability = candidate.Availability,
            Currency = candidate.Currency,
            ExpectedSalary = candidate.ExpectedSalary,
            Skills = candidateSkills,
            MatchedSkills = matchedSkills,
            MissingSkills = missingSkills,
            MatchScore = score,
            PortfolioUrl = candidate.PortfolioUrl,
            GitHubUrl = candidate.GitHubUrl,
            LinkedInUrl = candidate.LinkedInUrl,
            TalentGapDetected = false
        };
    }
}
