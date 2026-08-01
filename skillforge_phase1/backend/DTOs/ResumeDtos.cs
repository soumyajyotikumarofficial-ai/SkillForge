using System;
using System.Collections.Generic;

namespace SkillForge.DTOs;

/// <summary>
/// Lightweight resume descriptor used to populate the login "Active Resume" selection menu.
/// </summary>
public class ResumeSummaryDto
{
    public int ResumeId { get; set; }
    public string FileName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DeducedRole { get; set; }
    public int Score { get; set; }
}

/// <summary>
/// Response returned after a resume upload is parsed and persisted as JSON only.
/// </summary>
public class ResumeUploadResponseDto
{
    public int ResumeId { get; set; }
    public string FileName { get; set; } = "";
    public int Score { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Skills { get; set; } = new();
    public int ResumeCount { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Full parsed detail for a single stored resume, used to redisplay a candidate's data on dashboard load
/// without requiring the file to be re-uploaded/re-parsed.
/// </summary>
public class ResumeDetailDto
{
    public int ResumeId { get; set; }
    public string FileName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Score { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Skills { get; set; } = new();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Location { get; set; } = "";
    public string HighestQualification { get; set; } = "";
    public string YearsOfExperience { get; set; } = "";
}

