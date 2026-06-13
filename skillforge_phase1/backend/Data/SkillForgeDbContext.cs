using Microsoft.EntityFrameworkCore;
using SkillForge.Models;

namespace SkillForge.Data;

public class SkillForgeDbContext : DbContext
{
    public SkillForgeDbContext(DbContextOptions<SkillForgeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<CandidateSkill> CandidateSkills => Set<CandidateSkill>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<JobMatch> JobMatches => Set<JobMatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Users
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // Candidates
        modelBuilder.Entity<Candidate>()
            .HasOne(c => c.User)
            .WithMany(u => u.Candidates)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Candidate>()
            .HasIndex(c => c.Phone)
            .IsUnique();

        // CandidateSkills
        modelBuilder.Entity<CandidateSkill>()
            .HasOne(cs => cs.Candidate)
            .WithMany(c => c.Skills)
            .HasForeignKey(cs => cs.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        // JobSkills
        modelBuilder.Entity<JobSkill>()
            .HasOne(js => js.Job)
            .WithMany(j => j.RequiredSkills)
            .HasForeignKey(js => js.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        // JobMatches
        modelBuilder.Entity<JobMatch>()
            .HasOne(jm => jm.Job)
            .WithMany(j => j.Matches)
            .HasForeignKey(jm => jm.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JobMatch>()
            .HasOne(jm => jm.Candidate)
            .WithMany(c => c.JobMatches)
            .HasForeignKey(jm => jm.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed data
        SeedData(modelBuilder);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        // Users
        modelBuilder.Entity<User>().HasData(
            new User 
            { 
                UserId = 1, 
                Username = "recruiter", 
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("recruiter123"), 
                Email = "recruiter@skillforge.com", 
                Role = "Recruiter" 
            },
            new User 
            { 
                UserId = 2, 
                Username = "candidate1", 
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("candidate123"), 
                Email = "candidate1@example.com", 
                Role = "Candidate" 
            }
        );

        // Jobs (IT Positions)
        modelBuilder.Entity<Job>().HasData(
            new Job { JobId = 1, Title = "Senior Backend Developer", CompanyName = "Tech Corp", Location = "Bangalore", Description = "Build scalable APIs", SalaryRange = "15-20 LPA" },
            new Job { JobId = 2, Title = "Full Stack Developer", CompanyName = "StartUp Inc", Location = "Mumbai", Description = "React + Node.js", SalaryRange = "12-16 LPA" },
            new Job { JobId = 3, Title = "DevOps Engineer", CompanyName = "Cloud Systems", Location = "Pune", Description = "AWS, Docker, Kubernetes", SalaryRange = "14-18 LPA" },
            new Job { JobId = 4, Title = "Frontend Developer", CompanyName = "Digital Agency", Location = "Bangalore", Description = "Angular, React, Vue", SalaryRange = "10-14 LPA" },
            new Job { JobId = 5, Title = "Database Administrator", CompanyName = "Enterprise Solutions", Location = "Hyderabad", Description = "SQL Server, PostgreSQL", SalaryRange = "12-15 LPA" },
            new Job { JobId = 6, Title = "Cloud Architect", CompanyName = "Azure Partners", Location = "Delhi", Description = "Design cloud infrastructure", SalaryRange = "18-25 LPA" },
            new Job { JobId = 7, Title = "QA Engineer", CompanyName = "Quality First", Location = "Chennai", Description = "Automated testing", SalaryRange = "8-12 LPA" },
            new Job { JobId = 8, Title = "Mobile Developer", CompanyName = "App Makers", Location = "Bangalore", Description = "iOS, Android development", SalaryRange = "11-15 LPA" },
            new Job { JobId = 9, Title = "Data Engineer", CompanyName = "Analytics Pro", Location = "Bangalore", Description = "Big Data, ETL pipelines", SalaryRange = "14-19 LPA" },
            new Job { JobId = 10, Title = "Security Engineer", CompanyName = "SecureNet", Location = "Gurgaon", Description = "Application security", SalaryRange = "16-22 LPA" }
        );

        // Job Skills
        var jobSkillsData = new List<JobSkill>
        {
            // Job 1: Backend Developer
            new JobSkill { Id = 1, JobId = 1, SkillName = "C#", IsRequired = true },
            new JobSkill { Id = 2, JobId = 1, SkillName = "ASP.NET Core", IsRequired = true },
            new JobSkill { Id = 3, JobId = 1, SkillName = "SQL Server", IsRequired = true },
            new JobSkill { Id = 4, JobId = 1, SkillName = "Azure", IsRequired = false },
            
            // Job 2: Full Stack Developer
            new JobSkill { Id = 5, JobId = 2, SkillName = "React", IsRequired = true },
            new JobSkill { Id = 6, JobId = 2, SkillName = "Node.js", IsRequired = true },
            new JobSkill { Id = 7, JobId = 2, SkillName = "JavaScript", IsRequired = true },
            new JobSkill { Id = 8, JobId = 2, SkillName = "MongoDB", IsRequired = false },
            
            // Job 3: DevOps Engineer
            new JobSkill { Id = 9, JobId = 3, SkillName = "Docker", IsRequired = true },
            new JobSkill { Id = 10, JobId = 3, SkillName = "Kubernetes", IsRequired = true },
            new JobSkill { Id = 11, JobId = 3, SkillName = "AWS", IsRequired = true },
            new JobSkill { Id = 12, JobId = 3, SkillName = "Linux", IsRequired = true },
            
            // Job 4: Frontend Developer
            new JobSkill { Id = 13, JobId = 4, SkillName = "React", IsRequired = true },
            new JobSkill { Id = 14, JobId = 4, SkillName = "TypeScript", IsRequired = true },
            new JobSkill { Id = 15, JobId = 4, SkillName = "HTML", IsRequired = true },
            new JobSkill { Id = 16, JobId = 4, SkillName = "CSS", IsRequired = true },
            
            // Job 5: Database Administrator
            new JobSkill { Id = 17, JobId = 5, SkillName = "SQL Server", IsRequired = true },
            new JobSkill { Id = 18, JobId = 5, SkillName = "PostgreSQL", IsRequired = true },
            new JobSkill { Id = 19, JobId = 5, SkillName = "Backup & Recovery", IsRequired = true }
        };

        modelBuilder.Entity<JobSkill>().HasData(jobSkillsData);
    }
}