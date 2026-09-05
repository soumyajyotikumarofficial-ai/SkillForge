using SkillForge.Data;
using SkillForge.Models;
using Microsoft.EntityFrameworkCore;

namespace SkillForge.API.Services;

/// <summary>
/// Seeds company career pages registry with 30+ major tech companies.
/// Idempotent - safe to run multiple times.
/// </summary>
public class CompanyCareerSeedDataService
{
    private readonly SkillForgeDbContext _context;
    private readonly ILogger<CompanyCareerSeedDataService> _logger;

    public CompanyCareerSeedDataService(SkillForgeDbContext context, ILogger<CompanyCareerSeedDataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the company careers registry with 30+ companies.
    /// Idempotent: Only inserts companies that don't already exist.
    /// </summary>
    public async Task SeedCompanyCareersAsync()
    {
        try
        {
            var existingCompanies = await _context.CompanyCareers.Select(c => c.CompanyName).ToListAsync();
            var companiesToInsert = new List<CompanyCareer>();

            var companies = new List<(string name, string domain, string industry, string hq, bool scrapeSupported)>
            {
                // Global Tech Giants
                ("Google", "google.com", "Technology", "Mountain View, USA", true),
                ("Microsoft", "microsoft.com", "Technology", "Redmond, USA", true),
                ("Amazon", "amazon.com", "E-commerce & Cloud", "Seattle, USA", true),
                ("Meta", "meta.com", "Technology", "Menlo Park, USA", true),
                ("Apple", "apple.com", "Technology", "Cupertino, USA", true),
                ("Netflix", "netflix.com", "Entertainment", "Los Gatos, USA", true),

                // Indian Tech Giants
                ("Razorpay", "razorpay.com", "Fintech", "Bangalore, India", true),
                ("Zepto", "zepto.com", "E-commerce", "Bangalore, India", true),
                ("Swiggy", "swiggy.com", "Food Delivery", "Bangalore, India", true),
                ("Zomato", "zomato.com", "Food Delivery", "Bangalore, India", true),
                ("Flipkart", "flipkart.com", "E-commerce", "Bangalore, India", true),
                ("PhonePe", "phonepe.com", "Fintech", "Bangalore, India", true),
                ("CRED", "cred.club", "Fintech", "Bangalore, India", true),
                ("Meesho", "meesho.com", "E-commerce", "Bangalore, India", false),
                ("Freshworks", "freshworks.com", "SaaS", "Chennai, India", true),
                ("Zoho", "zoho.com", "SaaS", "Chennai, India", true),

                // Enterprise Software
                ("Infosys", "infosys.com", "IT Services", "Bangalore, India", false),
                ("Wipro", "wipro.com", "IT Services", "Bangalore, India", false),
                ("TCS", "tcs.com", "IT Services", "Mumbai, India", false),
                ("HCL", "hcl.com", "IT Services", "Noida, India", false),

                // Fintech & Payments
                ("Paytm", "paytm.com", "Fintech", "Bangalore, India", true),
                ("Groww", "groww.in", "Fintech", "Bangalore, India", true),

                // E-commerce & Marketplace
                ("Nykaa", "nykaa.com", "E-commerce", "Mumbai, India", false),
                ("Byju's", "byjus.com", "EdTech", "Bangalore, India", false),

                // Transportation & Delivery
                ("Ola", "olacabs.com", "Transportation", "Bangalore, India", false),
                ("Rapido", "rapido.bike", "Transportation", "Bangalore, India", false),
                ("Dunzo", "dunzo.com", "Delivery", "Bangalore, India", false),

                // Developer Tools & APIs
                ("Postman", "postman.com", "Developer Tools", "San Francisco, USA", true),
                ("BrowserStack", "browserstack.com", "Testing", "Bangalore, India", true),
                ("Chargebee", "chargebee.com", "SaaS", "Bangalore, India", true),

                // Additional Global Tech
                ("Stripe", "stripe.com", "Fintech", "San Francisco, USA", true),
                ("GitHub", "github.com", "Developer Tools", "San Francisco, USA", true),
                ("Figma", "figma.com", "Design", "San Francisco, USA", true),
            };

            foreach (var (name, domain, industry, hq, scrapeSupported) in companies)
            {
                // Skip if already exists
                if (existingCompanies.Contains(name))
                    continue;

                var company = new CompanyCareer
                {
                    CompanyName = name,
                    Industry = industry,
                    Headquarters = hq,
                    CareersUrl = $"https://careers.{domain}",
                    LogoUrl = $"https://logo.clearbit.com/{domain}",
                    ScrapeSupported = scrapeSupported,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                companiesToInsert.Add(company);
            }

            if (companiesToInsert.Any())
            {
                await _context.CompanyCareers.AddRangeAsync(companiesToInsert);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Seeded {companiesToInsert.Count} new company careers");
            }
            else
            {
                _logger.LogInformation("All companies already exist. No new companies added.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error seeding company careers: {ex.Message}");
            throw;
        }
    }
}
