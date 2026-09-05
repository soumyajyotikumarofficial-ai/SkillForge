using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SkillForge.Models;
using SkillForge.Data;
using SkillForge.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SkillForge.API.Controllers;

/// <summary>
/// Controller for managing recruitment emails sent when recruiters select candidates.
/// Automatically sends formal emails for both company job hiring and project-based hiring workflows.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecruitmentEmailController : ControllerBase
{
    private readonly SkillForgeDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<RecruitmentEmailController> _logger;

    public RecruitmentEmailController(
        SkillForgeDbContext context,
        IEmailService emailService,
        ILogger<RecruitmentEmailController> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    /// <summary>
    /// Sends a recruitment email when a recruiter selects a candidate for a company job.
    /// Email includes: company name, position, location, salary range, role details.
    /// </summary>
    /// <param name="companyJobRequestId">Company job request ID</param>
    /// <param name="candidateId">Candidate ID to send email to</param>
    /// <returns>Email send result</returns>
    [HttpPost("send-company-offer/{companyJobRequestId}/{candidateId}")]
    [ProducesResponseType(typeof(EmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendCompanyJobOfferAsync(int companyJobRequestId, int candidateId)
    {
        try
        {
            // Fetch the job request
            var jobRequest = _context.CompanyJobRequests
                .FirstOrDefault(j => j.Id == companyJobRequestId);
            
            if (jobRequest == null)
                return NotFound(new ErrorResponse { Message = "Company job request not found" });

            // Fetch the candidate
            var candidate = _context.Candidates
                .FirstOrDefault(c => c.CandidateId == candidateId);
            
            if (candidate == null)
                return NotFound(new ErrorResponse { Message = "Candidate not found" });

            if (string.IsNullOrEmpty(candidate.Email))
                return BadRequest(new ErrorResponse { Message = "Candidate has no email address on file" });

            // Build email template
            var template = new RecruitmentEmailTemplate
            {
                Recipient = candidate.Email,
                CompanyName = jobRequest.CompanyName,
                Position = jobRequest.RoleTitle,
                CandidateName = string.IsNullOrWhiteSpace(candidate.FullName) ? candidate.Name : candidate.FullName,
                Location = string.IsNullOrWhiteSpace(jobRequest.Locations) ? candidate.Location : jobRequest.Locations,
                SalaryRange = jobRequest.SalaryRange,
                RoleDetails = jobRequest.JobDescription,
                TeamInfo = jobRequest.CompanyDescription,
                AdditionalNotes = "We look forward to discussing this opportunity with you."
            };

            // Send email
            var result = await _emailService.SendCompanyJobOfferAsync(template);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to send company job offer email: {Error}", result.ErrorMessage);
                
                // Log to audit trail even on failure
                await LogEmailAttemptAsync(
                    jobRequest.RecruiterId,
                    candidateId,
                    companyJobRequestId,
                    null,
                    HiringWorkflowType.CompanyJob,
                    candidate.Email,
                    jobRequest.CompanyName,
                    jobRequest.RoleTitle,
                    success: false,
                    errorMessage: result.ErrorMessage,
                    messageId: null
                );

                return BadRequest(new ErrorResponse { Message = result.ErrorMessage ?? "Email delivery failed" });
            }

            // Log successful send
            await LogEmailAttemptAsync(
                jobRequest.RecruiterId,
                candidateId,
                companyJobRequestId,
                null,
                HiringWorkflowType.CompanyJob,
                candidate.Email,
                jobRequest.CompanyName,
                jobRequest.RoleTitle,
                success: true,
                errorMessage: null,
                messageId: result.MessageId
            );

            _logger.LogInformation("Company job offer email sent to {Email} for {Position} at {Company}",
                candidate.Email, jobRequest.RoleTitle, jobRequest.CompanyName);

            return Ok(new EmailResponse
            {
                Success = true,
                Message = $"Email sent successfully to {candidate.Email}",
                MessageId = result.MessageId,
                SentAt = result.SentAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending company job offer email");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Message = "An error occurred while sending the email" });
        }
    }

    /// <summary>
    /// Sends a recruitment email when a recruiter selects a candidate for a project.
    /// Email includes: company name, position, project name, location, project deadline, budget.
    /// </summary>
    /// <param name="projectHiringRequestId">Project hiring request ID</param>
    /// <param name="candidateId">Candidate ID to send email to</param>
    /// <returns>Email send result</returns>
    [HttpPost("send-project-offer/{projectHiringRequestId}/{candidateId}")]
    [ProducesResponseType(typeof(EmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendProjectOfferAsync(int projectHiringRequestId, int candidateId)
    {
        try
        {
            // Fetch the project request
            var projectRequest = _context.ProjectHiringRequests
                .FirstOrDefault(p => p.Id == projectHiringRequestId);
            
            if (projectRequest == null)
                return NotFound(new ErrorResponse { Message = "Project hiring request not found" });

            // Fetch the candidate
            var candidate = _context.Candidates
                .FirstOrDefault(c => c.CandidateId == candidateId);
            
            if (candidate == null)
                return NotFound(new ErrorResponse { Message = "Candidate not found" });

            if (string.IsNullOrEmpty(candidate.Email))
                return BadRequest(new ErrorResponse { Message = "Candidate has no email address on file" });

            // Build email template
            var template = new RecruitmentEmailTemplate
            {
                Recipient = candidate.Email,
                CompanyName = projectRequest.CompanyName,
                Position = projectRequest.Role,
                CandidateName = string.IsNullOrWhiteSpace(candidate.FullName) ? candidate.Name : candidate.FullName,
                Location = string.IsNullOrWhiteSpace(projectRequest.Locations) ? candidate.Location : projectRequest.Locations,
                ProjectName = projectRequest.ProjectDescription?.Substring(0, Math.Min(50, projectRequest.ProjectDescription.Length)),
                ProjectDescription = projectRequest.ProjectDescription,
                ProjectDeadline = projectRequest.ProjectDeadline,
                Budget = projectRequest.SalaryRange,
                AdditionalNotes = "This is an exciting opportunity to work on a high-impact project. We look forward to your interest and availability."
            };

            // Send email
            var result = await _emailService.SendProjectHiringEmailAsync(template);

            if (!result.Success)
            {
                _logger.LogWarning("Failed to send project offer email: {Error}", result.ErrorMessage);
                
                // Log to audit trail even on failure
                await LogEmailAttemptAsync(
                    projectRequest.RecruiterId,
                    candidateId,
                    null,
                    projectHiringRequestId,
                    HiringWorkflowType.Project,
                    candidate.Email,
                    projectRequest.CompanyName,
                    projectRequest.Role,
                    success: false,
                    errorMessage: result.ErrorMessage,
                    messageId: null
                );

                return BadRequest(new ErrorResponse { Message = result.ErrorMessage ?? "Email delivery failed" });
            }

            // Log successful send
            await LogEmailAttemptAsync(
                projectRequest.RecruiterId,
                candidateId,
                null,
                projectHiringRequestId,
                HiringWorkflowType.Project,
                candidate.Email,
                projectRequest.CompanyName,
                projectRequest.Role,
                success: true,
                errorMessage: null,
                messageId: result.MessageId
            );

            _logger.LogInformation("Project offer email sent to {Email} for {Position} at {Company}",
                candidate.Email, projectRequest.Role, projectRequest.CompanyName);

            return Ok(new EmailResponse
            {
                Success = true,
                Message = $"Email sent successfully to {candidate.Email}",
                MessageId = result.MessageId,
                SentAt = result.SentAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending project offer email");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Message = "An error occurred while sending the email" });
        }
    }

    /// <summary>
    /// Gets email audit log for a specific candidate shortlist entry.
    /// </summary>
    /// <param name="shortlistId">Candidate shortlist ID</param>
    /// <returns>Email audit log entries</returns>
    [HttpGet("audit-log/shortlist/{shortlistId}")]
    [ProducesResponseType(typeof(List<EmailAuditLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmailAuditLogAsync(int shortlistId)
    {
        try
        {
            var shortlist = _context.CandidateShortlists
                .FirstOrDefault(s => s.Id == shortlistId);
            
            if (shortlist == null)
                return NotFound(new ErrorResponse { Message = "Shortlist entry not found" });

            var logs = _context.EmailAuditLogs
                .Where(l => 
                    (shortlist.CompanyJobRequestId.HasValue && l.CompanyJobRequestId == shortlist.CompanyJobRequestId) ||
                    (shortlist.ProjectHiringRequestId.HasValue && l.ProjectHiringRequestId == shortlist.ProjectHiringRequestId)
                )
                .Where(l => l.CandidateId == shortlist.CandidateId)
                .OrderByDescending(l => l.SentAt)
                .ToList();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving email audit log");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Message = "An error occurred while retrieving audit log" });
        }
    }

    /// <summary>
    /// Gets all email audit logs for a recruiter.
    /// </summary>
    /// <param name="recruiterId">Recruiter ID</param>
    /// <returns>Email audit log entries</returns>
    [HttpGet("audit-log/recruiter/{recruiterId}")]
    [ProducesResponseType(typeof(List<EmailAuditLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecruiterEmailAuditLogAsync(int recruiterId)
    {
        try
        {
            var logs = _context.EmailAuditLogs
                .Where(l => l.RecruiterId == recruiterId)
                .OrderByDescending(l => l.SentAt)
                .ToList();

            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recruiter email audit log");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Message = "An error occurred while retrieving audit log" });
        }
    }

    /// <summary>
    /// Helper method to log email send attempts to EmailAuditLog table.
    /// </summary>
    private async Task LogEmailAttemptAsync(
        int recruiterId,
        int candidateId,
        int? companyJobRequestId,
        int? projectHiringRequestId,
        HiringWorkflowType workflowType,
        string recipientEmail,
        string companyName,
        string position,
        bool success,
        string? errorMessage,
        string? messageId)
    {
        try
        {
            var auditLog = new EmailAuditLog
            {
                RecruiterId = recruiterId,
                CandidateId = candidateId,
                CompanyJobRequestId = companyJobRequestId,
                ProjectHiringRequestId = projectHiringRequestId,
                WorkflowType = workflowType,
                RecipientEmail = recipientEmail,
                CompanyName = companyName,
                Position = position,
                Subject = $"Recruitment Opportunity: {position} at {companyName}",
                SendSuccessful = success,
                ErrorMessage = errorMessage,
                MessageId = messageId,
                SentAt = DateTime.UtcNow
            };

            _context.EmailAuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Email audit log created - Success: {Success}, Recipient: {Email}", success, recipientEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log email attempt to audit trail");
            // Don't throw - we don't want email sending to fail just because logging failed
        }
    }
}

/// <summary>
/// Response models for email endpoints.
/// </summary>
public class EmailResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? MessageId { get; set; }
    public DateTime SentAt { get; set; }
}

public class ErrorResponse
{
    public string Message { get; set; } = "";
}
