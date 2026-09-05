using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillForge.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailAuditLogsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecruiterId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateId = table.Column<int>(type: "INTEGER", nullable: false),
                    CompanyJobRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectHiringRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkflowType = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipientEmail = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientName = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    SendSuccessful = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    MessageId = table.Column<string>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClickedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailAuditLogs_Candidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidates",
                        principalColumn: "CandidateId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmailAuditLogs_CompanyJobRequests_CompanyJobRequestId",
                        column: x => x.CompanyJobRequestId,
                        principalTable: "CompanyJobRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailAuditLogs_ProjectHiringRequests_ProjectHiringRequestId",
                        column: x => x.ProjectHiringRequestId,
                        principalTable: "ProjectHiringRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailAuditLogs_Recruiters_RecruiterId",
                        column: x => x.RecruiterId,
                        principalTable: "Recruiters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuditLogs_CandidateId",
                table: "EmailAuditLogs",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuditLogs_CompanyJobRequestId",
                table: "EmailAuditLogs",
                column: "CompanyJobRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuditLogs_ProjectHiringRequestId",
                table: "EmailAuditLogs",
                column: "ProjectHiringRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuditLogs_RecruiterId",
                table: "EmailAuditLogs",
                column: "RecruiterId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuditLogs_SentAt",
                table: "EmailAuditLogs",
                column: "SentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailAuditLogs");
        }
    }
}
