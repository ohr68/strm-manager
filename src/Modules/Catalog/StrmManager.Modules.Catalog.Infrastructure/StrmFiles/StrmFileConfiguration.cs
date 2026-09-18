using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.StrmFiles;

namespace StrmManager.Modules.Catalog.Infrastructure.StrmFiles;

internal sealed class StrmFileConfiguration : IEntityTypeConfiguration<StrmFile>
{
    public void Configure(EntityTypeBuilder<StrmFile> builder)
    {
        builder.ToTable("strm_files");

        builder.HasKey(strmFile => strmFile.Id);

        builder.Property(strmFile => strmFile.Path).IsRequired().HasMaxLength(1024);

        // Unique (from Phase 1, unchanged) - one Episode/Movie can never accidentally
        // receive two active StrmFile records. Restrict on delete, same reasoning as
        // SourceAttemptConfiguration/ADR-005.
        builder.HasIndex(strmFile => strmFile.EpisodeId).IsUnique();
        builder.HasIndex(strmFile => strmFile.MovieId).IsUnique();

        builder.HasOne<Episode>()
            .WithOne()
            .HasForeignKey<StrmFile>(strmFile => strmFile.EpisodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Movie>()
            .WithOne()
            .HasForeignKey<StrmFile>(strmFile => strmFile.MovieId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
