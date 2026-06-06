using Microsoft.AspNetCore.Mvc;

namespace SkillForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecruiterController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { id = 1, name = "Sample Recruiter" });
}
