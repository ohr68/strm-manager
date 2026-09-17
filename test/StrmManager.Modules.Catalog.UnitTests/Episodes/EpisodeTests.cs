using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.UnitTests.Episodes;

public class EpisodeTests
{
    private static readonly DateTime ReleaseAtUtc = new(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);

    private static Episode CreateScheduledEpisode(DateTime utcNow) =>
        Episode.Schedule(
            seasonId: Guid.NewGuid(),
            externalId: "tt27497393:1:9",
            title: "Stuart Fails to Save the Universe",
            seasonNumber: 1,
            episodeNumber: 9,
            runtime: TimeSpan.FromMinutes(24),
            releaseAtUtc: ReleaseAtUtc,
            utcNow: utcNow);

    [Fact]
    public void Schedule_WhenReleaseIsInTheFuture_StaysScheduled()
    {
        DateTime utcNow = ReleaseAtUtc.AddMinutes(-1);

        Episode episode = CreateScheduledEpisode(utcNow);

        Assert.Equal(MediaStatus.Scheduled, episode.Status);
    }

    [Fact]
    public void TryBecomeEligible_BeforeReleaseAtUtc_ReturnsFalseAndStaysScheduled()
    {
        DateTime utcNow = ReleaseAtUtc.AddMinutes(-1);
        Episode episode = CreateScheduledEpisode(utcNow);

        bool becameEligible = episode.TryBecomeEligible(ReleaseAtUtc.AddSeconds(-1));

        Assert.False(becameEligible);
        Assert.Equal(MediaStatus.Scheduled, episode.Status);
    }

    [Fact]
    public void TryBecomeEligible_AtOrAfterReleaseAtUtc_TransitionsToPending()
    {
        DateTime utcNow = ReleaseAtUtc.AddMinutes(-10);
        Episode episode = CreateScheduledEpisode(utcNow);

        bool becameEligible = episode.TryBecomeEligible(ReleaseAtUtc);

        Assert.True(becameEligible);
        Assert.Equal(MediaStatus.Pending, episode.Status);
    }

    [Fact]
    public void Schedule_WhenReleaseIsAlreadyInThePast_StartsAsPending()
    {
        DateTime utcNow = ReleaseAtUtc.AddDays(1);

        Episode episode = CreateScheduledEpisode(utcNow);

        Assert.Equal(MediaStatus.Pending, episode.Status);
    }

    [Fact]
    public void FullPipeline_PendingToSearchingToValidatingToCompleted_Succeeds()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);

        Assert.True(episode.StartSearching(utcNow).IsSuccess);
        Assert.Equal(MediaStatus.Searching, episode.Status);

        Assert.True(episode.StartValidating(utcNow).IsSuccess);
        Assert.Equal(MediaStatus.Validating, episode.Status);

        Assert.True(episode.MarkCompleted(utcNow).IsSuccess);
        Assert.Equal(MediaStatus.Completed, episode.Status);
        Assert.Equal(1, episode.AttemptCount);
    }

    [Fact]
    public void MarkCompleted_DoesNotAcceptRetryBackToPending()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);
        episode.StartSearching(utcNow);
        episode.StartValidating(utcNow);
        episode.MarkCompleted(utcNow);

        var retryResult = episode.Retry(utcNow);

        Assert.True(retryResult.IsFailure);
        Assert.Equal(MediaStatus.Completed, episode.Status);
    }

    [Fact]
    public void MarkUnavailable_FromCompleted_Fails()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);
        episode.StartSearching(utcNow);
        episode.StartValidating(utcNow);
        episode.MarkCompleted(utcNow);

        var result = episode.MarkUnavailable(utcNow, nextAttemptAtUtc: null, reason: "no sources");

        Assert.True(result.IsFailure);
        Assert.Equal(MediaStatus.Completed, episode.Status);
    }

    [Fact]
    public void Unavailable_And_Error_AreDistinctTerminalStates()
    {
        DateTime utcNow = ReleaseAtUtc;

        Episode unavailableEpisode = CreateScheduledEpisode(utcNow);
        unavailableEpisode.TryBecomeEligible(utcNow);
        unavailableEpisode.StartSearching(utcNow);
        unavailableEpisode.MarkUnavailable(utcNow, utcNow.AddHours(6), "no streams returned");

        Episode errorEpisode = CreateScheduledEpisode(utcNow);
        errorEpisode.TryBecomeEligible(utcNow);
        errorEpisode.StartSearching(utcNow);
        errorEpisode.MarkError(utcNow, "ffprobe timed out");

        Assert.Equal(MediaStatus.Unavailable, unavailableEpisode.Status);
        Assert.Equal(MediaStatus.Error, errorEpisode.Status);
        Assert.NotEqual(unavailableEpisode.Status, errorEpisode.Status);
    }

    [Fact]
    public void Retry_FromUnavailable_TransitionsBackToPending()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);
        episode.StartSearching(utcNow);
        episode.MarkUnavailable(utcNow, utcNow.AddHours(6), "no streams returned");

        var result = episode.Retry(utcNow.AddHours(6));

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Pending, episode.Status);
        Assert.Null(episode.NextAttemptAtUtc);
    }

    [Fact]
    public void Retry_FromError_TransitionsBackToPending()
    {
        DateTime utcNow = ReleaseAtUtc;
        Episode episode = CreateScheduledEpisode(utcNow);
        episode.TryBecomeEligible(utcNow);
        episode.StartSearching(utcNow);
        episode.MarkError(utcNow, "ffprobe timed out");

        var result = episode.Retry(utcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Pending, episode.Status);
    }

    [Fact]
    public void StartSearching_WhenNotPending_Fails()
    {
        DateTime utcNow = ReleaseAtUtc.AddMinutes(-1);
        Episode episode = CreateScheduledEpisode(utcNow);

        var result = episode.StartSearching(utcNow);

        Assert.True(result.IsFailure);
        Assert.Equal(MediaStatus.Scheduled, episode.Status);
    }
}
