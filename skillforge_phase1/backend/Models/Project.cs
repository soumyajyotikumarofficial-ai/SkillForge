namespace SkillForge.API.Models;

public class Project
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? RequirementPath { get; set; }
    public string? SkillsJson { get; set; }
}
