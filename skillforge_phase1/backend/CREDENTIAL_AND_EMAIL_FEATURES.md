# SkillForge Enhancement: Credential Management & Automated Recruitment Emails

## Overview

This enhancement adds two critical features to SkillForge:

1. **Credential Management**: Securely handle user credentials with encryption and JWT tokens
2. **Automated Recruitment Emails**: Send formal recruitment emails when recruiters select candidates

---

## Feature 1: Credential Management

### Purpose
Remove all "sabuj" login credentials and implement secure credential storage using AES-256-GCM encryption combined with JWT tokens for stateless authentication.

### What This Does

- **Encrypted Storage**: User credentials encrypted at rest using AES-256-GCM
- **JWT Authentication**: Stateless session management with secure token claims
- **Audit Trail**: Track all login attempts and credential access
- **Secure Cleanup**: SQL scripts to safely remove old "sabuj" credentials

### How to Use

#### 1. Clean Up "sabuj" Credentials

Run the SQL cleanup script:

```bash
# Review what will be deleted
sqlite3 skillforge.db < REMOVE_SABUJ_CREDENTIALS.sql

# If satisfied, uncomment DELETE statements in the script and run again
```

**WARNING**: This operation is permanent. Backup your database first:
```bash
cp skillforge.db skillforge.db.backup
```

#### 2. Encryption Key Setup

Generate a 256-bit encryption key (64 hex characters):

**PowerShell**:
```powershell
$key = [System.Security.Cryptography.RNGCryptoServiceProvider]::new()
$bytes = [byte[]]::new(32)
$key.GetBytes($bytes)
[System.BitConverter]::ToString($bytes) -replace '-', ''
```

**Python**:
```python
import secrets
key = secrets.token_hex(32)
print(key)
```

Add to `appsettings.json`:
```json
{
  "Encryption": {
    "Key": "your-64-hex-character-key-here"
  }
}
```

#### 3. Register Services in Program.cs

Already done! The new services are registered:
```csharp
builder.Services.AddScoped<IEmailService, GmailSmtpService>();
builder.Services.AddScoped<ICredentialEncryptionService, CredentialEncryptionService>();
```

### Encryption Details

**Algorithm**: AES-256-GCM (Galois/Counter Mode)
- **Key Size**: 256 bits (32 bytes)
- **Nonce Size**: 96 bits (12 bytes)
- **Auth Tag**: 128 bits (16 bytes)

**Security Features**:
- Authenticated encryption (detects tampering)
- Random nonce per encryption
- HMAC authentication
- Protected against timing attacks

### API Usage

```csharp
// Inject the service
public class MyController : ControllerBase
{
    private readonly ICredentialEncryptionService _encryption;
    
    public MyController(ICredentialEncryptionService encryption)
    {
        _encryption = encryption;
    }
    
    public void Example()
    {
        // Encrypt
        string plaintext = "sensitive-data";
        string encrypted = _encryption.Encrypt(plaintext);
        
        // Decrypt
        string decrypted = _encryption.Decrypt(encrypted);
        
        // Verify
        bool matches = _encryption.VerifyEncrypted(plaintext, encrypted);
    }
}
```

---

## Feature 2: Automated Recruitment Emails

### Purpose
Send formal recruitment emails automatically when recruiters select candidates for either:
- **Normal Company Job Hiring**: Salary range, role details, team information
- **Project-Based Hiring**: Project deadline, budget, project duration

### What This Does

- **Dual Workflow Support**: Different email templates for job vs. project hiring
- **Gmail SMTP Integration**: Sends emails via Gmail SMTP (works with app passwords)
- **Email Audit Trail**: Logs all email sends with success/failure status
- **Formal Templates**: Professional HTML templates with company branding
- **Error Handling**: Graceful failure handling with detailed error logging

### Key Components

#### 1. Email Service Interface (`IEmailService.cs`)

```csharp
// Send normal company job offer
Task<EmailSendResult> SendCompanyJobOfferAsync(RecruitmentEmailTemplate template);

// Send project-based hiring email
Task<EmailSendResult> SendProjectHiringEmailAsync(RecruitmentEmailTemplate template);

// Send generic email
Task<EmailSendResult> SendEmailAsync(string toEmail, string subject, string htmlBody);

// Bulk send
Task<List<EmailSendResult>> SendBulkAsync(List<RecruitmentEmailTemplate> templates, bool isProjectHiring);
```

#### 2. Gmail SMTP Implementation (`GmailSmtpService.cs`)

Uses Gmail SMTP server (smtp.gmail.com:587) with TLS encryption.

Supports both:
- Regular Gmail accounts (with app passwords)
- Google Workspace accounts

#### 3. Email Audit Log (`EmailAuditLog` Model)

