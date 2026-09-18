using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrmManager.Modules.Catalog.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodeExternalIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_episodes_ExternalId",
                table: "episodes",
                column: "ExternalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_episodes_ExternalId",
                table: "episodes");
        }
    }
}
