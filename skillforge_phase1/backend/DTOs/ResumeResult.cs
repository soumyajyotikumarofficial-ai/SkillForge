using System.Collections.Generic;

namespace SkillForge.DTOs;

public class ResumeResult
{
    public ResumeCandidate? Candidate { get; set; } = new ResumeCandidate();
    public List<string> Skills { get; set; } = new();
    public int Score { get; set; }
    public string? ScoreLabel { get; set; }
    public string? Summary { get; set; }
}

public class ResumeCandidate
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string HighestQualification { get; set; } = string.Empty;
    public string YearsOfExperience { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}
