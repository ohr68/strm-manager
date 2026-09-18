using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrmManager.Modules.Catalog.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAutonomousSchedulingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastMetadataRefreshAtUtc",
                table: "series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextMetadataRefreshAtUtc",
                table: "series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_series_NextMetadataRefreshAtUtc",
                table: "series",
                column: "NextMetadataRefreshAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_series_NextMetadataRefreshAtUtc",
                table: "series");

            migrationBuilder.DropColumn(
                name: "LastMetadataRefreshAtUtc",
                table: "series");

            migrationBuilder.DropColumn(
                name: "NextMetadataRefreshAtUtc",
                table: "series");
        }
    }
}
