using Microsoft.AspNetCore.Mvc;
using SkillForge.API.Services;

namespace SkillForge.API.Controllers;

/// <summary>
/// Candidate matching endpoint for recruiter portal.
/// Matches candidates to project roles after team plan approval.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CandidateMatchingController : ControllerBase
{
    private readonly ICandidateMatchingService _matchingService;
    private readonly ILogger<CandidateMatchingController> _logger;

    public CandidateMatchingController(
        ICandidateMatchingService matchingService,
        ILogger<CandidateMatchingController> logger)
    {
        _matchingService = matchingService;
        _logger = logger;
    }

    /// <summary>
    /// Matches candidates to a specific role with intelligent scoring.
    /// </summary>
    /// <remarks>
    /// Query Parameters:
    /// - requiredSkills: Comma-separated list of required skills (e.g., "React,TypeScript,Node.js")
    /// - seniorityLevel: Junior, Mid, Senior, or Lead
    /// - minYoe: Minimum years of experience
    /// - preferredLocation: Preferred candidate location
    /// - maxResults: Maximum number of results to return (default: 5, max: 20)
    /// 
    /// Returns:
    /// - List of matched candidates sorted by match score (descending)
    /// - Each candidate includes match score, matched/missing skills, and profile links
    /// </remarks>
    [HttpGet("match-for-role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MatchRoleResponse>> MatchForRoleAsync(
        [FromQuery(Name = "requiredSkills")] string requiredSkills,
        [FromQuery(Name = "seniorityLevel")] string seniorityLevel = "Mid",
        [FromQuery(Name = "minYoe")] int minYoe = 2,
        [FromQuery(Name = "preferredLocation")] string preferredLocation = "Remote",
        [FromQuery(Name = "maxResults")] int maxResults = 5)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(requiredSkills))
            {
                return BadRequest(new { error = "requiredSkills parameter is required" });
            }

            if (maxResults > 20)
                maxResults = 20; // Cap at 20

            var matches = await _matchingService.MatchCandidatesForRoleAsync(
                requiredSkills,
                seniorityLevel,
                minYoe,
                preferredLocation,
                maxResults);

            var response = new MatchRoleResponse
            {
                TotalMatches = matches.Count,
                Candidates = matches,
                TalentGapDetected = matches.Count < 3
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in MatchForRoleAsync: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while matching candidates" });
        }
    }

    /// <summary>
    /// Matches candidates to an entire team plan after recruiter approval.
    /// This activates when a recruiter clicks "Approve Team Plan" in the UI.
    /// </summary>
    /// <remarks>
    /// Expects a POST request with team breakdown JSON from the recruiter's approved plan.
    /// Returns matched candidates for each role in the team plan.
    /// 
    /// Request body example:
    /// {
    ///   "teamBreakdownJson": "[{\"role\":\"Senior React Dev\",\"skills\":\"React,TypeScript\",\"seniority\":\"Senior\",\"minYoe\":5,\"location\":\"Remote\"}]"
    /// }
    /// 
    /// Response: Dictionary of role name -> list of matched candidates
    /// </remarks>
    [HttpPost("match-for-team")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<MatchTeamResponse>> MatchForTeamAsync([FromBody] MatchTeamRequest request)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request?.TeamBreakdownJson))
            {
                return BadRequest(new { error = "teamBreakdownJson is required" });
            }

            var matches = await _matchingService.MatchCandidatesForTeamAsync(request.TeamBreakdownJson);

            var response = new MatchTeamResponse
            {
                RolesMatched = matches.Count,
                CandidatesByRole = matches,
                TalentGapsDetected = matches.Values.Any(v => v.Any(c => c.TalentGapDetected))
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in MatchForTeamAsync: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while matching team" });
        }
    }

    /// <summary>
    /// Retrieves a candidate's full profile for shortlisting.
    /// </summary>
    [HttpGet("candidate/{candidateId}/profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public ActionResult<string> GetCandidateProfile(int candidateId)
    {
        try
        {
            // Placeholder for candidate profile retrieval
            // This would fetch from database and return candidate details
            return Ok(new { message = $"Profile for candidate {candidateId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error retrieving candidate profile: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while retrieving candidate profile" });
        }
    }
}

/// <summary>
/// Request model for matching candidates to a specific role.
/// </summary>
public class MatchRoleRequest
{
    public string RequiredSkills { get; set; } = "";
    public string SeniorityLevel { get; set; } = "Mid";
    public int MinYoe { get; set; } = 2;
    public string PreferredLocation { get; set; } = "Remote";
    public int MaxResults { get; set; } = 5;
}

/// <summary>
/// Response model for single role matching.
/// </summary>
public class MatchRoleResponse
{
    public int TotalMatches { get; set; }
    public List<CandidateMatchResult> Candidates { get; set; } = new();
    public bool TalentGapDetected { get; set; }
}

/// <summary>
/// Request model for matching candidates to a team plan.
/// </summary>
public class MatchTeamRequest
{
    public string TeamBreakdownJson { get; set; } = "";
}

/// <summary>
/// Response model for team plan matching.
/// </summary>
public class MatchTeamResponse
{
    public int RolesMatched { get; set; }
    public Dictionary<string, List<CandidateMatchResult>> CandidatesByRole { get; set; } = new();
    public bool TalentGapsDetected { get; set; }
}
