using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RequesterName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    RequesterEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IntegrationKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourcePage = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_Category_Status_CreatedAtUtc",
                table: "SupportRequests",
                columns: new[] { "Category", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_DisplayId",
                table: "SupportRequests",
                column: "DisplayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportRequests_OwnerUserId_CreatedAtUtc",
                table: "SupportRequests",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportRequests");
        }
    }
}
