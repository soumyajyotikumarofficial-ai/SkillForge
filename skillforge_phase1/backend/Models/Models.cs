using System;
using System.Collections.Generic;

namespace SkillForge.Models;

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
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Location { get; set; } = "";
    public string HighestQualification { get; set; } = "";
    public string YearsOfExperience { get; set; } = "";
    public int ResumeScore { get; set; } = 0;
    public string Summary { get; set; } = "";
    public string? ResumeFilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public virtual User? User { get; set; }
    public virtual ICollection<CandidateSkill> Skills { get; set; } = new List<CandidateSkill>();
    public virtual ICollection<JobMatch> JobMatches { get; set; } = new List<JobMatch>();
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string ApplyUrl { get; set; } = "";
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