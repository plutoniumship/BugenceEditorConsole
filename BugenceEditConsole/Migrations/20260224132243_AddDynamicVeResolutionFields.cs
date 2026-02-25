using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BugenceEditConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicVeResolutionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastResolvedAtUtc",
                table: "DynamicVeElementMaps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastResolvedSelector",
                table: "DynamicVeElementMaps",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastResolvedAtUtc",
                table: "DynamicVeElementMaps");

            migrationBuilder.DropColumn(
                name: "LastResolvedSelector",
                table: "DynamicVeElementMaps");
        }
    }
}
