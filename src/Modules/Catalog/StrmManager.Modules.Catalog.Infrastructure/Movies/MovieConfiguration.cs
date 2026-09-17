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
