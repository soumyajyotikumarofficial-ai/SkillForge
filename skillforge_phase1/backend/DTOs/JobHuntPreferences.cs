using System.Collections.Generic;
using System.Linq;

namespace SkillForge.DTOs;

/// <summary>
/// Optional job-hunt targeting preferences supplied by the candidate alongside their resume upload.
/// Drives the Apify Job Scraper query in place of (or in addition to) the AI-deduced role/location.
/// </summary>
public class JobHuntPreferences
{
    public string? Country { get; set; }

    /// <summary>
    /// Up to 3 preferred locations to search against, in priority order.
    /// </summary>
    public List<string> LocationPreferences { get; set; } = new();

    /// <summary>
    /// Optional target role/title. When omitted, the AI-deduced role from the resume is used instead.
    /// </summary>
    public string? RoleAspiration { get; set; }

    /// <summary>
    /// Returns up to 3 non-empty, trimmed location preferences.
    /// </summary>
    public List<string> GetCleanLocations()
    {
        return LocationPreferences
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Take(3)
            .ToList();
    }

    public bool HasRoleAspiration => !string.IsNullOrWhiteSpace(RoleAspiration);
}
