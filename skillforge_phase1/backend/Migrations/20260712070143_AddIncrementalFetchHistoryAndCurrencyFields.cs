using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkillForge.API.Migrations
{
    /// <inheritdoc />
    public partial class AddIncrementalFetchHistoryAndCurrencyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FetchedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceCreatedAt",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobFetchHistories",
                columns: table => new
                {
                    JobFetchHistoryId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastSuccessfulFetchUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastQuery = table.Column<string>(type: "TEXT", nullable: false),
                    LastLocation = table.Column<string>(type: "TEXT", nullable: false),
                    LastCountry = table.Column<string>(type: "TEXT", nullable: false),
                    InsertedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SkippedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobFetchHistories", x => x.JobFetchHistoryId);
                });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 1,
                columns: new[] { "Currency", "FetchedAtUtc", "SourceCreatedAt" },
                values: new object[] { "USD", new DateTime(2026, 7, 12, 7, 1, 41, 806, DateTimeKind.Utc).AddTicks(5187), null });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 2,
                columns: new[] { "Currency", "FetchedAtUtc", "SourceCreatedAt" },
                values: new object[] { "USD", new DateTime(2026, 7, 12, 7, 1, 41, 807, DateTimeKind.Utc).AddTicks(923), null });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 3,
                columns: new[] { "Currency", "FetchedAtUtc", "SourceCreatedAt" },
                values: new object[] { "USD", new DateTime(2026, 7, 12, 7, 1, 41, 807, DateTimeKind.Utc).AddTicks(998), null });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 4,
                columns: new[] { "Currency", "FetchedAtUtc", "SourceCreatedAt" },
                values: new object[] { "USD", new DateTime(2026, 7, 12, 7, 1, 41, 807, DateTimeKind.Utc).AddTicks(1061), null });

            migrationBuilder.UpdateData(
                table: "Jobs",
                keyColumn: "JobId",
                keyValue: 5,
                columns: new[] { "Currency", "FetchedAtUtc", "SourceCreatedAt" },
                values: new object[] { "USD", new DateTime(2026, 7, 12, 7, 1, 41, 807, DateTimeKind.Utc).AddTicks(1122), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobFetchHistories");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "FetchedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SourceCreatedAt",
                table: "Jobs");
        }
    }
}
