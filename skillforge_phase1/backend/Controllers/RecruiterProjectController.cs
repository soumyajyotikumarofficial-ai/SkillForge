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
/// Workflow B: "Recruitment for Project" - team/skill planning before candidate matching.
/// </summary>
[ApiController]
[Route("api/recruiter/projects")]
[Authorize(Roles = "Recruiter")]
public class RecruiterProjectController : ControllerBase
{
    private readonly SkillForgeDbContext _dbContext;
    private readonly ICompanyDescriptionService _companyDescriptionService;
    private readonly IProjectTeamPlannerService _teamPlannerService;
    private readonly ICandidateMatchingService _candidateMatchingService;
    private readonly IEmailNotificationService _emailService;
    private readonly ILogger<RecruiterProjectController> _logger;

    public RecruiterProjectController(
        SkillForgeDbContext dbContext,
        ICompanyDescriptionService companyDescriptionService,
        IProjectTeamPlannerService teamPlannerService,
        ICandidateMatchingService candidateMatchingService,
        IEmailNotificationService emailService,
        ILogger<RecruiterProjectController> logger)
    {
        _dbContext = dbContext;
        _companyDescriptionService = companyDescriptionService;
        _teamPlannerService = teamPlannerService;
        _candidateMatchingService = candidateMatchingService;
        _emailService = emailService;
        _logger = logger;
    }

