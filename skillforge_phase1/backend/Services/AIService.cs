using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using SkillForge.API.Models;

namespace SkillForge.API.Services;

public class AIService
{
    // Simple placeholder analysis: extracts words that look like skills
    public async Task<object> AnalyzeResumeAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new { error = "file not found" };

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string text = string.Empty;
        if (ext == ".txt")
        {
            text = await File.ReadAllTextAsync(filePath);
        }
        else
        {
            // For non-txt files we return a placeholder analysis
            text = "(binary or unsupported format)";
        }

        // Very naive skill extraction: look for common tokens
        var skills = new List<string>();
        var keywords = new[] { "c#", "dotnet", "asp.net", "sql", "javascript", "typescript", "react", "angular", "python", "java" };
        var lower = text.ToLowerInvariant();
        foreach (var k in keywords)
        {
            if (lower.Contains(k)) skills.Add(k);
        }

        var summary = text.Length > 200 ? text.Substring(0, 200) + "..." : text;

        return new { summary, skills };
    }
}
