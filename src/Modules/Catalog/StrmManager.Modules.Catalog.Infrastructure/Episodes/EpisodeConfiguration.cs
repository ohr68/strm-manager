using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;

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

        // UpdatedAtUtc doubles as an application-managed optimistic-concurrency token
        // (SQLite has no native rowversion type) - this is what lets
        // ProcessEpisodeCommandHandler's claim (Pending -> Searching) reject a second,
        // overlapping claim of the same episode with a DbUpdateConcurrencyException
        // instead of both callers silently proceeding. See ADR-013. No new column - the
        // field already existed and was already updated on every transition.
        builder.Property(episode => episode.UpdatedAtUtc).IsConcurrencyToken();

        builder.HasIndex(episode => new { episode.SeasonId, episode.EpisodeNumber }).IsUnique();
        // ExternalId (e.g. "tt27497393:1:9") is globally unique per the whole catalog,
        // not just per season - a second, independent guard against duplicate
        // synchronization beyond the composite SeasonId+EpisodeNumber index above.
        builder.HasIndex(episode => episode.ExternalId).IsUnique();
        builder.HasIndex(episode => episode.Status);
        builder.HasIndex(episode => episode.ReleaseAtUtc);
        builder.HasIndex(episode => episode.NextAttemptAtUtc);

        // Same reasoning as Series -> Season (see SeasonConfiguration / ADR-005):
        // FK-only relationship, no navigation property, Restrict delete.
        builder.HasOne<Season>()
            .WithMany()
            .HasForeignKey(episode => episode.SeasonId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
