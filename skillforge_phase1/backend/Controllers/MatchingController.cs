using Microsoft.AspNetCore.Mvc;

namespace SkillForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchingController : ControllerBase
{
    [HttpGet]
    public IActionResult GetMatches() => Ok(new[] { new { candidateId = 1, score = 0.95 } });
}
