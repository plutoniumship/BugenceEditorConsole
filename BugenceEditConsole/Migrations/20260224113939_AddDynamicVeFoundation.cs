using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicVeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DynamicVeAuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: true),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVeAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVePageRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    PagePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Environment = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BaseSnapshotId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVePageRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVePageRevisions_UploadedProjects_UploadedProjectId",
                        column: x => x.UploadedProjectId,
                        principalTable: "UploadedProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVeProjectConfigs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UploadedProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RuntimePolicy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FeatureEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DraftRevisionId = table.Column<long>(type: "INTEGER", nullable: true),
                    StagingRevisionId = table.Column<long>(type: "INTEGER", nullable: true),
                    LiveRevisionId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVeProjectConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVeProjectConfigs_UploadedProjects_UploadedProjectId",
                        column: x => x.UploadedProjectId,
                        principalTable: "UploadedProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVeActionBindings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ElementKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NavigateUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    BehaviorJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVeActionBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVeActionBindings_DynamicVePageRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DynamicVePageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVeElementMaps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ElementKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    PrimarySelector = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FallbackSelectorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    FingerprintHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AnchorHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVeElementMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVeElementMaps_DynamicVePageRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DynamicVePageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVePatchRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ElementKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Breakpoint = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Property = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVePatchRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVePatchRules_DynamicVePageRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DynamicVePageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVePublishArtifacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ArtifactType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ArtifactPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Checksum = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVePublishArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVePublishArtifacts_DynamicVePageRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DynamicVePageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVeSectionInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    InsertMode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TargetElementKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    MarkupJson = table.Column<string>(type: "TEXT", nullable: false),
                    CssJson = table.Column<string>(type: "TEXT", nullable: false),
                    JsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVeSectionInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVeSectionInstances_DynamicVePageRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DynamicVePageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicVeTextPatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RevisionId = table.Column<long>(type: "INTEGER", nullable: false),
                    ElementKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    TextMode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicVeTextPatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicVeTextPatches_DynamicVePageRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "DynamicVePageRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVeActionBindings_RevisionId_ElementKey",
                table: "DynamicVeActionBindings",
                columns: new[] { "RevisionId", "ElementKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVeAuditLogs_ProjectId_AtUtc",
                table: "DynamicVeAuditLogs",
                columns: new[] { "ProjectId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVeElementMaps_RevisionId_ElementKey",
                table: "DynamicVeElementMaps",
                columns: new[] { "RevisionId", "ElementKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVePageRevisions_UploadedProjectId_PagePath_CreatedAtUtc",
                table: "DynamicVePageRevisions",
                columns: new[] { "UploadedProjectId", "PagePath", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVePatchRules_RevisionId_ElementKey_Breakpoint_State",
                table: "DynamicVePatchRules",
                columns: new[] { "RevisionId", "ElementKey", "Breakpoint", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVeProjectConfigs_UploadedProjectId",
                table: "DynamicVeProjectConfigs",
                column: "UploadedProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVePublishArtifacts_RevisionId_PublishedAtUtc",
                table: "DynamicVePublishArtifacts",
                columns: new[] { "RevisionId", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVeSectionInstances_RevisionId_TemplateId",
                table: "DynamicVeSectionInstances",
                columns: new[] { "RevisionId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_DynamicVeTextPatches_RevisionId_ElementKey",
                table: "DynamicVeTextPatches",
                columns: new[] { "RevisionId", "ElementKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DynamicVeActionBindings");

            migrationBuilder.DropTable(
                name: "DynamicVeAuditLogs");

            migrationBuilder.DropTable(
                name: "DynamicVeElementMaps");

            migrationBuilder.DropTable(
                name: "DynamicVePatchRules");

            migrationBuilder.DropTable(
                name: "DynamicVeProjectConfigs");

            migrationBuilder.DropTable(
                name: "DynamicVePublishArtifacts");

            migrationBuilder.DropTable(
                name: "DynamicVeSectionInstances");

            migrationBuilder.DropTable(
                name: "DynamicVeTextPatches");

            migrationBuilder.DropTable(
                name: "DynamicVePageRevisions");
        }
    }
}
