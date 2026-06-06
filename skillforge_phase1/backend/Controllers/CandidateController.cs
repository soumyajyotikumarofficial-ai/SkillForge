using Microsoft.AspNetCore.Mvc;

namespace SkillForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidateController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll() => Ok(new[] { new { id = 1, name = "Sample Candidate" } });
}
