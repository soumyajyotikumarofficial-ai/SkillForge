using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using SkillForge.API.Services;

namespace SkillForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidateController : ControllerBase
{
    private readonly AIService _ai;
    public CandidateController(AIService ai) { _ai = ai; }

    [HttpGet]
    public IActionResult GetAll() => Ok(new[] { new { id = 1, name = "Sample Candidate" } });

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "no file" });

        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Resumes");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var fileName = Path.GetFileName(file.FileName);
        var savePath = Path.Combine(uploadsDir, fileName);

        using (var fs = System.IO.File.Create(savePath))
        {
            await file.CopyToAsync(fs);
        }

        var analysis = await _ai.AnalyzeResumeAsync(savePath);
        return Ok(new { file = fileName, analysis });
    }
}
