using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrmManager.Modules.Catalog.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Runtime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    ReleaseAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_episodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "movies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    imdb_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    tmdb_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    tvdb_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Runtime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    ReleaseAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    imdb_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    tmdb_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    tvdb_id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "source_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MovieId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    ExpectedDuration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    DifferencePercentage = table.Column<double>(type: "REAL", nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AudioCodec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "strm_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MovieId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strm_files", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_episodes_NextAttemptAtUtc",
                table: "episodes",
                column: "NextAttemptAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_episodes_ReleaseAtUtc",
                table: "episodes",
                column: "ReleaseAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_episodes_SeasonId_EpisodeNumber",
                table: "episodes",
                columns: new[] { "SeasonId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_episodes_Status",
                table: "episodes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_movies_NextAttemptAtUtc",
                table: "movies",
                column: "NextAttemptAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_movies_ReleaseAtUtc",
                table: "movies",
                column: "ReleaseAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_movies_Status",
                table: "movies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_movies_imdb_id",
                table: "movies",
                column: "imdb_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seasons_SeriesId_Number",
                table: "seasons",
                columns: new[] { "SeriesId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_series_imdb_id",
                table: "series",
                column: "imdb_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_attempts_EpisodeId",
                table: "source_attempts",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_source_attempts_MovieId",
                table: "source_attempts",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_strm_files_EpisodeId",
                table: "strm_files",
                column: "EpisodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_strm_files_MovieId",
                table: "strm_files",
                column: "MovieId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "episodes");

            migrationBuilder.DropTable(
                name: "movies");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "series");

            migrationBuilder.DropTable(
                name: "source_attempts");

            migrationBuilder.DropTable(
                name: "strm_files");
        }
    }
}
