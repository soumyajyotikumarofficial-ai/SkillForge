using SkillForge.Data;
using SkillForge.Models;
using Microsoft.EntityFrameworkCore;

namespace SkillForge.API.Services;

/// <summary>
/// Idempotent seed data service for populating the Candidates table with rich, diverse data.
/// Safe to run multiple times without creating duplicates.
/// </summary>
public class CandidateSeedDataService
{
    private readonly SkillForgeDbContext _context;
    private readonly ILogger<CandidateSeedDataService> _logger;

    public CandidateSeedDataService(SkillForgeDbContext context, ILogger<CandidateSeedDataService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the database with 40+ diverse candidates across different roles and skill sets.
    /// Idempotent: Only inserts candidates that don't already exist (checked by email).
    /// </summary>
    public async Task SeedCandidatesAsync()
    {
        try
        {
            var existingEmails = await _context.Candidates.Select(c => c.Email).ToListAsync();
            var candidatesToInsert = new List<Candidate>();

            // ===== FRONTEND ENGINEERS (8 candidates) =====
            var frontendCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Aisha Patel", "aisha.patel@skillforge.dev", "React|TypeScript|Tailwind|JavaScript|Redux", 5, "Bangalore, India", 1200000, "aisha-portfolio.dev", "github.com/aisha-patel", "linkedin.com/in/aisha-patel"),
                ("Rohan Kumar", "rohan.kumar@skillforge.dev", "Vue|TypeScript|Webpack|CSS|Responsive Design", 3, "Mumbai, India", 850000, "rohan-dev.com", "github.com/rohan-kumar", "linkedin.com/in/rohan-kumar"),
                ("Emma Wilson", "emma.wilson@skillforge.dev", "Angular|TypeScript|RxJS|Material UI|Testing", 7, "London, UK", 68000, "emma-wilson.dev", "github.com/emma-w", "linkedin.com/in/emma-wilson"),
                ("Raj Sharma", "raj.sharma@skillforge.dev", "React|Next.js|TypeScript|Tailwind|Figma", 4, "Delhi, India", 950000, "raj-dev.io", "github.com/raj-sharma", "linkedin.com/in/raj-sharma"),
                ("Sarah Chen", "sarah.chen@skillforge.dev", "React|JavaScript|CSS Modules|Accessibility|Jest", 6, "Berlin, Germany", 58000, "sarah-chen.dev", "github.com/sarah-chen", "linkedin.com/in/sarah-chen"),
                ("Vikram Singh", "vikram.singh@skillforge.dev", "Svelte|JavaScript|Three.js|WebGL|Animation", 2, "Hyderabad, India", 650000, "vikram-dev.com", "github.com/vikram-singh", "linkedin.com/in/vikram-singh"),
                ("Sophie Martin", "sophie.martin@skillforge.dev", "React|TypeScript|GraphQL|Storybook|Accessibility", 8, "Berlin, Germany", 72000, "sophie-martin.dev", "github.com/sophie-m", "linkedin.com/in/sophie-martin"),
                ("Arjun Desai", "arjun.desai@skillforge.dev", "React|Next.js|Tailwind|Node.js|PostgreSQL", 6, "Chennai, India", 1150000, "arjun-dev.io", "github.com/arjun-desai", "linkedin.com/in/arjun-desai"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, frontendCandidates, "Frontend Engineer", 8);

            // ===== BACKEND ENGINEERS (8 candidates) =====
            var backendCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Priya Verma", "priya.verma@skillforge.dev", "Node.js|Express|PostgreSQL|Redis|Docker", 6, "Pune, India", 1300000, "priya-api.dev", "github.com/priya-verma", "linkedin.com/in/priya-verma"),
                ("Marcus Johnson", "marcus.johnson@skillforge.dev", "Python|Django|FastAPI|PostgreSQL|Celery", 8, "Toronto, Canada", 95000, "marcus-dev.io", "github.com/marcus-j", "linkedin.com/in/marcus-johnson"),
                ("Deepak Nair", "deepak.nair@skillforge.dev", "Java|Spring Boot|Hibernate|MySQL|AWS", 10, "Bangalore, India", 1600000, "deepak-java.dev", "github.com/deepak-nair", "linkedin.com/in/deepak-nair"),
                ("Lena Müller", "lena.muller@skillforge.dev", "Go|Microservices|gRPC|Kubernetes|Docker", 7, "Amsterdam, Netherlands", 78000, "lena-go.dev", "github.com/lena-muller", "linkedin.com/in/lena-muller"),
                ("Anand Pillai", "anand.pillai@skillforge.dev", "Python|FastAPI|SQLAlchemy|MongoDB|GraphQL", 5, "Remote, India", 1100000, "anand-api.dev", "github.com/anand-pillai", "linkedin.com/in/anand-pillai"),
                ("Ruby Desai", "ruby.desai@skillforge.dev", "Ruby on Rails|PostgreSQL|Redis|Sidekiq|REST", 9, "Dubai, UAE", 120000, "ruby-rails.dev", "github.com/ruby-desai", "linkedin.com/in/ruby-desai"),
                ("Chen Wei", "chen.wei@skillforge.dev", "Node.js|TypeScript|GraphQL|MongoDB|AWS", 4, "Singapore, Singapore", 98000, "chen-api.dev", "github.com/chen-wei", "linkedin.com/in/chen-wei"),
                ("Natalia Kovalenko", "natalia.k@skillforge.dev", "Python|Django|PostgreSQL|Elasticsearch|Docker", 12, "Remote, Ukraine", 75000, "natalia-dev.io", "github.com/natalia-k", "linkedin.com/in/natalia-kovalenko"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, backendCandidates, "Backend Engineer", 5);

            // ===== ML / AI ENGINEERS (6 candidates) =====
            var mlCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Sophia Rodriguez", "sophia.rodriguez@skillforge.dev", "Python|TensorFlow|PyTorch|Scikit-learn|Keras", 7, "San Francisco, USA", 165000, "sophia-ai.dev", "github.com/sophia-r", "linkedin.com/in/sophia-rodriguez"),
                ("Ravi Shankar", "ravi.shankar@skillforge.dev", "Python|PyTorch|Hugging Face|LangChain|CUDA", 6, "Bangalore, India", 1500000, "ravi-ml.dev", "github.com/ravi-shankar", "linkedin.com/in/ravi-shankar"),
                ("Isabella Chen", "isabella.chen@skillforge.dev", "Python|TensorFlow|OpenCV|Computer Vision|YOLO", 5, "Remote, Singapore", 110000, "isabella-cv.dev", "github.com/isabella-chen", "linkedin.com/in/isabella-chen"),
                ("Dimitri Petrov", "dimitri.petrov@skillforge.dev", "Python|MLflow|Airflow|Pandas|NumPy|SQL", 8, "London, UK", 125000, "dimitri-ml.dev", "github.com/dimitri-p", "linkedin.com/in/dimitri-petrov"),
                ("Swati Bhattacharya", "swati.bhat@skillforge.dev", "Python|TensorFlow|Keras|NLP|Transformers", 6, "Hyderabad, India", 1400000, "swati-nlp.dev", "github.com/swati-bhat", "linkedin.com/in/swati-bhattacharya"),
                ("Lucas Santos", "lucas.santos@skillforge.dev", "Python|PyTorch|MLOps|Docker|Kubernetes", 9, "Remote, Brazil", 98000, "lucas-mlops.dev", "github.com/lucas-santos", "linkedin.com/in/lucas-santos"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, mlCandidates, "ML/AI Engineer", 6);

            // ===== DEVOPS / CLOUD ENGINEERS (5 candidates) =====
            var devopsCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("James O'Connor", "james.oconnor@skillforge.dev", "Docker|Kubernetes|AWS|Terraform|Jenkins", 9, "Remote, USA", 155000, "james-devops.dev", "github.com/james-o", "linkedin.com/in/james-oconnor"),
                ("Prashant Reddy", "prashant.reddy@skillforge.dev", "Docker|Kubernetes|GCP|Terraform|GitHub Actions", 7, "Pune, India", 1350000, "prashant-devops.dev", "github.com/prashant-r", "linkedin.com/in/prashant-reddy"),
                ("Anna Bergström", "anna.bergstrom@skillforge.dev", "Kubernetes|Docker|Azure|Helm|Ansible", 8, "Amsterdam, Netherlands", 82000, "anna-cloud.dev", "github.com/anna-b", "linkedin.com/in/anna-bergstrom"),
                ("Vikas Mahajan", "vikas.mahajan@skillforge.dev", "Docker|Kubernetes|AWS|CI/CD|Linux", 6, "Bangalore, India", 1200000, "vikas-infra.dev", "github.com/vikas-mahajan", "linkedin.com/in/vikas-mahajan"),
                ("Konstantin Volkov", "konstantin.v@skillforge.dev", "Kubernetes|Docker|AWS|Terraform|Prometheus", 11, "Toronto, Canada", 148000, "konstantin-ops.dev", "github.com/konstantin-v", "linkedin.com/in/konstantin-volkov"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, devopsCandidates, "DevOps/Cloud Engineer", 5);

            // ===== MOBILE ENGINEERS (5 candidates) =====
            var mobileCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Neha Garg", "neha.garg@skillforge.dev", "Flutter|Dart|Firebase|REST|UI/UX", 5, "Mumbai, India", 1050000, "neha-mobile.dev", "github.com/neha-garg", "linkedin.com/in/neha-garg"),
                ("Miguel Angel", "miguel.angel@skillforge.dev", "React Native|JavaScript|Native Modules|Firebase", 6, "Berlin, Germany", 68000, "miguel-rn.dev", "github.com/miguel-a", "linkedin.com/in/miguel-angel"),
                ("Ananya Singh", "ananya.singh@skillforge.dev", "Swift|iOS|Objective-C|ARKit|Core Data", 7, "Bangalore, India", 1280000, "ananya-ios.dev", "github.com/ananya-singh", "linkedin.com/in/ananya-singh"),
                ("Arjun Mathew", "arjun.mathew@skillforge.dev", "Kotlin|Android|Jetpack Compose|Firebase|REST", 4, "Remote, India", 900000, "arjun-android.dev", "github.com/arjun-mathew", "linkedin.com/in/arjun-mathew"),
                ("Sofia Rossi", "sofia.rossi@skillforge.dev", "Flutter|Dart|Bloc|GetX|Firebase", 3, "Dubai, UAE", 110000, "sofia-flutter.dev", "github.com/sofia-rossi", "linkedin.com/in/sofia-rossi"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, mobileCandidates, "Mobile Engineer", 5);

            // ===== QA ENGINEERS (4 candidates) =====
            var qaCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Kavya Rao", "kavya.rao@skillforge.dev", "Selenium|Cypress|Jest|Playwright|Test Automation", 6, "Chennai, India", 800000, "kavya-qa.dev", "github.com/kavya-rao", "linkedin.com/in/kavya-rao"),
                ("Michael Thompson", "michael.thompson@skillforge.dev", "Cypress|Postman|JMeter|TestRail|Performance Testing", 7, "Remote, UK", 72000, "michael-qa.dev", "github.com/michael-t", "linkedin.com/in/michael-thompson"),
                ("Shreya Patel", "shreya.patel@skillforge.dev", "Selenium|Appium|Python|BDD|CI/CD", 4, "Bangalore, India", 700000, "shreya-qa.dev", "github.com/shreya-patel", "linkedin.com/in/shreya-patel"),
                ("Dmitri Sokolov", "dmitri.sokolov@skillforge.dev", "Cypress|Playwright|JavaScript|API Testing|Docker", 7, "Remote, Russia", 65000, "dmitri-qa.dev", "github.com/dmitri-s", "linkedin.com/in/dmitri-sokolov"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, qaCandidates, "QA Engineer", 5);

            // ===== DATA ENGINEERS (4 candidates) =====
            var dataCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Aman Saxena", "aman.saxena@skillforge.dev", "Kafka|Apache Spark|Airflow|PostgreSQL|Scala", 8, "Bangalore, India", 1450000, "aman-data.dev", "github.com/aman-saxena", "linkedin.com/in/aman-saxena"),
                ("Elena Kowalski", "elena.kowalski@skillforge.dev", "Spark|Kafka|dbt|Snowflake|Python", 7, "Remote, Poland", 92000, "elena-data.dev", "github.com/elena-k", "linkedin.com/in/elena-kowalski"),
                ("Siddharth Verma", "siddharth.verma@skillforge.dev", "Apache Spark|Scala|Hadoop|HBase|Hive", 10, "Remote, India", 1550000, "siddharth-bigdata.dev", "github.com/siddharth-v", "linkedin.com/in/siddharth-verma"),
                ("Lisa Wong", "lisa.wong@skillforge.dev", "BigQuery|dbt|Python|Airflow|Dataflow", 6, "Singapore, Singapore", 105000, "lisa-data.dev", "github.com/lisa-wong", "linkedin.com/in/lisa-wong"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, dataCandidates, "Data Engineer", 6);

            // ===== FULL STACK ENGINEERS (5 candidates) =====
            var fullstackCandidates = new List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)>
            {
                ("Kavya Mishra", "kavya.mishra@skillforge.dev", "React|Node.js|PostgreSQL|Docker|AWS|TypeScript", 6, "Remote, India", 1350000, "kavya-fullstack.dev", "github.com/kavya-mishra", "linkedin.com/in/kavya-mishra"),
                ("Gabriel Silva", "gabriel.silva@skillforge.dev", "React|Node.js|MongoDB|AWS|GraphQL", 5, "Toronto, Canada", 105000, "gabriel-fullstack.dev", "github.com/gabriel-s", "linkedin.com/in/gabriel-silva"),
                ("Priya Nair", "priya.nair@skillforge.dev", "React|Express|PostgreSQL|Docker|Kubernetes", 7, "Bangalore, India", 1400000, "priya-stack.dev", "github.com/priya-nair", "linkedin.com/in/priya-nair"),
                ("Johannes Beck", "johannes.beck@skillforge.dev", "Vue|Node.js|MySQL|Docker|Digital Ocean", 4, "Berlin, Germany", 62000, "johannes-stack.dev", "github.com/johannes-b", "linkedin.com/in/johannes-beck"),
                ("Amar Patel", "amar.patel@skillforge.dev", "React|Node.js|MongoDB|AWS|Microservices", 8, "Remote, India", 1450000, "amar-fullstack.dev", "github.com/amar-patel", "linkedin.com/in/amar-patel"),
            };

            AddCandidatesToList(candidatesToInsert, existingEmails, fullstackCandidates, "Full Stack Engineer", 8);

            // ===== ADD ALL CANDIDATES TO DATABASE =====
            if (candidatesToInsert.Any())
            {
                await _context.Candidates.AddRangeAsync(candidatesToInsert);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Seeded {candidatesToInsert.Count} new candidates");
            }
            else
            {
                _logger.LogInformation("All candidates already exist. No new candidates added.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error seeding candidates: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Helper method to add candidates to the list if they don't already exist.
    /// </summary>
    private void AddCandidatesToList(
        List<Candidate> candidatesToInsert,
        List<string> existingEmails,
        List<(string fullName, string email, string skills, int yoe, string location, int salary, string portfolio, string github, string linkedin)> candidateData,
        string primaryRole,
        int minSalaryMultiplier)
    {
        foreach (var (fullName, email, skills, yoe, location, baseSalary, portfolio, github, linkedin) in candidateData)
        {
            // Skip if already exists
            if (existingEmails.Contains(email))
                continue;

            var seniority = DetermineSeniority(yoe);
            var availability = GetRandomAvailability();
            var currency = DetermineCurrency(location);
            var adjustedSalary = AdjustSalaryByCurrency(baseSalary, currency);

            var candidate = new Candidate
            {
                FullName = fullName,
                Name = fullName,
                Email = email,
                Phone = GeneratePhoneNumber(location),
                Location = location,
                PrimaryRole = primaryRole,
                Seniority = seniority,
                Availability = availability,
                SkillsList = skills,
                YearsOfExperienceInt = yoe,
                YearsOfExperience = yoe.ToString(),
                ExpectedSalary = adjustedSalary,
                Currency = currency,
                PortfolioUrl = portfolio,
                GitHubUrl = github,
                LinkedInUrl = linkedin,
                Bio = GenerateBio(fullName, primaryRole, yoe, location),
                PreferredWorkMode = location.Contains("Remote") ? "Remote" : "Hybrid",
                HighestQualification = GetRandomQualification(),
                ResumeScore = Random.Shared.Next(75, 100),
                Summary = $"Experienced {primaryRole} with {yoe} years in the industry",
                CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 90)),
                UpdatedAt = DateTime.UtcNow,
            };

            candidatesToInsert.Add(candidate);
        }
    }

    /// <summary>
    /// Determines seniority level based on years of experience.
    /// </summary>
    private string DetermineSeniority(int yoe) =>
        yoe switch
        {
            <= 2 => "Junior",
            <= 5 => "Mid",
            <= 9 => "Senior",
            _ => "Lead"
        };

    /// <summary>
    /// Randomly assigns availability for candidates.
    /// </summary>
    private string GetRandomAvailability()
    {
        var random = Random.Shared.Next(0, 100);
        return random switch
        {
            < 20 => "Immediate",
            < 50 => "2 Weeks",
            < 75 => "1 Month",
            _ => "Not Available"
        };
    }

    /// <summary>
    /// Determines currency based on location.
    /// </summary>
    private string DetermineCurrency(string location) =>
        location switch
        {
            var l when l.Contains("India") || l.Contains("Remote, India") => "INR",
            var l when l.Contains("USA") || l.Contains("San Francisco") || l.Contains("Toronto") || l.Contains("Canada") || l.Contains("Brazil") => "USD",
            var l when l.Contains("UK") || l.Contains("London") => "GBP",
            var l when l.Contains("UAE") || l.Contains("Dubai") => "AED",
            var l when l.Contains("Singapore") => "SGD",
            _ => "EUR"
        };

    /// <summary>
    /// Adjusts salary based on currency (converts from base INR if needed).
    /// </summary>
    private int AdjustSalaryByCurrency(int salary, string currency) =>
        currency switch
        {
            "INR" => salary,
            "USD" => salary / 83, // Approximate INR to USD
            "GBP" => salary / 104, // Approximate INR to GBP
            "EUR" => salary / 91, // Approximate INR to EUR
            "AED" => salary / 23, // Approximate INR to AED
            "SGD" => salary / 62, // Approximate INR to SGD
            _ => salary
        };

    /// <summary>
    /// Generates a realistic phone number based on location.
    /// </summary>
    private string GeneratePhoneNumber(string location)
    {
        var countryCode = location switch
        {
            var l when l.Contains("India") => "+91",
            var l when l.Contains("USA") => "+1",
            var l when l.Contains("UK") => "+44",
            var l when l.Contains("Canada") => "+1",
            var l when l.Contains("Germany") => "+49",
            var l when l.Contains("Netherlands") => "+31",
            var l when l.Contains("UAE") => "+971",
            var l when l.Contains("Singapore") => "+65",
            _ => "+44"
        };

        var randomNumber = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();
        return $"{countryCode}{randomNumber[..10]}";
    }

    /// <summary>
    /// Generates a realistic bio for the candidate.
    /// </summary>
    private string GenerateBio(string name, string role, int yoe, string location) =>
        $"Passionate {role} with {yoe} years of experience based in {location}. " +
        $"Specialized in building scalable solutions and mentoring junior developers. " +
        $"Open to remote opportunities and interesting technical challenges.";

    /// <summary>
    /// Returns a random qualification.
    /// </summary>
    private string GetRandomQualification()
    {
        var qualifications = new[] { "B.E. Computer Science", "B.Tech Information Technology", "M.E. Software Engineering", "B.S. Computer Science", "B.Tech CSE" };
        return qualifications[Random.Shared.Next(qualifications.Length)];
    }
}
