using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class DomainObservabilityEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailureCount",
                table: "ProjectDomains",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailureNotifiedAtUtc",
                table: "ProjectDomains",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureStreak",
                table: "DomainVerificationLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationSent",
                table: "DomainVerificationLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDomains_ConsecutiveFailureCount",
                table: "ProjectDomains",
                column: "ConsecutiveFailureCount");

            migrationBuilder.CreateIndex(
                name: "IX_DomainVerificationLogs_FailureStreak",
                table: "DomainVerificationLogs",
                column: "FailureStreak");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectDomains_ConsecutiveFailureCount",
                table: "ProjectDomains");

            migrationBuilder.DropIndex(
                name: "IX_DomainVerificationLogs_FailureStreak",
                table: "DomainVerificationLogs");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailureCount",
                table: "ProjectDomains");

            migrationBuilder.DropColumn(
                name: "LastFailureNotifiedAtUtc",
                table: "ProjectDomains");

            migrationBuilder.DropColumn(
                name: "FailureStreak",
                table: "DomainVerificationLogs");

            migrationBuilder.DropColumn(
                name: "NotificationSent",
                table: "DomainVerificationLogs");
        }
    }
}
