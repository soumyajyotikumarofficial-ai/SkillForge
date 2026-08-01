using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillForge.API.Services;

/// <summary>
/// Generates a concise, professional company description (strictly under 200 words) from a
/// company name and surrounding job/project context.
/// </summary>
public interface ICompanyDescriptionService
{
    Task<string> GenerateCompanyDescriptionAsync(string companyName, string jobContext);
}

/// <summary>
/// Step 1 of the "Recruitment for Project" workflow: recommends a team & skill breakdown
/// based on the project description and deadline, before any candidate matching happens.
/// </summary>
public interface IProjectTeamPlannerService
{
    Task<string> GenerateTeamBreakdownAsync(string projectDescription, System.DateTime deadline);
}

/// <summary>Search criteria used to rank candidate resumes against a recruiter's hiring request.</summary>
public class CandidateMatchCriteria
{
    public string RoleTitle { get; set; } = "";
    public List<string> RequiredSkills { get; set; } = new();
    public string YearsOfExperience { get; set; } = "";
    public List<string> Locations { get; set; } = new();
    public List<string> WorkModes { get; set; } = new();
}

public class CandidateMatchResult
{
    public int CandidateId { get; set; }
    public string CandidateName { get; set; } = "";
    public int MatchScore { get; set; }
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public string Explanation { get; set; } = "";
}

/// <summary>
/// Compares recruiter hiring criteria against candidate <c>ParsedResumeJson</c> profiles and
/// returns a ranked shortlist with an AI-generated "why this candidate is favored" explanation.
/// </summary>
public interface ICandidateMatchingService
{
    Task<List<CandidateMatchResult>> MatchCandidatesAsync(CandidateMatchCriteria criteria, int topN = 5);
}
