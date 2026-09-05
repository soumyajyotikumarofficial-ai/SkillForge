using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillForge.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCandidateProfileWithRichData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "YearsOfExperienceInt",
                table: "Candidates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryRole",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Seniority",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "Mid");

            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "2 Weeks");

            migrationBuilder.AddColumn<int>(
                name: "ExpectedSalary",
                table: "Candidates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "INR");

            migrationBuilder.AddColumn<string>(
                name: "PortfolioUrl",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GitHubUrl",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LinkedInUrl",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SkillsList",
                table: "Candidates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Create index on frequently queried columns
            migrationBuilder.CreateIndex(
                name: "IX_Candidates_PrimaryRole",
                table: "Candidates",
                column: "PrimaryRole");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_Seniority",
                table: "Candidates",
                column: "Seniority");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_Location",
                table: "Candidates",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_Candidates_Availability",
                table: "Candidates",
                column: "Availability");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Candidates_PrimaryRole",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_Seniority",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_Location",
                table: "Candidates");

            migrationBuilder.DropIndex(
                name: "IX_Candidates_Availability",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "YearsOfExperienceInt",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "PrimaryRole",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "Seniority",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "Availability",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "ExpectedSalary",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "PortfolioUrl",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "GitHubUrl",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "LinkedInUrl",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "Candidates");

            migrationBuilder.DropColumn(
                name: "SkillsList",
                table: "Candidates");
        }
    }
}
