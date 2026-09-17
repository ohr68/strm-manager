using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.Infrastructure.Series;

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<SeriesEntity>
{
    public void Configure(EntityTypeBuilder<SeriesEntity> builder)
    {
        builder.ToTable("series");

        builder.HasKey(series => series.Id);

        builder.Property(series => series.Title).IsRequired().HasMaxLength(512);
        builder.Property(series => series.OriginalTitle).HasMaxLength(512);
        builder.Property(series => series.Status).HasConversion<string>().HasMaxLength(32);

        builder.OwnsOne(series => series.ExternalIds, ownedBuilder =>
        {
            ownedBuilder.Property(externalIds => externalIds.ImdbId).HasColumnName("imdb_id").HasMaxLength(32);
            ownedBuilder.Property(externalIds => externalIds.TmdbId).HasColumnName("tmdb_id").HasMaxLength(32);
            ownedBuilder.Property(externalIds => externalIds.TvdbId).HasColumnName("tvdb_id").HasMaxLength(32);
            ownedBuilder.HasIndex(externalIds => externalIds.ImdbId).IsUnique();
        });
    }
}
