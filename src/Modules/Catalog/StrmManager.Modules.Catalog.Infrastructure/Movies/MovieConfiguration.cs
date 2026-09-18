using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.Movies;

namespace StrmManager.Modules.Catalog.Infrastructure.Movies;

internal sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("movies");

        builder.HasKey(movie => movie.Id);

        builder.Property(movie => movie.Title).IsRequired().HasMaxLength(512);
        builder.Property(movie => movie.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(movie => movie.LastError).HasMaxLength(2048);

        // UpdatedAtUtc doubles as an application-managed optimistic-concurrency token
        // (SQLite has no native rowversion type), exactly as for Episode - it is what lets
        // a Pending -> Searching claim followed by IUnitOfWork.TrySaveChangesAsync reject a
        // second, overlapping claim of the same movie instead of both callers proceeding.
        // See ADR-013. No new column - the field already existed and is updated on every
        // transition.
        builder.Property(movie => movie.UpdatedAtUtc).IsConcurrencyToken();

        builder.OwnsOne(movie => movie.ExternalIds, ownedBuilder =>
        {
            ownedBuilder.Property(externalIds => externalIds.ImdbId).HasColumnName("imdb_id").HasMaxLength(32);
            ownedBuilder.Property(externalIds => externalIds.TmdbId).HasColumnName("tmdb_id").HasMaxLength(32);
            ownedBuilder.Property(externalIds => externalIds.TvdbId).HasColumnName("tvdb_id").HasMaxLength(32);
            ownedBuilder.HasIndex(externalIds => externalIds.ImdbId).IsUnique();
        });

        builder.HasIndex(movie => movie.Status);
        builder.HasIndex(movie => movie.ReleaseAtUtc);
        builder.HasIndex(movie => movie.NextAttemptAtUtc);
    }
}
