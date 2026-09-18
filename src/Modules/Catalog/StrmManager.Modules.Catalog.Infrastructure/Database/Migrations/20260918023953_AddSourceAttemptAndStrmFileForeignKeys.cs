using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrmManager.Modules.Catalog.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceAttemptAndStrmFileForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_source_attempts_episodes_EpisodeId",
                table: "source_attempts",
                column: "EpisodeId",
                principalTable: "episodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_source_attempts_movies_MovieId",
                table: "source_attempts",
                column: "MovieId",
                principalTable: "movies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_strm_files_episodes_EpisodeId",
                table: "strm_files",
                column: "EpisodeId",
                principalTable: "episodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_strm_files_movies_MovieId",
                table: "strm_files",
                column: "MovieId",
                principalTable: "movies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_source_attempts_episodes_EpisodeId",
                table: "source_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_source_attempts_movies_MovieId",
                table: "source_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_strm_files_episodes_EpisodeId",
                table: "strm_files");

            migrationBuilder.DropForeignKey(
                name: "FK_strm_files_movies_MovieId",
                table: "strm_files");
        }
    }
}
