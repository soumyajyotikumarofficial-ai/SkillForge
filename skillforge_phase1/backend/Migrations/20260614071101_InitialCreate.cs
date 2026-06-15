using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SkillForge.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    JobId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    SalaryRange = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.JobId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "JobSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    SkillName = table.Column<string>(type: "TEXT", nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProficiencyLevel = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobSkills_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "JobId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Candidates",
                columns: table => new
                {
                    CandidateId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    HighestQualification = table.Column<string>(type: "TEXT", nullable: false),
                    YearsOfExperience = table.Column<string>(type: "TEXT", nullable: false),
                    ResumeScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ResumeFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidates", x => x.CandidateId);
                    table.ForeignKey(
                        name: "FK_Candidates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CandidateSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<int>(type: "INTEGER", nullable: false),
                    SkillName = table.Column<string>(type: "TEXT", nullable: false),
                    Proficiency = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateSkills_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "CandidateId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobMatches",
                columns: table => new
                {
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchScore = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedSkills = table.Column<string>(type: "TEXT", nullable: false),
                    MissingSkills = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobMatches", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_JobMatches_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "CandidateId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobMatches_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "JobId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Jobs",
                columns: new[] { "JobId", "CompanyName", "CreatedAt", "Description", "Location", "SalaryRange", "Title" },
                values: new object[,]
                {
                    { 1, "SkillForge Inc", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Looking for an experienced C# Developer", "Remote", "50000-120000", "C# Developer" },
                    { 2, "SkillForge Inc", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Looking for an experienced React Developer", "Remote", "50000-120000", "React Developer" },
                    { 3, "SkillForge Inc", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Looking for an experienced DevOps Engineer", "Remote", "50000-120000", "DevOps Engineer" },
                    { 4, "SkillForge Inc", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Looking for an experienced Data Engineer", "Remote", "50000-120000", "Data Engineer" },
                    { 5, "SkillForge Inc", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Looking for an experienced Full Stack Developer", "Remote", "50000-120000", "Full Stack Developer" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "UserId", "CreatedAt", "Email", "PasswordHash", "Role", "Username" },
                values: new object[] { 1, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "admin@skillforge.com", "$2a$11$6r2wEZVCp9v4EL0vPVA8uu7eYQ/qk4jZvJAqEYkLvyTlL6YsH8pf2", "Recruiter", "admin" });

            migrationBuilder.InsertData(
                table: "JobSkills",
                columns: new[] { "Id", "IsRequired", "JobId", "ProficiencyLevel", "SkillName" },
                values: new object[,]
                {
                    { 11, true, 1, 4, "C#" },
                    { 12, true, 1, 4, "ASP.NET Core" },
                    { 13, true, 1, 4, "SQL Server" },
                    { 21, true, 2, 4, "React" },
                    { 22, true, 2, 4, "TypeScript" },
                    { 23, true, 2, 4, "CSS" },
                    { 31, true, 3, 4, "Docker" },
                    { 32, true, 3, 4, "Kubernetes" },
                    { 33, true, 3, 4, "Azure" },
                    { 41, true, 4, 4, "Python" },
                    { 42, true, 4, 4, "SQL" },
                    { 43, true, 4, 4, "Spark" },
                    { 51, true, 5, 4, "C#" },
                    { 52, true, 5, 4, "React" },
                    { 53, true, 5, 4, "SQL Server" },
                    { 54, true, 5, 4, "Azure" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_UserId",
                table: "Candidates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateSkills_CandidateId",
                table: "CandidateSkills",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_JobMatches_CandidateId",
                table: "JobMatches",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_JobMatches_JobId",
                table: "JobMatches",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobSkills_JobId",
                table: "JobSkills",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateSkills");

            migrationBuilder.DropTable(
                name: "JobMatches");

            migrationBuilder.DropTable(
                name: "JobSkills");

            migrationBuilder.DropTable(
                name: "Candidates");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
