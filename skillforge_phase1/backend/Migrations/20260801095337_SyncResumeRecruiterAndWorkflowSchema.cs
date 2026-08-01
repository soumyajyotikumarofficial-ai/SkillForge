using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillForge.API.Migrations
{
    /// <inheritdoc />
    public partial class SyncResumeRecruiterAndWorkflowSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FinalUrl",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkMode",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ActiveResumeId",
                table: "Candidates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredWorkMode",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CandidateResumes",
                columns: table => new
                {
                    CandidateResumeId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedResumeJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateResumes", x => x.CandidateResumeId);
                    table.ForeignKey(
                        name: "FK_CandidateResumes_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "CandidateId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recruiters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    Designation = table.Column<string>(type: "TEXT", nullable: false),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recruiters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyJobRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecruiterId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleTitle = table.Column<string>(type: "TEXT", nullable: false),
                    JobDescription = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredSkills = table.Column<string>(type: "TEXT", nullable: false),
                    YearsOfExperience = table.Column<string>(type: "TEXT", nullable: false),
                    SalaryRange = table.Column<string>(type: "TEXT", nullable: true),
                    WorkModes = table.Column<string>(type: "TEXT", nullable: false),
                    Locations = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyDescription = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyJobRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyJobRequests_Recruiters_RecruiterId",
                        column: x => x.RecruiterId,
                        principalTable: "Recruiters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectHiringRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecruiterId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectDescription = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredSkills = table.Column<string>(type: "TEXT", nullable: false),
                    YearsOfExperience = table.Column<string>(type: "TEXT", nullable: false),
                    SalaryRange = table.Column<string>(type: "TEXT", nullable: true),
                    WorkModes = table.Column<string>(type: "TEXT", nullable: false),
                    Locations = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectDeadline = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompanyDescription = table.Column<string>(type: "TEXT", nullable: true),
                    TeamBreakdownJson = table.Column<string>(type: "TEXT", nullable: true),
                    TeamBreakdownApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectHiringRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectHiringRequests_Recruiters_RecruiterId",
                        column: x => x.RecruiterId,
                        principalTable: "Recruiters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CandidateShortlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowType = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanyJobRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectHiringRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    CandidateId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchScore = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchExplanation = table.Column<string>(type: "TEXT", nullable: false),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContactRevealed = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailSentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateShortlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateShortlists_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "CandidateId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CandidateShortlists_CompanyJobRequests_CompanyJobRequestId",
                        column: x => x.CompanyJobRequestId,
                        principalTable: "CompanyJobRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CandidateShortlists_ProjectHiringRequests_ProjectHiringRequestId",
                        column: x => x.ProjectHiringRequestId,
                        principalTable: "ProjectHiringRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 1,
                columns: new[] { "FinalUrl", "WorkMode" },
                values: new object[] { "", "Hybrid" });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 2,
                columns: new[] { "FinalUrl", "WorkMode" },
                values: new object[] { "", "Hybrid" });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 3,
                columns: new[] { "FinalUrl", "WorkMode" },
                values: new object[] { "", "Hybrid" });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 4,
                columns: new[] { "FinalUrl", "WorkMode" },
                values: new object[] { "", "Hybrid" });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 5,
                columns: new[] { "FinalUrl", "WorkMode" },
                values: new object[] { "", "Hybrid" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ApplyUrl",
                table: "Jobs",
                column: "ApplyUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_ActiveResumeId",
                table: "Candidates",
                column: "ActiveResumeId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateResumes_CandidateId_FileName",
                table: "CandidateResumes",
                columns: new[] { "CandidateId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateShortlists_CandidateId",
                table: "CandidateShortlists",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateShortlists_CompanyJobRequestId",
                table: "CandidateShortlists",
                column: "CompanyJobRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateShortlists_ProjectHiringRequestId",
                table: "CandidateShortlists",
                column: "ProjectHiringRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyJobRequests_RecruiterId",
                table: "CompanyJobRequests",
                column: "RecruiterId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectHiringRequests_RecruiterId",
                table: "ProjectHiringRequests",
                column: "RecruiterId");

            migrationBuilder.CreateIndex(
                name: "IX_Recruiters_Email",
                table: "Recruiters",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Candidates_CandidateResumes_ActiveResumeId",
                table: "Candidates",
                column: "ActiveResumeId",
                principalTable: "CandidateResumes",
                principalColumn: "CandidateResumeId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Candidates_CandidateResumes_ActiveResumeId",
                table: "Candidates");

            migrationBuilder.DropTable(
                name: "CandidateResumes");

            migrationBuilder.DropTable(
                name: "CandidateShortlists");

            migrationBuilder.DropTable(
                name: "CompanyJobRequests");

            migrationBuilder.DropTable(
                name: "ProjectHiringRequests");

            migrationBuilder.DropTable(
                name: "Recruiters");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_ApplyUrl",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_ActiveResumeId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "FinalUrl",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "WorkMode",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ActiveResumeId",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "PreferredWorkMode",
                table: "Candidates");
        }
    }
}
