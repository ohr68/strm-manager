using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.Episodes;

namespace StrmManager.Modules.Catalog.Infrastructure.Episodes;

internal sealed class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> builder)
    {
        builder.ToTable("episodes");

        builder.HasKey(episode => episode.Id);

        builder.Property(episode => episode.ExternalId).IsRequired().HasMaxLength(128);
        builder.Property(episode => episode.Title).IsRequired().HasMaxLength(512);
        builder.Property(episode => episode.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(episode => episode.LastError).HasMaxLength(2048);

        builder.HasIndex(episode => new { episode.SeasonId, episode.EpisodeNumber }).IsUnique();
        builder.HasIndex(episode => episode.Status);
        builder.HasIndex(episode => episode.ReleaseAtUtc);
        builder.HasIndex(episode => episode.NextAttemptAtUtc);
    }
}
