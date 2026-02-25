using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyProfileAndProjectIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "UploadedProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompanyAdmin",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AddressLine1 = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AddressLine2 = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    StateOrProvince = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ExpectedUserCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.Id);
                });

            if (ActiveProvider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(@"
INSERT INTO CompanyProfiles (Id, Name, CreatedByUserId, CreatedAtUtc)
SELECT
    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6))),
    COALESCE(NULLIF(up.BusinessName, ''), COALESCE(NULLIF(u.DisplayName, ''), COALESCE(NULLIF(u.UserName, ''), 'Company'))),
    u.Id,
    CURRENT_TIMESTAMP
FROM AspNetUsers u
LEFT JOIN UserProfiles up ON up.UserId = u.Id
WHERE u.CompanyId IS NULL;");

                migrationBuilder.Sql(@"
UPDATE AspNetUsers
SET CompanyId = (
    SELECT cp.Id
    FROM CompanyProfiles cp
    WHERE cp.CreatedByUserId = AspNetUsers.Id
    LIMIT 1
),
IsCompanyAdmin = 1
WHERE CompanyId IS NULL;");

                migrationBuilder.Sql(@"
UPDATE UploadedProjects
SET CompanyId = (
    SELECT u.CompanyId
    FROM AspNetUsers u
    WHERE u.Id = UploadedProjects.UserId
)
WHERE CompanyId IS NULL;");
            }
            else
            {
                migrationBuilder.Sql(@"
INSERT INTO CompanyProfiles (Id, Name, CreatedByUserId, CreatedAtUtc)
SELECT
    NEWID(),
    COALESCE(NULLIF(up.BusinessName, ''), COALESCE(NULLIF(u.DisplayName, ''), COALESCE(NULLIF(u.UserName, ''), 'Company'))),
    u.Id,
    SYSUTCDATETIME()
FROM AspNetUsers u
LEFT JOIN UserProfiles up ON up.UserId = u.Id
WHERE u.CompanyId IS NULL;");

                migrationBuilder.Sql(@"
UPDATE u
SET u.CompanyId = cp.Id,
    u.IsCompanyAdmin = 1
FROM AspNetUsers u
CROSS APPLY (
    SELECT TOP 1 Id
    FROM CompanyProfiles
    WHERE CreatedByUserId = u.Id
) cp
WHERE u.CompanyId IS NULL;");

                migrationBuilder.Sql(@"
UPDATE p
SET p.CompanyId = u.CompanyId
FROM UploadedProjects p
INNER JOIN AspNetUsers u ON u.Id = p.UserId
WHERE p.CompanyId IS NULL;");
            }

            migrationBuilder.CreateIndex(
                name: "IX_UploadedProjects_CompanyId_UploadedAtUtc",
                table: "UploadedProjects",
                columns: new[] { "CompanyId", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_Name",
                table: "CompanyProfiles",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_CompanyProfiles_CompanyId",
                table: "AspNetUsers",
                column: "CompanyId",
                principalTable: "CompanyProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UploadedProjects_CompanyProfiles_CompanyId",
                table: "UploadedProjects",
                column: "CompanyId",
                principalTable: "CompanyProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_CompanyProfiles_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_UploadedProjects_CompanyProfiles_CompanyId",
                table: "UploadedProjects");

            migrationBuilder.DropTable(
                name: "CompanyProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UploadedProjects_CompanyId_UploadedAtUtc",
                table: "UploadedProjects");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsCompanyAdmin",
                table: "AspNetUsers");
        }
    }
}
