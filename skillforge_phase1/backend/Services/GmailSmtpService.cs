using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SkillForge.Services;

/// <summary>
/// Gmail SMTP implementation of email service for SkillForge recruitment notifications.
/// Sends formal emails for both normal company hiring and project-based hiring workflows.
/// </summary>
public class GmailSmtpService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<GmailSmtpService> _logger;
    private readonly SmtpClient _smtpClient;
    private readonly string _senderEmail;
    private readonly string _senderName = "SkillForge Recruitment";

    public GmailSmtpService(IConfiguration config, ILogger<GmailSmtpService> logger)
    {
        _config = config;
        _logger = logger;

        _senderEmail = _config["Email:SenderEmail"] 
            ?? throw new InvalidOperationException("Email:SenderEmail not configured");
        var senderPassword = _config["Email:SenderPassword"]
            ?? throw new InvalidOperationException("Email:SenderPassword not configured");

        _smtpClient = new SmtpClient
        {
            Host = _config["Email:SmtpHost"] ?? "smtp.gmail.com",
            Port = int.Parse(_config["Email:SmtpPort"] ?? "587"),
            EnableSsl = true,
            Credentials = new NetworkCredential(_senderEmail, senderPassword),
            Timeout = 30000 // 30 seconds
        };

        _logger.LogInformation("Gmail SMTP service initialized for {Email}", _senderEmail);
    }

    /// <summary>
    /// Sends a normal company job offer email with salary range and role details.
    /// </summary>
    public async Task<EmailSendResult> SendCompanyJobOfferAsync(RecruitmentEmailTemplate template)
    {
        try
        {
            var subject = $"Exciting Opportunity: {template.Position} at {template.CompanyName}";
            var htmlBody = BuildCompanyJobHtmlTemplate(template);

            return await SendEmailAsync(template.Recipient, subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send company job offer email to {Email}", template.Recipient);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Sends a project-based hiring email with project details and timeline.
    /// </summary>
    public async Task<EmailSendResult> SendProjectHiringEmailAsync(RecruitmentEmailTemplate template)
    {
        try
        {
            var subject = $"Project Opportunity: {template.Position} - {template.ProjectName}";
            var htmlBody = BuildProjectHiringHtmlTemplate(template);

            return await SendEmailAsync(template.Recipient, subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send project hiring email to {Email}", template.Recipient);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Sends a generic email via Gmail SMTP.
    /// </summary>
    public async Task<EmailSendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextBody = null)
    {
        try
        {
            if (string.IsNullOrEmpty(toEmail))
                throw new ArgumentException("Recipient email cannot be empty", nameof(toEmail));

            using (var mailMessage = new MailMessage())
            {
                mailMessage.From = new MailAddress(_senderEmail, _senderName);
                mailMessage.To.Add(toEmail);
                mailMessage.Subject = subject;
                mailMessage.IsBodyHtml = true;
                mailMessage.Body = htmlBody;

                // Add plain text alternative for clients that don't support HTML
                if (!string.IsNullOrEmpty(plainTextBody))
                {
                    var plainView = AlternateView.CreateAlternateViewFromString(
                        plainTextBody, null, "text/plain");
                    mailMessage.AlternateViews.Add(plainView);
                }

                await _smtpClient.SendMailAsync(mailMessage);

                _logger.LogInformation("Email sent successfully to {Email} with subject: {Subject}", toEmail, subject);

                return new EmailSendResult
                {
                    Success = true,
                    MessageId = mailMessage.Headers?["Message-ID"],
                    SentAt = DateTime.UtcNow
                };
            }
        }
        catch (SmtpFailedRecipientException ex)
        {
            _logger.LogWarning(ex, "Recipient rejected by SMTP server: {Email}", toEmail);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = $"Recipient rejected: {ex.Message}"
            };
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP error when sending to {Email}", toEmail);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = $"SMTP error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending email to {Email}", toEmail);
            return new EmailSendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Sends bulk recruitment emails.
    /// </summary>
    public async Task<List<EmailSendResult>> SendBulkAsync(List<RecruitmentEmailTemplate> templates, bool isProjectHiring)
    {
        var results = new List<EmailSendResult>();

        foreach (var template in templates)
        {
            try
            {
                EmailSendResult result;
                if (isProjectHiring)
                    result = await SendProjectHiringEmailAsync(template);
                else
                    result = await SendCompanyJobOfferAsync(template);

                results.Add(result);

                // Add delay to avoid rate limiting
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending bulk email to {Email}", template.Recipient);
                results.Add(new EmailSendResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Builds formal HTML email template for company job offers.
    /// Includes: company name, position, location, salary range, role details.
    /// </summary>
    private string BuildCompanyJobHtmlTemplate(RecruitmentEmailTemplate template)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; color: #333; line-height: 1.6; }");
        sb.AppendLine(".container { max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9f9f9; }");
        sb.AppendLine(".header { background-color: #2c3e50; color: white; padding: 20px; border-radius: 5px 5px 0 0; }");
        sb.AppendLine(".content { background-color: white; padding: 20px; border-radius: 0 0 5px 5px; }");
        sb.AppendLine(".section { margin-bottom: 20px; }");
        sb.AppendLine(".section-title { font-weight: bold; color: #2c3e50; margin-bottom: 10px; }");
        sb.AppendLine(".footer { margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }");
        sb.AppendLine(".button { display: inline-block; padding: 10px 20px; background-color: #3498db; color: white; text-decoration: none; border-radius: 5px; margin-top: 10px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine($"<div class=\"header\"><h1>Exciting Career Opportunity at {EscapeHtml(template.CompanyName)}</h1></div>");
        
        sb.AppendLine("<div class=\"content\">");
        sb.AppendLine($"<p>Dear {EscapeHtml(template.CandidateName)},</p>");
        sb.AppendLine("<p>We are pleased to inform you about an exciting opportunity that matches your professional profile and career goals.</p>");
        
        // Position Details Section
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("<div class=\"section-title\">Position Details</div>");
        sb.AppendLine($"<p><strong>Position:</strong> {EscapeHtml(template.Position)}</p>");
        sb.AppendLine($"<p><strong>Location:</strong> {EscapeHtml(template.Location)}</p>");
        
        if (!string.IsNullOrEmpty(template.SalaryRange))
            sb.AppendLine($"<p><strong>Salary Range:</strong> {EscapeHtml(template.SalaryRange)}</p>");
        
        sb.AppendLine("</div>");
        
        // Role Details Section
        if (!string.IsNullOrEmpty(template.RoleDetails))
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("<div class=\"section-title\">Role Overview</div>");
            sb.AppendLine($"<p>{EscapeHtml(template.RoleDetails)}</p>");
            sb.AppendLine("</div>");
        }
        
        // Team Information Section
        if (!string.IsNullOrEmpty(template.TeamInfo))
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("<div class=\"section-title\">Your Team</div>");
            sb.AppendLine($"<p>{EscapeHtml(template.TeamInfo)}</p>");
            sb.AppendLine("</div>");
        }
        
        // Additional Notes
        if (!string.IsNullOrEmpty(template.AdditionalNotes))
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine($"<p>{EscapeHtml(template.AdditionalNotes)}</p>");
            sb.AppendLine("</div>");
        }
        
        // Next Steps
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("<p>We would love to explore this opportunity with you. Our recruitment team will reach out shortly to discuss the details and next steps.</p>");
        sb.AppendLine("</div>");
        
        // Footer
        sb.AppendLine("<div class=\"footer\">");
        sb.AppendLine("<p>Best regards,<br>");
        sb.AppendLine($"<strong>SkillForge Recruitment Team</strong><br>");
        sb.AppendLine("On behalf of <strong>{template.CompanyName}</strong></p>");
        sb.AppendLine("<p>This is an automated message. Please do not reply to this email. For inquiries, contact our recruitment team through our portal.</p>");
        sb.AppendLine("</div>");
        
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        
        return sb.ToString();
    }

    /// <summary>
    /// Builds formal HTML email template for project-based hiring.
    /// Includes: company name, position, project name, project deadline, budget, location.
    /// </summary>
    private string BuildProjectHiringHtmlTemplate(RecruitmentEmailTemplate template)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; color: #333; line-height: 1.6; }");
        sb.AppendLine(".container { max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f9f9f9; }");
        sb.AppendLine(".header { background-color: #27ae60; color: white; padding: 20px; border-radius: 5px 5px 0 0; }");
        sb.AppendLine(".content { background-color: white; padding: 20px; border-radius: 0 0 5px 5px; }");
        sb.AppendLine(".section { margin-bottom: 20px; }");
        sb.AppendLine(".section-title { font-weight: bold; color: #27ae60; margin-bottom: 10px; font-size: 16px; }");
        sb.AppendLine(".timeline { background-color: #ecf0f1; padding: 10px; border-left: 4px solid #27ae60; }");
        sb.AppendLine(".footer { margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }");
        sb.AppendLine(".badge { display: inline-block; background-color: #e8f8f5; border: 1px solid #27ae60; color: #27ae60; padding: 5px 10px; border-radius: 3px; font-size: 12px; margin-right: 5px; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine($"<div class=\"header\"><h1>Project Opportunity: {EscapeHtml(template.Position)}</h1></div>");
        
        sb.AppendLine("<div class=\"content\">");
        sb.AppendLine($"<p>Dear {EscapeHtml(template.CandidateName)},</p>");
        sb.AppendLine("<p>We are excited to present a project-based engagement opportunity that aligns perfectly with your expertise and professional goals.</p>");
        
        // Project Information
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("<div class=\"section-title\">Project Overview</div>");
        sb.AppendLine($"<p><strong>Company:</strong> {EscapeHtml(template.CompanyName)}</p>");
        sb.AppendLine($"<p><strong>Project:</strong> {EscapeHtml(template.ProjectName ?? "N/A")}</p>");
        sb.AppendLine($"<p><strong>Position:</strong> {EscapeHtml(template.Position)}</p>");
        sb.AppendLine($"<p><strong>Location:</strong> {EscapeHtml(template.Location)}</p>");
        sb.AppendLine("</div>");
        
        // Project Description
        if (!string.IsNullOrEmpty(template.ProjectDescription))
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("<div class=\"section-title\">Project Description</div>");
            sb.AppendLine($"<p>{EscapeHtml(template.ProjectDescription)}</p>");
            sb.AppendLine("</div>");
        }
        
        // Timeline & Budget
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("<div class=\"section-title\">Project Timeline & Budget</div>");
        sb.AppendLine("<div class=\"timeline\">");
        
        if (template.ProjectDeadline.HasValue)
            sb.AppendLine($"<p><strong>Project Deadline:</strong> {template.ProjectDeadline:MMMM d, yyyy}</p>");
        
        if (!string.IsNullOrEmpty(template.Budget))
            sb.AppendLine($"<p><strong>Budget/Compensation:</strong> {EscapeHtml(template.Budget)}</p>");
        
        if (template.StartDate.HasValue)
            sb.AppendLine($"<p><strong>Expected Start Date:</strong> {template.StartDate:MMMM d, yyyy}</p>");
        
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        
        // Additional Notes
        if (!string.IsNullOrEmpty(template.AdditionalNotes))
        {
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine("<div class=\"section-title\">Additional Information</div>");
            sb.AppendLine($"<p>{EscapeHtml(template.AdditionalNotes)}</p>");
            sb.AppendLine("</div>");
        }
        
        // Next Steps
        sb.AppendLine("<div class=\"section\">");
        sb.AppendLine("<p>We believe this is an excellent match and would like to discuss the engagement details with you. Our team will be in touch shortly to schedule a conversation.</p>");
        sb.AppendLine("</div>");
        
        // Footer
        sb.AppendLine("<div class=\"footer\">");
        sb.AppendLine("<p>Best regards,<br>");
        sb.AppendLine($"<strong>SkillForge Recruitment Team</strong><br>");
        sb.AppendLine("On behalf of <strong>{template.CompanyName}</strong></p>");
        sb.AppendLine("<p>This is an automated message from SkillForge. Please do not reply to this email. For inquiries, contact our recruitment team through our portal.</p>");
        sb.AppendLine("</div>");
        
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        
        return sb.ToString();
    }

    /// <summary>
    /// Escapes HTML special characters to prevent injection.
    /// </summary>
    private string EscapeHtml(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }
}
