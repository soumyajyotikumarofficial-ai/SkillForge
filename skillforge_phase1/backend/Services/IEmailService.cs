using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SkillForge.Services;

/// <summary>
/// Email template model for both project and normal hiring workflows.
/// </summary>
public class RecruitmentEmailTemplate
{
    public string Recipient { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Position { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string Location { get; set; } = "";
    
    // For normal hiring
    public string? SalaryRange { get; set; }
    public string? RoleDetails { get; set; }
    public string? TeamInfo { get; set; }
    
    // For project hiring
    public string? ProjectName { get; set; }
    public string? ProjectDescription { get; set; }
    public DateTime? ProjectDeadline { get; set; }
    public string? Budget { get; set; }
    
    // Common
    public string? AdditionalNotes { get; set; }
    public DateTime? StartDate { get; set; }
}

/// <summary>
/// Email delivery result tracking.
/// </summary>
public class EmailSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Email service interface for sending recruitment notifications.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a normal company job offer email.
    /// </summary>
    Task<EmailSendResult> SendCompanyJobOfferAsync(RecruitmentEmailTemplate template);

    /// <summary>
    /// Sends a project-based hiring email.
    /// </summary>
    Task<EmailSendResult> SendProjectHiringEmailAsync(RecruitmentEmailTemplate template);

    /// <summary>
    /// Sends a generic email.
    /// </summary>
    Task<EmailSendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextBody = null);

    /// <summary>
    /// Sends bulk recruitment emails.
    /// </summary>
    Task<List<EmailSendResult>> SendBulkAsync(List<RecruitmentEmailTemplate> templates, bool isProjectHiring);
}
