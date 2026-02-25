using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TriggerType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TriggerConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_OwnerUserId_CreatedAtUtc",
                table: "Workflows",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Workflows");
        }
    }
}
