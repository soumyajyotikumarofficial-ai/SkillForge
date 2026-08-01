using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SkillForge.API.Services;

public class ShortlistEmailModel
{
    public string CandidateEmail { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string RoleTitle { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? CompanyLogoUrl { get; set; }
}

/// <summary>Sends the branded "you've been shortlisted" email once a recruiter selects a candidate.</summary>
public interface IEmailNotificationService
{
    Task<bool> SendShortlistEmailAsync(ShortlistEmailModel model);
}

/// <summary>
/// SMTP-backed email sender. If SMTP is not configured (no host in appsettings), sending is
/// skipped defensively (logged, not thrown) so the candidate-selection workflow never fails
/// purely because outbound mail credentials are missing in a local/dev environment.
/// </summary>
public class EmailNotificationService : IEmailNotificationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(IConfiguration config, ILogger<EmailNotificationService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendShortlistEmailAsync(ShortlistEmailModel model)
    {
        if (string.IsNullOrWhiteSpace(model.CandidateEmail))
        {
            _logger.LogWarning("Cannot send shortlist email - candidate has no email on file.");
            return false;
        }

        var host = _config["Smtp:Host"];
        var fromAddress = _config["Smtp:From"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            _logger.LogWarning(
                "SMTP is not configured (Smtp:Host/Smtp:From missing). Skipping outbound email to {Email} for role '{Role}'.",
                model.CandidateEmail, model.RoleTitle);
            return false;
        }

        try
        {
            var port = int.TryParse(_config["Smtp:Port"], out var parsedPort) ? parsedPort : 587;
            var enableSsl = !bool.TryParse(_config["Smtp:EnableSsl"], out var ssl) || ssl;
            var username = _config["Smtp:Username"];
            var password = _config["Smtp:Password"];

            using var client = new SmtpClient(host, port) { EnableSsl = enableSsl };
            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress, model.CompanyName),
                Subject = $"You've been shortlisted for {model.RoleTitle} at {model.CompanyName}",
                Body = BuildHtmlBody(model),
                IsBodyHtml = true
            };
            message.To.Add(model.CandidateEmail);

            await client.SendMailAsync(message);
            _logger.LogInformation("Shortlist email sent to {Email} for role '{Role}' at '{Company}'.", model.CandidateEmail, model.RoleTitle, model.CompanyName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shortlist email to {Email}.", model.CandidateEmail);
            return false;
        }
    }

    private static string BuildHtmlBody(ShortlistEmailModel model)
    {
        var logoHtml = string.IsNullOrWhiteSpace(model.CompanyLogoUrl)
            ? ""
            : $"<img src=\"{model.CompanyLogoUrl}\" alt=\"{model.CompanyName} logo\" style=\"max-height:56px;margin-bottom:16px;\" />";

        return $@"
<div style=""font-family:Segoe UI, Arial, sans-serif; max-width:600px; margin:0 auto; border:1px solid #e5e7eb; border-radius:8px; overflow:hidden;"">
  <div style=""background:#0f172a; padding:24px; text-align:center;"">
    {logoHtml}
    <h1 style=""color:#ffffff; font-size:20px; margin:0;"">{model.CompanyName}</h1>
  </div>
  <div style=""padding:32px; color:#1f2937;"">
    <p style=""font-size:16px;"">Dear {model.CandidateName},</p>
    <p style=""font-size:16px; line-height:1.6;"">
      Congratulations! Your profile has been shortlisted for the position of
      <strong>{model.RoleTitle}</strong> at <strong>{model.CompanyName}</strong>.
      Our recruiting team was impressed by your background and would like to move forward with your application.
    </p>
    <p style=""font-size:16px; line-height:1.6;"">
      A member of the {model.CompanyName} hiring team will reach out to you shortly with next steps.
    </p>
    <p style=""font-size:16px; margin-top:32px;"">Best regards,<br/>The {model.CompanyName} Hiring Team</p>
  </div>
  <div style=""background:#f8fafc; padding:16px; text-align:center; font-size:12px; color:#94a3b8;"">
    Sent via SkillForge on behalf of {model.CompanyName}
  </div>
</div>";
    }
}
