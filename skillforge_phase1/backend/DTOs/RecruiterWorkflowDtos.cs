using System;
using System.Collections.Generic;

namespace SkillForge.DTOs;

// ===================== Workflow A: Recruitment for Company =====================

public class CreateCompanyJobRequestDto
{
    public string RoleTitle { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public List<string> RequiredSkills { get; set; } = new();
    public string YearsOfExperience { get; set; } = "";
    public string? SalaryRange { get; set; }
    public List<string> WorkModes { get; set; } = new(); // WFH / Hybrid / WFO (multi-select)
    public List<string> Locations { get; set; } = new(); // multi-select
    public string CompanyName { get; set; } = "";
}

public class CompanyJobRequestResponseDto
{
    public int Id { get; set; }
    public string RoleTitle { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyDescription { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class CompanyDescriptionPreviewRequestDto
{
    public string CompanyName { get; set; } = "";
}

public class CompanyDescriptionPreviewResponseDto
{
    public string CompanyDescription { get; set; } = "";
}

// ===================== Workflow B: Recruitment for Project =====================

public class CreateProjectHiringRequestDto
{
    public string ProjectDescription { get; set; } = "";
    public string Role { get; set; } = "";
    public List<string> RequiredSkills { get; set; } = new();
    public string YearsOfExperience { get; set; } = "";
    public string? SalaryRange { get; set; }
    public List<string> WorkModes { get; set; } = new();
    public List<string> Locations { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public DateTime ProjectDeadline { get; set; }
}

public class ProjectHiringRequestResponseDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string CompanyDescription { get; set; } = "";
    public string TeamBreakdown { get; set; } = "";
    public bool TeamBreakdownApproved { get; set; }
    public DateTime ProjectDeadline { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ApproveTeamBreakdownDto
{
    /// <summary>Optional recruiter-adjusted team breakdown text. When omitted, the AI suggestion is approved as-is.</summary>
    public string? AdjustedTeamBreakdown { get; set; }
}

// ===================== Shared: Candidate matching & selection =====================

/// <summary>Ranked candidate result surfaced to the recruiter BEFORE manual selection. Contact details are withheld.</summary>
public class RankedCandidateDto
{
    public int ShortlistId { get; set; }
    public int CandidateId { get; set; }
    public string Name { get; set; } = "";
    public string HighestQualification { get; set; } = "";
    public string YearsOfExperience { get; set; } = "";
    public int MatchScore { get; set; }
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public string Explanation { get; set; } = "";
}

/// <summary>Returned only after the recruiter manually selects a candidate - reveals contact info.</summary>
public class CandidateSelectionResponseDto
{
    public int CandidateId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool EmailSent { get; set; }
}
