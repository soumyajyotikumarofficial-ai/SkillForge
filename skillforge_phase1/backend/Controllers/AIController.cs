using Microsoft.AspNetCore.Mvc;

namespace SkillForge.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AIController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { message = "AI OK" });
}
