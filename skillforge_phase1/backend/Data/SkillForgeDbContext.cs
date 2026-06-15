using Microsoft.EntityFrameworkCore;
using SkillForge.Models;

namespace SkillForge.Data;

public class SkillForgeDbContext : DbContext
{
    private readonly ILogger<SkillForgeDbContext> _logger;

    public SkillForgeDbContext(DbContextOptions<SkillForgeDbContext> options, ILogger<SkillForgeDbContext> logger)
        : base(options)
    {
        _logger = logger;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Candidate> Candidates { get; set; }
    public DbSet<CandidateSkill> CandidateSkills { get; set; }
    public DbSet<Job> Jobs { get; set; }
    public DbSet<JobSkill> JobSkills { get; set; }
    public DbSet<JobMatch> JobMatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ===== USER CONFIGURATION =====
        modelBuilder.Entity<User>()
            .HasKey(u => u.UserId);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasMany(u => u.Candidates)
            .WithOne(c => c.User)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== CANDIDATE CONFIGURATION =====
        modelBuilder.Entity<Candidate>()
            .HasKey(c => c.CandidateId);

        modelBuilder.Entity<Candidate>()
            .HasMany(c => c.Skills)
            .WithOne(s => s.Candidate)
            .HasForeignKey(s => s.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Candidate>()
            .HasMany(c => c.JobMatches)
            .WithOne(m => m.Candidate)
            .HasForeignKey(m => m.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== JOB CONFIGURATION =====
        modelBuilder.Entity<Job>()
            .HasKey(j => j.JobId);

        modelBuilder.Entity<Job>()
            .HasMany(j => j.RequiredSkills)
            .WithOne(s => s.Job)
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Job>()
            .HasMany(j => j.Matches)
            .WithOne(m => m.Job)
            .HasForeignKey(m => m.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // ===== JOB MATCH CONFIGURATION =====
        modelBuilder.Entity<JobMatch>()
            .HasKey(m => m.MatchId);

        // ===== SEED DEFAULT DATA =====
        // ✅ FIX: Use FIXED date instead of DateTime.UtcNow
        var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Default recruiter user
        modelBuilder.Entity<User>().HasData(
            new User
            {
                UserId = 1,
                Username = "admin",
                Email = "admin@skillforge.com",
                PasswordHash = "$2a$11$6r2wEZVCp9v4EL0vPVA8uu7eYQ/qk4jZvJAqEYkLvyTlL6YsH8pf2", // admin123
                Role = "Recruiter",
                CreatedAt = seedDate  // ✅ FIXED: Static date
            }
        );

        // Seed sample jobs
        var sampleJobs = new[]
        {
            new { JobId = 1, Title = "C# Developer", Skills = new[] { "C#", "ASP.NET Core", "SQL Server" } },
            new { JobId = 2, Title = "React Developer", Skills = new[] { "React", "TypeScript", "CSS" } },
            new { JobId = 3, Title = "DevOps Engineer", Skills = new[] { "Docker", "Kubernetes", "Azure" } },
            new { JobId = 4, Title = "Data Engineer", Skills = new[] { "Python", "SQL", "Spark" } },
            new { JobId = 5, Title = "Full Stack Developer", Skills = new[] { "C#", "React", "SQL Server", "Azure" } }
        };

        foreach (var job in sampleJobs)
        {
            modelBuilder.Entity<Job>().HasData(
                new Job
                {
                    JobId = job.JobId,
                    Title = job.Title,
                    Description = $"Looking for an experienced {job.Title}",
                    CompanyName = "SkillForge Inc",
                    Location = "Remote",
                    SalaryRange = "50000-120000",
                    CreatedAt = seedDate  // ✅ FIXED: Static date
                }
            );

            foreach (var skill in job.Skills)
            {
                var skillIndex = Array.IndexOf(job.Skills, skill);
                modelBuilder.Entity<JobSkill>().HasData(
                    new JobSkill
                    {
                        Id = (job.JobId * 10) + skillIndex + 1,
                        JobId = job.JobId,
                        SkillName = skill,
                        IsRequired = true,
                        ProficiencyLevel = 4
                    }
                );
            }
        }
    }

    public override int SaveChanges()
    {
        var result = base.SaveChanges();
        _logger.LogInformation("✅ Database changes saved successfully at {Time}", DateTime.UtcNow);
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("✅ Database changes saved asynchronously at {Time}", DateTime.UtcNow);
        return result;
    }
}