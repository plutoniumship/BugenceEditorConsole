using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowDisplayIdAndDguid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dguid",
                table: "Workflows",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DisplayId",
                table: "Workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql(@"
UPDATE Workflows
SET Dguid = lower(hex(randomblob(16)))
WHERE Dguid IS NULL OR Dguid = '';
");

                migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY OwnerUserId ORDER BY CreatedAtUtc, Id) AS rn
    FROM Workflows
)
UPDATE Workflows
SET DisplayId = (
    SELECT ranked.rn
    FROM ranked
    WHERE ranked.Id = Workflows.Id
);
");
            }
            else
            {
                migrationBuilder.Sql(@"
UPDATE Workflows
SET Dguid = CONVERT(varchar(32), NEWID(), 2)
WHERE Dguid IS NULL OR Dguid = '';
");

                migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY OwnerUserId ORDER BY CreatedAtUtc, Id) AS rn
    FROM Workflows
)
UPDATE w
SET DisplayId = ranked.rn
FROM Workflows w
INNER JOIN ranked ON ranked.Id = w.Id;
");
            }

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_OwnerUserId_Dguid",
                table: "Workflows",
                columns: new[] { "OwnerUserId", "Dguid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_OwnerUserId_DisplayId",
                table: "Workflows",
                columns: new[] { "OwnerUserId", "DisplayId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workflows_OwnerUserId_Dguid",
                table: "Workflows");

            migrationBuilder.DropIndex(
                name: "IX_Workflows_OwnerUserId_DisplayId",
                table: "Workflows");

            migrationBuilder.DropColumn(
                name: "Dguid",
                table: "Workflows");

            migrationBuilder.DropColumn(
                name: "DisplayId",
                table: "Workflows");
        }
    }
}
