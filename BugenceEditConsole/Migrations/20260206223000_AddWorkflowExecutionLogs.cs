using System;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260206223000_AddWorkflowExecutionLogs")]
    public partial class AddWorkflowExecutionLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkflowExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_WorkflowId_ExecutedAtUtc",
                table: "WorkflowExecutionLogs",
                columns: new[] { "WorkflowId", "ExecutedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkflowExecutionLogs");
        }
    }
}
