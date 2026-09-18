using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Domain.Episodes;

public sealed class Episode : Entity
{
    private Episode()
    {
    }

    public Guid Id { get; private set; }

    public Guid SeasonId { get; private set; }

    public string ExternalId { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public int SeasonNumber { get; private set; }

    public int EpisodeNumber { get; private set; }

    public TimeSpan? Runtime { get; private set; }

    public DateTime ReleaseAtUtc { get; private set; }

    public MediaStatus Status { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Episode Schedule(
        Guid seasonId,
        string externalId,
        string title,
        int seasonNumber,
        int episodeNumber,
        TimeSpan? runtime,
        DateTime releaseAtUtc,
        DateTime utcNow)
    {
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            SeasonId = seasonId,
            ExternalId = externalId,
            Title = title,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            Runtime = runtime,
            ReleaseAtUtc = releaseAtUtc,
            Status = MediaStatus.Scheduled,
            AttemptCount = 0,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

        episode.TryBecomeEligible(utcNow);

        return episode;
    }

    /// <summary>
    /// Transitions Scheduled -> Pending once <see cref="ReleaseAtUtc"/> has passed.
    /// Returns false (no-op) when the episode is not Scheduled or not yet released.
    /// </summary>
    public bool TryBecomeEligible(DateTime utcNow)
    {
        if (Status != MediaStatus.Scheduled || ReleaseAtUtc > utcNow)
        {
            return false;
        }

        Status = MediaStatus.Pending;
        UpdatedAtUtc = utcNow;
        return true;
    }

    public Result StartSearching(DateTime utcNow)
    {
        if (Status != MediaStatus.Pending)
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Episode), Status, MediaStatus.Searching));
        }

        Status = MediaStatus.Searching;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result StartValidating(DateTime utcNow)
    {
        if (Status != MediaStatus.Searching)
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Episode), Status, MediaStatus.Validating));
        }

        Status = MediaStatus.Validating;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkCompleted(DateTime utcNow)
    {
        if (Status is not (MediaStatus.Validating or MediaStatus.Searching))
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Episode), Status, MediaStatus.Completed));
        }

        Status = MediaStatus.Completed;
        LastAttemptAtUtc = utcNow;
        AttemptCount++;
        NextAttemptAtUtc = null;
        LastError = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkUnavailable(DateTime utcNow, DateTime? nextAttemptAtUtc, string? reason)
    {
        if (Status is not (MediaStatus.Searching or MediaStatus.Validating))
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Episode), Status, MediaStatus.Unavailable));
        }

        Status = MediaStatus.Unavailable;
        LastAttemptAtUtc = utcNow;
        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = reason;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkError(DateTime utcNow, string error)
    {
        if (Status is MediaStatus.Completed)
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Episode), Status, MediaStatus.Error));
        }

        Status = MediaStatus.Error;
        LastAttemptAtUtc = utcNow;
        AttemptCount++;
        LastError = error;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Applies a metadata correction (title/runtime always; ReleaseAtUtc only while
    /// still Scheduled - once processing has started, the release timestamp is treated
    /// as historical rather than something a provider refresh should keep rewriting).
    /// Does not touch Status - callers should follow up with TryBecomeEligible if a
    /// release date correction may have just made a Scheduled episode due.
    /// </summary>
    /// <returns>true if anything actually changed.</returns>
    public bool UpdateMetadata(string title, TimeSpan? runtime, DateTime releaseAtUtc, DateTime utcNow)
    {
        bool changed = false;

        if (Title != title)
        {
            Title = title;
            changed = true;
        }

        if (Runtime != runtime)
        {
            Runtime = runtime;
            changed = true;
        }

        if (Status == MediaStatus.Scheduled && ReleaseAtUtc != releaseAtUtc)
        {
            ReleaseAtUtc = releaseAtUtc;
            changed = true;
        }

        if (changed)
        {
            UpdatedAtUtc = utcNow;
        }

        return changed;
    }

    public Result Retry(DateTime utcNow)
    {
        if (Status is not (MediaStatus.Unavailable or MediaStatus.Error))
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Episode), Status, MediaStatus.Pending));
        }

        Status = MediaStatus.Pending;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }
}
