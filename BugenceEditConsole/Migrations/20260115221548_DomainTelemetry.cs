using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class DomainTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DomainVerificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectDomainId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SslStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    AllRecordsSatisfied = table.Column<bool>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainVerificationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainVerificationLogs_ProjectDomains_ProjectDomainId",
                        column: x => x.ProjectDomainId,
                        principalTable: "ProjectDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DomainVerificationLogs_ProjectDomainId_CheckedAtUtc",
                table: "DomainVerificationLogs",
                columns: new[] { "ProjectDomainId", "CheckedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DomainVerificationLogs");
        }
    }
}
