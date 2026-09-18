using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Domain.Movies;

public sealed class Movie : Entity
{
    private Movie()
    {
    }

    public Guid Id { get; private set; }

    public ExternalIds ExternalIds { get; private set; } = null!;

    public string Title { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public TimeSpan? Runtime { get; private set; }

    public DateTime ReleaseAtUtc { get; private set; }

    public MediaStatus Status { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Movie Schedule(
        ExternalIds externalIds,
        string title,
        int year,
        TimeSpan? runtime,
        DateTime releaseAtUtc,
        DateTime utcNow)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            ExternalIds = externalIds,
            Title = title,
            Year = year,
            Runtime = runtime,
            ReleaseAtUtc = releaseAtUtc,
            Status = MediaStatus.Scheduled,
            AttemptCount = 0,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

        movie.TryBecomeEligible(utcNow);

        return movie;
    }

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
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Searching));
        }

        Status = MediaStatus.Searching;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result StartValidating(DateTime utcNow)
    {
        if (Status != MediaStatus.Searching)
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Validating));
        }

        Status = MediaStatus.Validating;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result MarkCompleted(DateTime utcNow)
    {
        if (Status is not (MediaStatus.Validating or MediaStatus.Searching))
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Completed));
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
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Unavailable));
        }

        Status = MediaStatus.Unavailable;
        LastAttemptAtUtc = utcNow;
        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = reason;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// <paramref name="nextAttemptAtUtc"/> is the sole retryability signal - a non-null
    /// value means this failure is eligible for automatic retry, null means it stays Error
    /// until a manual retry. Always assigned explicitly (never left as whatever the loaded
    /// entity already had) so a stale value from a prior Unavailable/Error cycle can never
    /// leak through and make a non-retryable failure look retryable. Same contract as
    /// Episode.MarkError - see ADR-013.
    /// </summary>
    public Result MarkError(DateTime utcNow, string error, DateTime? nextAttemptAtUtc = null)
    {
        if (Status is MediaStatus.Completed)
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Error));
        }

        Status = MediaStatus.Error;
        LastAttemptAtUtc = utcNow;
        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = error;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Recovers a movie left in Searching/Validating by an interrupted processing run
    /// (crash, restart, cancelled shutdown) back to Pending. Deliberately not the same as
    /// MarkUnavailable/MarkError - an interrupted run reached no outcome, so AttemptCount,
    /// LastAttemptAtUtc and LastError are left untouched. Mirrors
    /// Episode.RecoverInterruptedProcessing - see ADR-013.
    /// </summary>
    public Result RecoverInterruptedProcessing(DateTime utcNow)
    {
        if (Status is not (MediaStatus.Searching or MediaStatus.Validating))
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Pending));
        }

        Status = MediaStatus.Pending;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    public Result Retry(DateTime utcNow)
    {
        if (Status is not (MediaStatus.Unavailable or MediaStatus.Error))
        {
            return Result.Failure(MediaErrors.InvalidTransition(nameof(Movie), Status, MediaStatus.Pending));
        }

        Status = MediaStatus.Pending;
        NextAttemptAtUtc = null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }
}
