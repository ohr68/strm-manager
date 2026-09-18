using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.UnitTests.Episodes;

public class EpisodeMetadataUpdateTests
{
    private static readonly DateTime ReleaseAtUtc = new(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);

    private static Episode CreateScheduledEpisode(DateTime utcNow) =>
        Episode.Schedule(
            seasonId: Guid.NewGuid(),
            externalId: "tt27497393:1:9",
            title: "Original Title",
            seasonNumber: 1,
            episodeNumber: 9,
            runtime: TimeSpan.FromMinutes(20),
            releaseAtUtc: ReleaseAtUtc,
            utcNow: utcNow);

    [Fact]
    public void UpdateMetadata_WhenScheduled_UpdatesReleaseAtUtc()
    {
        DateTime utcNow = ReleaseAtUtc.AddDays(-30);
        Episode episode = CreateScheduledEpisode(utcNow);
        DateTime correctedReleaseAtUtc = ReleaseAtUtc.AddDays(-1);

        bool changed = episode.UpdateMetadata("Original Title", TimeSpan.FromMinutes(20), correctedReleaseAtUtc, utcNow);

        Assert.True(changed);
        Assert.Equal(correctedReleaseAtUtc, episode.ReleaseAtUtc);
        Assert.Equal(MediaStatus.Scheduled, episode.Status);
    }

    [Fact]
    public void UpdateMetadata_WhenScheduled_CorrectedReleaseDateNowInPast_BecomesEligibleAfterTryBecomeEligible()
    {
        DateTime utcNow = ReleaseAtUtc.AddDays(-30);
        Episode episode = CreateScheduledEpisode(utcNow);

        // Metadata refresh happens 10 days later and reveals the release date was
        // actually a week earlier than first thought - now in the past.
        DateTime refreshUtcNow = ReleaseAtUtc.AddDays(-10);
        DateTime correctedReleaseAtUtc = ReleaseAtUtc.AddDays(-20);

        episode.UpdateMetadata("Original Title", TimeSpan.FromMinutes(20), correctedReleaseAtUtc, refreshUtcNow);
        bool becameEligible = episode.TryBecomeEligible(refreshUtcNow);

        Assert.True(becameEligible);
        Assert.Equal(MediaStatus.Pending, episode.Status);
        Assert.Equal(correctedReleaseAtUtc, episode.ReleaseAtUtc);
    }

    [Fact]
    public void UpdateMetadata_WhenCompleted_TitleAndRuntimeUpdate_ButReleaseAtUtcAndStatusDoNotChange()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);
        episode.StartSearching(utcNow);
        episode.StartValidating(utcNow);
        episode.MarkCompleted(utcNow);

        DateTime laterCorrectedDate = ReleaseAtUtc.AddDays(1);
        bool changed = episode.UpdateMetadata("Corrected Title", TimeSpan.FromMinutes(22), laterCorrectedDate, utcNow.AddDays(2));

        Assert.True(changed);
        Assert.Equal("Corrected Title", episode.Title);
        Assert.Equal(TimeSpan.FromMinutes(22), episode.Runtime);
        Assert.Equal(MediaStatus.Completed, episode.Status);
        Assert.Equal(ReleaseAtUtc, episode.ReleaseAtUtc); // unchanged - not Scheduled anymore
    }

    [Fact]
    public void UpdateMetadata_WhenUnavailable_StatusIsPreserved()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);
        episode.StartSearching(utcNow);
        episode.MarkUnavailable(utcNow, utcNow.AddHours(6), "no streams returned");

        episode.UpdateMetadata("Corrected Title", TimeSpan.FromMinutes(20), ReleaseAtUtc, utcNow.AddHours(1));

        Assert.Equal(MediaStatus.Unavailable, episode.Status);
        Assert.Equal("Corrected Title", episode.Title);
    }

    [Fact]
    public void UpdateMetadata_WithIdenticalValues_ReturnsFalseAndDoesNotBumpUpdatedAtUtc()
    {
        DateTime utcNow = ReleaseAtUtc.AddDays(-1);
        Episode episode = CreateScheduledEpisode(utcNow);
        DateTime updatedAtBefore = episode.UpdatedAtUtc;

        bool changed = episode.UpdateMetadata("Original Title", TimeSpan.FromMinutes(20), ReleaseAtUtc, utcNow.AddMinutes(5));

        Assert.False(changed);
        Assert.Equal(updatedAtBefore, episode.UpdatedAtUtc);
    }
}
