using System;
using System.Collections.Generic;

namespace SkillForge.Models;

// Email Audit Trail - Tracks all recruitment emails sent via SkillForge
public class EmailAuditLog
{
    public int Id { get; set; }
    public int RecruiterId { get; set; }
    public int CandidateId { get; set; }
    public int? CompanyJobRequestId { get; set; }
    public int? ProjectHiringRequestId { get; set; }
    public HiringWorkflowType WorkflowType { get; set; }

    public string RecipientEmail { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Position { get; set; } = "";
    public string Subject { get; set; } = "";

    public bool SendSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MessageId { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClickedAt { get; set; }

    public virtual Recruiter? Recruiter { get; set; }
    public virtual Candidate? Candidate { get; set; }
    public virtual CompanyJobRequest? CompanyJobRequest { get; set; }
    public virtual ProjectHiringRequest? ProjectHiringRequest { get; set; }
}

// User (Login)
public class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Candidate"; // Candidate or Recruiter
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
}

// Candidate Profile
public class Candidate
{
    public int CandidateId { get; set; }
    public int? UserId { get; set; }
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Location { get; set; } = "";
    public string HighestQualification { get; set; } = "";
    public string YearsOfExperience { get; set; } = ""; // Legacy field
    public int YearsOfExperienceInt { get; set; } = 0; // New standardized field (0-50)
    public int ResumeScore { get; set; } = 0;
    public string Summary { get; set; } = "";
    public string? ResumeFilePath { get; set; }
    public string PreferredWorkMode { get; set; } = "Hybrid";
    public int? ActiveResumeId { get; set; }
    
    // Enhanced Candidate Profile Fields
    public string PrimaryRole { get; set; } = ""; // Frontend Engineer, Backend Engineer, etc.
    public string Seniority { get; set; } = "Mid"; // Junior | Mid | Senior | Lead
    public string Availability { get; set; } = "2 Weeks"; // Immediate | 2 Weeks | 1 Month | Not Available
    public int ExpectedSalary { get; set; } = 0; // In base currency
    public string Currency { get; set; } = "INR"; // INR | USD | EUR | GBP
    public string PortfolioUrl { get; set; } = "";
    public string GitHubUrl { get; set; } = "";
    public string LinkedInUrl { get; set; } = "";
    public string Bio { get; set; } = "";
    
    // Skills stored as pipe-delimited string for SQLite compatibility (will convert to array in PostgreSQL)
    public string SkillsList { get; set; } = ""; // Skills separated by | e.g., "React|TypeScript|Node.js"
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual User? User { get; set; }
    public virtual CandidateResume? ActiveResume { get; set; }
    public virtual ICollection<CandidateSkill> Skills { get; set; } = new List<CandidateSkill>();
    public virtual ICollection<CandidateResume> Resumes { get; set; } = new List<CandidateResume>();
    public virtual ICollection<JobMatch> JobMatches { get; set; } = new List<JobMatch>();
    
    // Helper method to get skills as list
    public List<string> GetSkillsAsList() => 
        string.IsNullOrWhiteSpace(SkillsList) 
            ? new List<string>() 
            : SkillsList.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
    
    // Helper method to set skills from list
    public void SetSkillsFromList(IEnumerable<string> skills) => 
        SkillsList = string.Join("|", skills?.Where(s => !string.IsNullOrWhiteSpace(s)) ?? new List<string>());
}

// Candidate Skills
public class CandidateSkill
{
    public int Id { get; set; }
    public int CandidateId { get; set; }
    public string SkillName { get; set; } = "";
    public int Proficiency { get; set; } = 3; // 1-5 scale
    
