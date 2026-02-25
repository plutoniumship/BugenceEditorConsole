using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "UploadedProjects",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailOtpTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOtpTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Last4 = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    ExpMonth = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    ExpYear = table.Column<string>(type: "TEXT", maxLength: 4, nullable: true),
                    BillingNameEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderPaymentMethodIdEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PlanKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TrialEndsUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProviderCustomerId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    ProviderSubscriptionId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    LastPaymentStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtpTickets_Email_Purpose_ExpiresAtUtc",
                table: "EmailOtpTickets",
                columns: new[] { "Email", "Purpose", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_CreatedAtUtc",
                table: "UserNotifications",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPaymentMethods_UserId_IsDefault",
                table: "UserPaymentMethods",
                columns: new[] { "UserId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId_Status",
                table: "UserSubscriptions",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOtpTickets");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "UserPaymentMethods");

            migrationBuilder.DropTable(
                name: "UserSubscriptions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "UploadedProjects");
        }
    }
}
