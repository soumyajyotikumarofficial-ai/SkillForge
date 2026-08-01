using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkillForge.API.Services;
using SkillForge.Data;
using SkillForge.DTOs;
using SkillForge.Models;

namespace SkillForge.Controllers;

/// <summary>
/// Workflow A: "Recruitment for Company" - standard job hiring, from posting through
/// AI-ranked matching to manual selection and the automated shortlisting email.
/// </summary>
[ApiController]
[Route("api/recruiter/jobs")]
[Authorize(Roles = "Recruiter")]
public class RecruiterJobController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly ICompanyDescriptionService _companyDescriptionService;
    private readonly ICandidateMatchingService _candidateMatchingService;
    private readonly IEmailNotificationService _emailService;
    private readonly ILogger<RecruiterJobController> _logger;

    public RecruiterJobController(
        SkillForgeDbContext dbContext,
        ICompanyDescriptionService companyDescriptionService,
        ICandidateMatchingService candidateMatchingService,
        IEmailNotificationService emailService,
        ILogger<RecruiterJobController> logger)
    {
        _dbContext = dbContext;
        _companyDescriptionService = companyDescriptionService;
        _candidateMatchingService = candidateMatchingService;
        _emailService = emailService;
        _logger = logger;
    }

    private int? GetRecruiterId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }

    // Lists all job requests posted by the current recruiter (for the dashboard's "my jobs" list).
    [HttpGet]
    public async Task<IActionResult> GetMyJobs()
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        var jobs = await _dbContext.CompanyJobRequests
            .Where(j => j.RecruiterId == recruiterId.Value)
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new CompanyJobRequestResponseDto
            {
                Id = j.Id,
                RoleTitle = j.RoleTitle,
                CompanyName = j.CompanyName,
                CompanyDescription = j.CompanyDescription ?? "",
                CreatedAt = j.CreatedAt
            })
            .ToListAsync();

        return Ok(jobs);
    }

    // Live preview of the AI-generated company description, called as the recruiter types the company name (no job is created/persisted).
    [HttpPost("preview-company-description")]
    public async Task<IActionResult> PreviewCompanyDescription([FromBody] CompanyDescriptionPreviewRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest(new { error = "CompanyName is required." });
        }

        var companyDescription = await _companyDescriptionService.GenerateCompanyDescriptionAsync(request.CompanyName.Trim(), jobContext: "");
        return Ok(new CompanyDescriptionPreviewResponseDto { CompanyDescription = companyDescription });
    }

    // Step 1-2: capture job inputs and auto-generate the AI company description.
    [HttpPost]
    public async Task<IActionResult> CreateJobRequest([FromBody] CreateCompanyJobRequestDto request)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        if (request == null || string.IsNullOrWhiteSpace(request.RoleTitle) || string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest(new { error = "RoleTitle and CompanyName are required." });
        }
        if (request.RequiredSkills == null || request.RequiredSkills.Count == 0)
        {
            return BadRequest(new { error = "At least one required skill must be provided." });
        }

        var jobContext = $"Role: {request.RoleTitle}. Description: {request.JobDescription}. Required skills: {string.Join(", ", request.RequiredSkills)}.";
        var companyDescription = await _companyDescriptionService.GenerateCompanyDescriptionAsync(request.CompanyName, jobContext);

        var jobRequest = new CompanyJobRequest
        {
            RecruiterId = recruiterId.Value,
            RoleTitle = request.RoleTitle.Trim(),
            JobDescription = request.JobDescription?.Trim() ?? "",
            RequiredSkills = string.Join(",", request.RequiredSkills.Select(s => s.Trim())),
            YearsOfExperience = request.YearsOfExperience?.Trim() ?? "",
            SalaryRange = request.SalaryRange?.Trim(),
            WorkModes = string.Join(",", request.WorkModes ?? new()),
            Locations = string.Join(",", request.Locations ?? new()),
            CompanyName = request.CompanyName.Trim(),
            CompanyDescription = companyDescription,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.CompanyJobRequests.Add(jobRequest);
        await _dbContext.SaveChangesAsync();

        return Ok(new CompanyJobRequestResponseDto
        {
            Id = jobRequest.Id,
            RoleTitle = jobRequest.RoleTitle,
            CompanyName = jobRequest.CompanyName,
            CompanyDescription = jobRequest.CompanyDescription ?? "",
            CreatedAt = jobRequest.CreatedAt
        });
    }

    // Step 3: AI candidate matching against ParsedResumeJson profiles, ranked with explanations.
    [HttpGet("{id:int}/candidates")]
    public async Task<IActionResult> GetRankedCandidates(int id)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        var jobRequest = await _dbContext.CompanyJobRequests
            .FirstOrDefaultAsync(j => j.Id == id && j.RecruiterId == recruiterId.Value);
        if (jobRequest == null) return NotFound(new { error = "Job request not found." });

        var criteria = new CandidateMatchCriteria
        {
            RoleTitle = jobRequest.RoleTitle,
            RequiredSkills = jobRequest.RequiredSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            YearsOfExperience = jobRequest.YearsOfExperience,
            Locations = jobRequest.Locations.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            WorkModes = jobRequest.WorkModes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
        };

        var matches = await _candidateMatchingService.MatchCandidatesAsync(criteria);

        var ranked = new System.Collections.Generic.List<RankedCandidateDto>();
        foreach (var match in matches)
        {
            var candidate = await _dbContext.Candidates.FindAsync(match.CandidateId);
            if (candidate == null) continue;

            var shortlist = await _dbContext.CandidateShortlists.FirstOrDefaultAsync(s =>
                s.WorkflowType == HiringWorkflowType.CompanyJob && s.CompanyJobRequestId == id && s.CandidateId == match.CandidateId);

            if (shortlist == null)
            {
                shortlist = new CandidateShortlist
                {
                    WorkflowType = HiringWorkflowType.CompanyJob,
                    CompanyJobRequestId = id,
                    CandidateId = match.CandidateId,
                    MatchScore = match.MatchScore,
                    MatchExplanation = match.Explanation,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.CandidateShortlists.Add(shortlist);
            }
            else
            {
                shortlist.MatchScore = match.MatchScore;
                shortlist.MatchExplanation = match.Explanation;
            }

            ranked.Add(new RankedCandidateDto
            {
                ShortlistId = shortlist.Id,
                CandidateId = candidate.CandidateId,
                Name = candidate.Name,
                HighestQualification = candidate.HighestQualification,
                YearsOfExperience = candidate.YearsOfExperience,
                MatchScore = match.MatchScore,
                MatchedSkills = match.MatchedSkills,
                MissingSkills = match.MissingSkills,
                Explanation = match.Explanation
            });
        }

        await _dbContext.SaveChangesAsync();
        // Re-attach generated shortlist IDs after the insert.
        foreach (var dto in ranked.Where(r => r.ShortlistId == 0))
        {
            var persisted = await _dbContext.CandidateShortlists.FirstOrDefaultAsync(s =>
                s.WorkflowType == HiringWorkflowType.CompanyJob && s.CompanyJobRequestId == id && s.CandidateId == dto.CandidateId);
            if (persisted != null) dto.ShortlistId = persisted.Id;
        }

        return Ok(ranked.OrderByDescending(r => r.MatchScore).ToList());
    }

    // Step 4: recruiter manually selects a candidate - reveal contact info and trigger the shortlisting email.
    [HttpPost("{id:int}/candidates/{candidateId:int}/select")]
    public async Task<IActionResult> SelectCandidate(int id, int candidateId)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        var jobRequest = await _dbContext.CompanyJobRequests
            .FirstOrDefaultAsync(j => j.Id == id && j.RecruiterId == recruiterId.Value);
        if (jobRequest == null) return NotFound(new { error = "Job request not found." });

        var candidate = await _dbContext.Candidates.FindAsync(candidateId);
        if (candidate == null) return NotFound(new { error = "Candidate not found." });

        var shortlist = await _dbContext.CandidateShortlists.FirstOrDefaultAsync(s =>
            s.WorkflowType == HiringWorkflowType.CompanyJob && s.CompanyJobRequestId == id && s.CandidateId == candidateId);
        if (shortlist == null) return BadRequest(new { error = "Run candidate matching before selecting a candidate." });

        shortlist.IsSelected = true;
        shortlist.ContactRevealed = true;

        var emailSent = await _emailService.SendShortlistEmailAsync(new ShortlistEmailModel
        {
            CandidateEmail = candidate.Email,
            CandidateName = candidate.Name,
            RoleTitle = jobRequest.RoleTitle,
            CompanyName = jobRequest.CompanyName
        });

        if (emailSent) shortlist.EmailSentAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new CandidateSelectionResponseDto
        {
            CandidateId = candidate.CandidateId,
            Name = candidate.Name,
            Email = candidate.Email,
            Phone = candidate.Phone,
            EmailSent = emailSent
        });
    }
}