Tracks every email send:
```sql
EmailAuditLogs Table
├── Id (PK)
├── RecruiterId (FK)
├── CandidateId (FK)
├── CompanyJobRequestId (FK) -- NULL for project hiring
├── ProjectHiringRequestId (FK) -- NULL for job hiring
├── WorkflowType (CompanyJob | Project)
├── RecipientEmail
├── CompanyName
├── Position
├── SendSuccessful (bool)
├── ErrorMessage (if failed)
├── MessageId (SMTP ID)
├── SentAt (timestamp)
├── OpenedAt (optional - webhook tracking)
└── ClickedAt (optional - webhook tracking)
```

#### 4. Recruitment Email Controller (`RecruitmentEmailController.cs`)

**Endpoints**:

**Send Company Job Offer**:
```
POST /api/recruitmentemail/send-company-offer/{companyJobRequestId}/{candidateId}
Authorization: Bearer {JWT_TOKEN}
```

Response:
```json
{
  "success": true,
  "message": "Email sent successfully to candidate@example.com",
  "messageId": "<SMTP_MESSAGE_ID>",
  "sentAt": "2026-09-04T10:30:00Z"
}
```

**Send Project Offer**:
```
POST /api/recruitmentemail/send-project-offer/{projectHiringRequestId}/{candidateId}
Authorization: Bearer {JWT_TOKEN}
```

**Get Email Audit Log**:
```
GET /api/recruitmentemail/audit-log/shortlist/{shortlistId}
GET /api/recruitmentemail/audit-log/recruiter/{recruiterId}
```

### Email Templates

#### Company Job Email
Includes:
- ✅ Company name
- ✅ Position
- ✅ Location
- ✅ Salary range
- ✅ Role details/description
- ✅ Team information
- ✅ Call to action

#### Project Hiring Email
Includes:
- ✅ Company name
- ✅ Position
- ✅ Project name
- ✅ Project description
- ✅ Location
- ✅ Project deadline
- ✅ Budget/Compensation
- ✅ Call to action

### Setup Instructions

#### Step 1: Configure Gmail SMTP

**Option A: Regular Gmail Account**

1. Go to https://myaccount.google.com/security
2. Enable 2-Step Verification (if not already)
3. Go to https://myaccount.google.com/apppasswords
4. Select "Mail" and "Windows Computer"
5. Generate app password (16 characters)
6. Add to `appsettings.json`:

```json
{
  "Email": {
    "SenderEmail": "your-email@gmail.com",
    "SenderPassword": "your-16-char-app-password",
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587"
  }
}
```

**Option B: Environment Variables (Recommended)**

```bash
export SKILLFORGE_EMAIL_SENDER="your-email@gmail.com"
export SKILLFORGE_EMAIL_PASSWORD="your-app-password"
export SKILLFORGE_ENCRYPTION_KEY="your-64-hex-key"
```

#### Step 2: Apply Database Migration

```bash
cd backend
dotnet ef migrations add AddEmailAuditLogsTable
dotnet ef database update
```

This creates the `EmailAuditLogs` table automatically.

#### Step 3: Rebuild and Run

```bash
dotnet build
dotnet run
```

#### Step 4: Test Email Sending

Using curl:
```bash
curl -X POST \
  "http://localhost:5123/api/recruitmentemail/send-company-offer/1/1" \
  -H "Authorization: Bearer {YOUR_JWT_TOKEN}" \
  -H "Content-Type: application/json"
```

Using Postman:
1. Open Postman
2. New Request → POST
3. URL: `http://localhost:5123/api/recruitmentemail/send-company-offer/1/1`
4. Headers → Add `Authorization: Bearer {JWT_TOKEN}`
5. Send

### Usage in Workflow

**When a recruiter selects a candidate:**

```csharp
// In CandidateShortlistService or Controller
public async Task SelectCandidateAsync(int shortlistId, bool isProjectHiring)
{
    var shortlist = await _context.CandidateShortlists.FindAsync(shortlistId);
    shortlist.IsSelected = true;
    shortlist.EmailSentAt = DateTime.UtcNow;
    
    await _context.SaveChangesAsync();
    
    // Trigger email
    if (isProjectHiring)
    {
        await _emailService.SendProjectHiringEmailAsync(templateData);
    }
    else
    {
        await _emailService.SendCompanyJobOfferAsync(templateData);
    }
}
```

### Monitoring & Audit

Query email sends:
```sql
-- All emails by recruiter
SELECT * FROM EmailAuditLogs WHERE RecruiterId = 1 ORDER BY SentAt DESC;

-- Failed sends
SELECT * FROM EmailAuditLogs WHERE SendSuccessful = 0;

-- Emails by candidate
SELECT * FROM EmailAuditLogs WHERE CandidateId = 5 ORDER BY SentAt DESC;

-- Today's email volume
SELECT COUNT(*) as DailyEmails FROM EmailAuditLogs 
WHERE date(SentAt) = date('now');
```

### Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Authentication failed | Wrong app password | Regenerate app password at myaccount.google.com/apppasswords |
| SMTP connection timeout | Firewall blocking port 587 | Check firewall rules, allow port 587 outbound |
| Email not received | Spam folder | Add SkillForge email to contacts/safe sender list |
| Logs not appearing | Migration not applied | Run `dotnet ef database update` |
| Encryption key error | Invalid key length | Key must be exactly 64 hex characters (32 bytes) |

---

## Security Considerations

### 1. Password Security
- ✅ Gmail app passwords are safer than regular passwords
- ✅ Each app password can be revoked independently
- ✅ 2-Step Verification required
- ❌ Never use your main Gmail password in code

### 2. Encryption Key Management
- ✅ Keys should be rotated periodically (annually)
- ✅ Store in secret management system (Azure Key Vault, AWS Secrets Manager)
- ✅ Different keys per environment (dev/staging/production)
- ❌ Never commit keys to version control

### 3. Email Audit Log Privacy
- ✅ Contains email addresses and candidate info
- ✅ Implement proper access controls (authentication required)
- ✅ Log viewing should be restricted to recruiters and admins
- ✅ Implement data retention policies (e.g., delete after 90 days)

### 4. SMTP Connection
- ✅ Uses TLS encryption (STARTTLS on port 587)
- ✅ Validates certificate by default
- ✅ No plaintext password transmission
- ✅ Timeout set to 30 seconds to prevent hanging

---

## Files Added/Modified

### New Files
- `Services/IEmailService.cs` - Email service interface
- `Services/GmailSmtpService.cs` - Gmail SMTP implementation (450+ lines)
- `Services/CredentialEncryptionService.cs` - AES-256-GCM encryption (200+ lines)
- `Controllers/RecruitmentEmailController.cs` - Email API endpoints (300+ lines)
- `Migrations/20260904140000_AddEmailAuditLogsTable.cs` - Database migration
- `Migrations/20260904140000_AddEmailAuditLogsTable.Designer.cs` - Migration snapshot
- `EMAIL_CONFIGURATION_GUIDE.txt` - Setup instructions
- `REMOVE_SABUJ_CREDENTIALS.sql` - Credential cleanup script

### Modified Files
- `Models/Models.cs` - Added `EmailAuditLog` class
- `Data/SkillForgeDbContext.cs` - Added `EmailAuditLogs` DbSet
- `appsettings.json` - Added Email and Encryption config sections
- `Program.cs` - Registered `IEmailService` and `ICredentialEncryptionService`

---

## Best Practices

### 1. Email Sending
```csharp
// ✅ Good: Async email sending
var result = await _emailService.SendCompanyJobOfferAsync(template);
if (!result.Success)
{
    _logger.LogError("Failed to send email: {Error}", result.ErrorMessage);
}

// ❌ Bad: Blocking email send
var result = _emailService.SendCompanyJobOfferAsync(template).Result;  // Deadlock risk
```

### 2. Credential Handling
```csharp
// ✅ Good: Use encryption for sensitive data
string encrypted = _encryption.Encrypt(sensitiveData);
await _repository.SaveAsync(encrypted);

// ❌ Bad: Storing plaintext
await _repository.SaveAsync(sensitiveData);  // Security risk
```

### 3. Error Handling
```csharp
// ✅ Good: Log errors but don't expose details to client
try 
{
    await SendEmailAsync(...);
}
catch (SmtpException ex)
{
    _logger.LogError(ex, "SMTP error");
    return new { success = false, message = "Email send failed" };  // Generic message
}

// ❌ Bad: Exposing SMTP details
catch (SmtpException ex)
{
    return new { success = false, message = ex.Message };  // Exposes internal details
}
```

---

## Future Enhancements

1. **Email Templates**
   - Liquid template engine for dynamic templates
   - Template versioning and A/B testing
   - Internationalization (i18n) support

2. **Email Tracking**
   - Webhook integration for open/click tracking
   - Email performance analytics
   - Engagement scoring

3. **Bulk Operations**
   - Queue-based email sending (Hangfire)
   - Rate limiting to avoid API throttling
   - Batch retry logic for failures

4. **Alternative Providers**
   - SendGrid integration
   - Mailgun support
   - Amazon SES integration

5. **Compliance**
   - GDPR unsubscribe links
   - DKIM/SPF/DMARC configuration
   - Email audit compliance logging

---

## Support

For configuration help, see [EMAIL_CONFIGURATION_GUIDE.txt](./EMAIL_CONFIGURATION_GUIDE.txt)

For credential cleanup, see [REMOVE_SABUJ_CREDENTIALS.sql](./REMOVE_SABUJ_CREDENTIALS.sql)

---

## Changelog

**Version 1.0 (September 4, 2026)**
- ✅ Initial implementation of encrypted credential storage
- ✅ Gmail SMTP email service
- ✅ Dual email templates (job vs. project)
- ✅ Email audit logging
- ✅ Cleanup scripts for old credentials
- ✅ Comprehensive documentation
