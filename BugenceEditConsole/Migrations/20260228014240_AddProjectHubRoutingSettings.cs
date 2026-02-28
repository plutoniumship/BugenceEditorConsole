using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectHubRoutingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoDeployOnPush",
                table: "UploadedProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableContentSecurityPolicy",
                table: "UploadedProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnablePreviewDeploys",
                table: "UploadedProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnforceHttps",
                table: "UploadedProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocalPreviewPath",
                table: "UploadedProjects",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageRouteOverridesJson",
                table: "UploadedProjects",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoDeployOnPush",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "EnableContentSecurityPolicy",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "EnablePreviewDeploys",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "EnforceHttps",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "LocalPreviewPath",
                table: "UploadedProjects");

            migrationBuilder.DropColumn(
                name: "PageRouteOverridesJson",
                table: "UploadedProjects");
        }
    }
}