    private int? GetRecruiterId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }

    // Step 1-2: capture project inputs, auto-generate the AI company description AND the
    // recommended team/skill breakdown (Step 1 of AI project analysis) - no candidates yet.
    [HttpPost]
    public async Task<IActionResult> CreateProjectRequest([FromBody] CreateProjectHiringRequestDto request)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        if (request == null || string.IsNullOrWhiteSpace(request.ProjectDescription) || string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest(new { error = "ProjectDescription and CompanyName are required." });
        }
        if (request.ProjectDeadline <= DateTime.UtcNow.Date)
        {
            return BadRequest(new { error = "ProjectDeadline must be a future date." });
        }

        var jobContext = $"Project: {request.ProjectDescription}. Role: {request.Role}. Required skills: {string.Join(", ", request.RequiredSkills)}.";
        var companyDescription = await _companyDescriptionService.GenerateCompanyDescriptionAsync(request.CompanyName, jobContext);
        var teamBreakdown = await _teamPlannerService.GenerateTeamBreakdownAsync(request.ProjectDescription, request.ProjectDeadline);

        var projectRequest = new ProjectHiringRequest
        {
            RecruiterId = recruiterId.Value,
            ProjectDescription = request.ProjectDescription.Trim(),
            Role = request.Role?.Trim() ?? "",
            RequiredSkills = string.Join(",", request.RequiredSkills ?? new()),
            YearsOfExperience = request.YearsOfExperience?.Trim() ?? "",
            SalaryRange = request.SalaryRange?.Trim(),
            WorkModes = string.Join(",", request.WorkModes ?? new()),
            Locations = string.Join(",", request.Locations ?? new()),
            CompanyName = request.CompanyName.Trim(),
            ProjectDeadline = request.ProjectDeadline,
            CompanyDescription = companyDescription,
            TeamBreakdownJson = teamBreakdown,
            TeamBreakdownApproved = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProjectHiringRequests.Add(projectRequest);
        await _dbContext.SaveChangesAsync();

        return Ok(new ProjectHiringRequestResponseDto
        {
            Id = projectRequest.Id,
            CompanyName = projectRequest.CompanyName,
            CompanyDescription = projectRequest.CompanyDescription ?? "",
            TeamBreakdown = projectRequest.TeamBreakdownJson ?? "",
            TeamBreakdownApproved = projectRequest.TeamBreakdownApproved,
            ProjectDeadline = projectRequest.ProjectDeadline,
            CreatedAt = projectRequest.CreatedAt
        });
    }

    // Step 2 gate: recruiter approves (or adjusts) the AI team suggestion before matching unlocks.
    [HttpPost("{id:int}/approve-team")]
    public async Task<IActionResult> ApproveTeamBreakdown(int id, [FromBody] ApproveTeamBreakdownDto request)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        var projectRequest = await _dbContext.ProjectHiringRequests
            .FirstOrDefaultAsync(p => p.Id == id && p.RecruiterId == recruiterId.Value);
        if (projectRequest == null) return NotFound(new { error = "Project request not found." });

        if (!string.IsNullOrWhiteSpace(request?.AdjustedTeamBreakdown))
        {
            projectRequest.TeamBreakdownJson = request.AdjustedTeamBreakdown.Trim();
        }
        projectRequest.TeamBreakdownApproved = true;
        await _dbContext.SaveChangesAsync();

        return Ok(new { success = true, teamBreakdown = projectRequest.TeamBreakdownJson });
    }

    // Step 2 (continued): AI candidate matching, only unlocked once the team breakdown is approved.
    [HttpGet("{id:int}/candidates")]
    public async Task<IActionResult> GetRankedCandidates(int id)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        var projectRequest = await _dbContext.ProjectHiringRequests
            .FirstOrDefaultAsync(p => p.Id == id && p.RecruiterId == recruiterId.Value);
        if (projectRequest == null) return NotFound(new { error = "Project request not found." });

        if (!projectRequest.TeamBreakdownApproved)
        {
            return BadRequest(new { error = "Approve the AI team/skill breakdown before matching candidates." });
        }

        var criteria = new CandidateMatchCriteria
        {
            RoleTitle = projectRequest.Role,
            RequiredSkills = projectRequest.RequiredSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            YearsOfExperience = projectRequest.YearsOfExperience,
            Locations = projectRequest.Locations.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            WorkModes = projectRequest.WorkModes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
        };

        var matches = await _candidateMatchingService.MatchCandidatesAsync(criteria);

        var ranked = new System.Collections.Generic.List<RankedCandidateDto>();
        foreach (var match in matches)
        {
            var candidate = await _dbContext.Candidates.FindAsync(match.CandidateId);
            if (candidate == null) continue;

            var shortlist = await _dbContext.CandidateShortlists.FirstOrDefaultAsync(s =>
                s.WorkflowType == HiringWorkflowType.Project && s.ProjectHiringRequestId == id && s.CandidateId == match.CandidateId);

            if (shortlist == null)
            {
                shortlist = new CandidateShortlist
                {
                    WorkflowType = HiringWorkflowType.Project,
                    ProjectHiringRequestId = id,
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
        foreach (var dto in ranked.Where(r => r.ShortlistId == 0))
        {
            var persisted = await _dbContext.CandidateShortlists.FirstOrDefaultAsync(s =>
                s.WorkflowType == HiringWorkflowType.Project && s.ProjectHiringRequestId == id && s.CandidateId == dto.CandidateId);
            if (persisted != null) dto.ShortlistId = persisted.Id;
        }

        return Ok(ranked.OrderByDescending(r => r.MatchScore).ToList());
    }

    // Step 2 finale: manual selection reveals contact info and fires the shortlisting email.
    [HttpPost("{id:int}/candidates/{candidateId:int}/select")]
    public async Task<IActionResult> SelectCandidate(int id, int candidateId)
    {
        var recruiterId = GetRecruiterId();
        if (recruiterId == null) return Unauthorized(new { error = "Invalid recruiter session." });

        var projectRequest = await _dbContext.ProjectHiringRequests
            .FirstOrDefaultAsync(p => p.Id == id && p.RecruiterId == recruiterId.Value);
        if (projectRequest == null) return NotFound(new { error = "Project request not found." });

        var candidate = await _dbContext.Candidates.FindAsync(candidateId);
        if (candidate == null) return NotFound(new { error = "Candidate not found." });

        var shortlist = await _dbContext.CandidateShortlists.FirstOrDefaultAsync(s =>
            s.WorkflowType == HiringWorkflowType.Project && s.ProjectHiringRequestId == id && s.CandidateId == candidateId);
        if (shortlist == null) return BadRequest(new { error = "Run candidate matching before selecting a candidate." });

        shortlist.IsSelected = true;
        shortlist.ContactRevealed = true;

        var emailSent = await _emailService.SendShortlistEmailAsync(new ShortlistEmailModel
        {
            CandidateEmail = candidate.Email,
            CandidateName = candidate.Name,
            RoleTitle = projectRequest.Role,
            CompanyName = projectRequest.CompanyName
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