    public virtual Candidate? Candidate { get; set; }
}

// Job Posting
public class Job
{
    public int JobId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Location { get; set; } = "";
    public string SalaryRange { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SourceCreatedAt { get; set; }
    public string ApplyUrl { get; set; } = "";
    public string FinalUrl { get; set; } = "";
    public string WorkMode { get; set; } = "Hybrid";
    public string Country { get; set; } = "";
    public string Benefits { get; set; } = ""; // Flattened, comma-separated list of job benefits
    
    public virtual ICollection<JobSkill> RequiredSkills { get; set; } = new List<JobSkill>();
    public virtual ICollection<JobMatch> Matches { get; set; } = new List<JobMatch>();
}

// Job Required Skills
public class JobSkill
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string SkillName { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public int ProficiencyLevel { get; set; } = 3; // 1-5 scale
    
    public virtual Job? Job { get; set; }
}

// Job Match Results
public class JobMatch
{
    public int MatchId { get; set; }
    public int JobId { get; set; }
    public int CandidateId { get; set; }
    public int MatchScore { get; set; } = 0; // 0-100%
    public string MatchedSkills { get; set; } = ""; // Comma-separated
    public string MissingSkills { get; set; } = ""; // Comma-separated
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual Job? Job { get; set; }
    public virtual Candidate? Candidate { get; set; }
}

public class JobFetchHistory
{
    public int JobFetchHistoryId { get; set; }
    public DateTime LastSuccessfulFetchUtc { get; set; } = DateTime.UtcNow;
    public string LastQuery { get; set; } = "";
    public string LastLocation { get; set; } = "";
    public string LastCountry { get; set; } = "";
    public int InsertedCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CandidateResume
{
    public int CandidateResumeId { get; set; }
    public int CandidateId { get; set; }
    public string FileName { get; set; } = "";
    public string FileExtension { get; set; } = "";
    public string ParsedResumeJson { get; set; } = "{}";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Candidate? Candidate { get; set; }
}

public class Recruiter
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Designation { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<CompanyJobRequest> JobRequests { get; set; } = new List<CompanyJobRequest>();
    public virtual ICollection<ProjectHiringRequest> ProjectRequests { get; set; } = new List<ProjectHiringRequest>();
}

// Workflow A: standard company job hiring request
public class CompanyJobRequest
{
    public int Id { get; set; }
    public int RecruiterId { get; set; }
    public string RoleTitle { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public string RequiredSkills { get; set; } = ""; // comma-separated
    public string YearsOfExperience { get; set; } = "";
    public string? SalaryRange { get; set; }
    public string WorkModes { get; set; } = ""; // comma-separated multi-select: WFH,Hybrid,WFO
    public string Locations { get; set; } = ""; // comma-separated multi-select
    public string CompanyName { get; set; } = "";
    public string? CompanyDescription { get; set; } // AI-generated, strictly under 200 words
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Recruiter? Recruiter { get; set; }
    public virtual ICollection<CandidateShortlist> Shortlist { get; set; } = new List<CandidateShortlist>();
}

// Workflow B: project-based team/skill planning hiring request
public class ProjectHiringRequest
{
    public int Id { get; set; }
    public int RecruiterId { get; set; }
    public string ProjectDescription { get; set; } = "";
    public string Role { get; set; } = "";
    public string RequiredSkills { get; set; } = ""; // comma-separated
    public string YearsOfExperience { get; set; } = "";
    public string? SalaryRange { get; set; }
    public string WorkModes { get; set; } = ""; // comma-separated multi-select
    public string Locations { get; set; } = ""; // comma-separated multi-select
    public string CompanyName { get; set; } = "";
    public DateTime ProjectDeadline { get; set; }
    public string? CompanyDescription { get; set; } // AI-generated, strictly under 200 words
    public string? TeamBreakdownJson { get; set; } // AI-generated team & skill breakdown recommendation
    public bool TeamBreakdownApproved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Recruiter? Recruiter { get; set; }
    public virtual ICollection<CandidateShortlist> Shortlist { get; set; } = new List<CandidateShortlist>();
}

public enum HiringWorkflowType
{
    CompanyJob = 1,
    Project = 2
}

// Ranked AI match + manual recruiter selection/contact-reveal/email audit trail, shared by both workflows
public class CandidateShortlist
{
    public int Id { get; set; }
    public HiringWorkflowType WorkflowType { get; set; }
    public int? CompanyJobRequestId { get; set; }
    public int? ProjectHiringRequestId { get; set; }
    public int CandidateId { get; set; }
    public int MatchScore { get; set; }
    public string MatchExplanation { get; set; } = "";
    public bool IsSelected { get; set; }
    public bool ContactRevealed { get; set; }
    public DateTime? EmailSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual CompanyJobRequest? CompanyJobRequest { get; set; }
    public virtual ProjectHiringRequest? ProjectHiringRequest { get; set; }
    public virtual Candidate? Candidate { get; set; }
}

/// <summary>
/// Company career pages registry for direct recruitment linking.
/// Enables candidates to access company career pages directly without relying solely on job scraping.
/// </summary>
public class CompanyCareer
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string LogoUrl { get; set; } = ""; // Clearbit Logo API or fallback
    public string CareersUrl { get; set; } = ""; // Direct link to careers page
    public string Industry { get; set; } = ""; // Technology, Finance, Healthcare, etc.
    public string Headquarters { get; set; } = ""; // City, Country
    public bool ScrapeSupported { get; set; } = false; // Whether Apify can scrape this company
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}