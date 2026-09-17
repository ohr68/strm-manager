using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrmManager.Modules.Catalog.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_episodes_seasons_SeasonId",
                table: "episodes",
                column: "SeasonId",
                principalTable: "seasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_seasons_series_SeriesId",
                table: "seasons",
                column: "SeriesId",
                principalTable: "series",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_episodes_seasons_SeasonId",
                table: "episodes");

            migrationBuilder.DropForeignKey(
                name: "FK_seasons_series_SeriesId",
                table: "seasons");
        }
    }
}
