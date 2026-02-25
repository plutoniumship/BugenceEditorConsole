using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class DomainInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPublishedAtUtc",
                table: "UploadedProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishStoragePath",
                table: "UploadedProjects",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "UploadedProjects",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
UPDATE UploadedProjects
SET Slug = (
    CASE
        WHEN LENGTH(TRIM(
            LOWER(
                REPLACE(REPLACE(REPLACE(COALESCE(DisplayName, FolderName, 'project'), ' ', '-'), '_', '-'), '.', '-')
            )
        )) = 0 THEN 'project-' || Id
        ELSE
            TRIM(LOWER(
                REPLACE(REPLACE(REPLACE(COALESCE(DisplayName, FolderName, 'project'), ' ', '-'), '_', '-'), '.', '-')
            )) || '-' || Id
    END
)
WHERE IFNULL(Slug, '') = '';
""");

            migrationBuilder.CreateTable(
                name: "ProjectDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    DomainName = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    NormalizedDomain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    ApexRoot = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    DomainType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SslStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    VerificationToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CertificatePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CertificateKeyPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSslRenewalAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectDomains_UploadedProjects_UploadedProjectId",
                        column: x => x.UploadedProjectId,
                        principalTable: "UploadedProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDomainDnsRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectDomainId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSatisfied = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExternalRecordId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDomainDnsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectDomainDnsRecords_ProjectDomains_ProjectDomainId",
                        column: x => x.ProjectDomainId,
                        principalTable: "ProjectDomains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadedProjects_Slug",
                table: "UploadedProjects",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDomainDnsRecords_ProjectDomainId_RecordType_Name_Purpose",
                table: "ProjectDomainDnsRecords",
                columns: new[] { "ProjectDomainId", "RecordType", "Name", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDomains_DomainName",
                table: "ProjectDomains",
                column: "DomainName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDomains_NormalizedDomain",
                table: "ProjectDomains",
                column: "NormalizedDomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDomains_UploadedProjectId",
                table: "ProjectDomains",
                column: "UploadedProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectDomainDnsRecords");

            migrationBuilder.DropTable(
                name: "ProjectDomains");

            migrationBuilder.DropIndex(
                name: "IX_UploadedProjects_Slug",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "LastPublishedAtUtc",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "PublishStoragePath",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "UploadedProjects");
        }
    }
}
