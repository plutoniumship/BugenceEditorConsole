using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddReliabilityFoundationPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsPageViews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    IsBot = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsPageViews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    UserAgentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReferrerHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    OwnerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDeploySnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VersionLabel = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    RootPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsSuccessful = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDeploySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectDeploySnapshots_UploadedProjects_UploadedProjectId",
                        column: x => x.UploadedProjectId,
                        principalTable: "UploadedProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectPreflightRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Safe = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockersJson = table.Column<string>(type: "TEXT", nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: false),
                    DiffSummaryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPreflightRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectPreflightRuns_UploadedProjects_UploadedProjectId",
                        column: x => x.UploadedProjectId,
                        principalTable: "UploadedProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectEnvironmentPointers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    DraftSnapshotId = table.Column<long>(type: "INTEGER", nullable: true),
                    StagingSnapshotId = table.Column<long>(type: "INTEGER", nullable: true),
                    LiveSnapshotId = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEnvironmentPointers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEnvironmentPointers_ProjectDeploySnapshots_DraftSnapshotId",
                        column: x => x.DraftSnapshotId,
                        principalTable: "ProjectDeploySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectEnvironmentPointers_ProjectDeploySnapshots_LiveSnapshotId",
                        column: x => x.LiveSnapshotId,
                        principalTable: "ProjectDeploySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectEnvironmentPointers_ProjectDeploySnapshots_StagingSnapshotId",
                        column: x => x.StagingSnapshotId,
                        principalTable: "ProjectDeploySnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProjectEnvironmentPointers_UploadedProjects_UploadedProjectId",
                        column: x => x.UploadedProjectId,
                        principalTable: "UploadedProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsPageViews_OwnerUserId_CompanyId_ProjectId_OccurredAtUtc",
                table: "AnalyticsPageViews",
                columns: new[] { "OwnerUserId", "CompanyId", "ProjectId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsPageViews_ProjectId_Path_OccurredAtUtc",
                table: "AnalyticsPageViews",
                columns: new[] { "ProjectId", "Path", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_OwnerUserId_CompanyId_ProjectId_LastSeenUtc",
                table: "AnalyticsSessions",
                columns: new[] { "OwnerUserId", "CompanyId", "ProjectId", "LastSeenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsSessions_SessionId",
                table: "AnalyticsSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDeploySnapshots_UploadedProjectId_Environment_CreatedAtUtc",
                table: "ProjectDeploySnapshots",
                columns: new[] { "UploadedProjectId", "Environment", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEnvironmentPointers_DraftSnapshotId",
                table: "ProjectEnvironmentPointers",
                column: "DraftSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEnvironmentPointers_LiveSnapshotId",
                table: "ProjectEnvironmentPointers",
                column: "LiveSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEnvironmentPointers_StagingSnapshotId",
                table: "ProjectEnvironmentPointers",
                column: "StagingSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEnvironmentPointers_UploadedProjectId",
                table: "ProjectEnvironmentPointers",
                column: "UploadedProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPreflightRuns_UploadedProjectId_CreatedAtUtc",
                table: "ProjectPreflightRuns",
                columns: new[] { "UploadedProjectId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsPageViews");

            migrationBuilder.DropTable(
                name: "AnalyticsSessions");

            migrationBuilder.DropTable(
                name: "ProjectEnvironmentPointers");

            migrationBuilder.DropTable(
                name: "ProjectPreflightRuns");

            migrationBuilder.DropTable(
                name: "ProjectDeploySnapshots");
        }
    }
}
