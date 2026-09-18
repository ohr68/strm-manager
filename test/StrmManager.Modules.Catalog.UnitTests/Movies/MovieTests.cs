using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.UnitTests.Movies;

public class MovieTests
{
    private static readonly DateTime ReleaseAtUtc = new(2026, 9, 18, 5, 0, 0, DateTimeKind.Utc);

    private static Movie CreateScheduledMovie(DateTime utcNow) =>
        Movie.Schedule(
            externalIds: new ExternalIds("tt0111161", null, null),
            title: "The Shawshank Redemption",
            year: 1994,
            runtime: TimeSpan.FromMinutes(142),
            releaseAtUtc: ReleaseAtUtc,
            utcNow: utcNow);

    private static Movie CreateSearchingMovie(DateTime utcNow)
    {
        Movie movie = CreateScheduledMovie(utcNow);
        movie.TryBecomeEligible(utcNow);
        movie.StartSearching(utcNow);
        return movie;
    }

    private static Movie CreateCompletedMovie(DateTime utcNow)
    {
        Movie movie = CreateSearchingMovie(utcNow);
        movie.StartValidating(utcNow);
        movie.MarkCompleted(utcNow);
        return movie;
    }

    // --- Schedule / TryBecomeEligible ---

    [Fact]
    public void Schedule_WhenReleaseIsInThePast_StartsAsPending()
    {
        Movie movie = CreateScheduledMovie(ReleaseAtUtc.AddDays(1));

        Assert.Equal(MediaStatus.Pending, movie.Status);
    }

    [Fact]
    public void Schedule_WhenReleaseIsExactlyNow_StartsAsPending()
    {
        Movie movie = CreateScheduledMovie(ReleaseAtUtc);

        Assert.Equal(MediaStatus.Pending, movie.Status);
    }

    [Fact]
    public void Schedule_WhenReleaseIsInTheFuture_StaysScheduled()
    {
        Movie movie = CreateScheduledMovie(ReleaseAtUtc.AddMinutes(-1));

        Assert.Equal(MediaStatus.Scheduled, movie.Status);
        Assert.Equal(0, movie.AttemptCount);
    }

    [Fact]
    public void TryBecomeEligible_BeforeReleaseAtUtc_ReturnsFalseAndStaysScheduled()
    {
        Movie movie = CreateScheduledMovie(ReleaseAtUtc.AddMinutes(-10));

        bool becameEligible = movie.TryBecomeEligible(ReleaseAtUtc.AddSeconds(-1));

        Assert.False(becameEligible);
        Assert.Equal(MediaStatus.Scheduled, movie.Status);
    }

    [Fact]
    public void TryBecomeEligible_AtReleaseAtUtc_TransitionsToPending()
    {
        Movie movie = CreateScheduledMovie(ReleaseAtUtc.AddMinutes(-10));

        bool becameEligible = movie.TryBecomeEligible(ReleaseAtUtc);

        Assert.True(becameEligible);
        Assert.Equal(MediaStatus.Pending, movie.Status);
        Assert.Equal(ReleaseAtUtc, movie.UpdatedAtUtc);
    }

    [Fact]
    public void TryBecomeEligible_WhenNotScheduled_ReturnsFalseAndDoesNotTouchTheMovie()
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        DateTime updatedAtUtc = movie.UpdatedAtUtc;

        bool becameEligible = movie.TryBecomeEligible(ReleaseAtUtc.AddDays(1));

        Assert.False(becameEligible);
        Assert.Equal(MediaStatus.Searching, movie.Status);
        Assert.Equal(updatedAtUtc, movie.UpdatedAtUtc);
    }

    // --- StartSearching / StartValidating ---

    [Fact]
    public void StartSearching_FromPending_Succeeds()
    {
        Movie movie = CreateScheduledMovie(ReleaseAtUtc);
        DateTime claimedAtUtc = ReleaseAtUtc.AddMinutes(1);

        var result = movie.StartSearching(claimedAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Searching, movie.Status);
        Assert.Equal(claimedAtUtc, movie.UpdatedAtUtc); // the concurrency token must move on the claim
    }

    [Theory]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Searching)]
    [InlineData(MediaStatus.Validating)]
    [InlineData(MediaStatus.Completed)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public void StartSearching_FromAnythingButPending_FailsAndChangesNothing(MediaStatus status)
    {
        Movie movie = MovieInStatus(status);
        DateTime updatedAtUtc = movie.UpdatedAtUtc;

        var result = movie.StartSearching(ReleaseAtUtc.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(status, movie.Status);
        Assert.Equal(updatedAtUtc, movie.UpdatedAtUtc);
    }

    [Fact]
    public void StartValidating_FromSearching_Succeeds()
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);

        var result = movie.StartValidating(ReleaseAtUtc.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Validating, movie.Status);
    }

    [Theory]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Validating)]
    [InlineData(MediaStatus.Completed)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public void StartValidating_FromAnythingButSearching_Fails(MediaStatus status)
    {
        Movie movie = MovieInStatus(status);

        var result = movie.StartValidating(ReleaseAtUtc.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(status, movie.Status);
    }

    // --- MarkCompleted ---

    [Theory]
    [InlineData(false)] // Searching
    [InlineData(true)] // Validating
    public void MarkCompleted_FromSearchingOrValidating_CompletesAndIncrementsAttemptCount(bool alsoStartValidating)
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        if (alsoStartValidating)
        {
            movie.StartValidating(ReleaseAtUtc);
        }

        DateTime completedAtUtc = ReleaseAtUtc.AddMinutes(5);
        var result = movie.MarkCompleted(completedAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Completed, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Equal(completedAtUtc, movie.LastAttemptAtUtc);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Null(movie.LastError);
    }

    [Theory]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public void MarkCompleted_FromAnythingButSearchingOrValidating_Fails(MediaStatus status)
    {
        Movie movie = MovieInStatus(status);
        int attemptCount = movie.AttemptCount;

        var result = movie.MarkCompleted(ReleaseAtUtc.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(status, movie.Status);
        Assert.Equal(attemptCount, movie.AttemptCount);
    }

    // --- MarkUnavailable ---

    [Theory]
    [InlineData(false)] // Searching
    [InlineData(true)] // Validating
    public void MarkUnavailable_FromSearchingOrValidating_RecordsTheAttempt(bool alsoStartValidating)
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        if (alsoStartValidating)
        {
            movie.StartValidating(ReleaseAtUtc);
        }

        DateTime attemptedAtUtc = ReleaseAtUtc.AddMinutes(5);
        DateTime nextAttemptAtUtc = attemptedAtUtc.AddHours(6);
        var result = movie.MarkUnavailable(attemptedAtUtc, nextAttemptAtUtc, "no candidate passed validation");

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Unavailable, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Equal(attemptedAtUtc, movie.LastAttemptAtUtc);
        Assert.Equal(nextAttemptAtUtc, movie.NextAttemptAtUtc);
        Assert.Equal("no candidate passed validation", movie.LastError);
    }

    [Theory]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Completed)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public void MarkUnavailable_FromAnythingButSearchingOrValidating_Fails(MediaStatus status)
    {
        Movie movie = MovieInStatus(status);
        int attemptCount = movie.AttemptCount;

        var result = movie.MarkUnavailable(ReleaseAtUtc.AddDays(30), nextAttemptAtUtc: null, reason: "x");

        Assert.True(result.IsFailure);
        Assert.Equal(status, movie.Status);
        Assert.Equal(attemptCount, movie.AttemptCount);
    }

    // --- MarkError (pre-existing two-argument form) ---

    [Fact]
    public void MarkError_AlwaysOverwritesAStalePriorNextAttemptAtUtc()
    {
        // A previous Unavailable cycle leaves NextAttemptAtUtc set, and Retry clears it -
        // but a Movie can also reach MarkError with a value already present (e.g. an
        // Unavailable movie whose retry signal was never cleared). MarkError must never let
        // that leak through and make a non-retryable failure look retryable to a future
        // GetRetryableAsync sweep.
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        movie.MarkUnavailable(ReleaseAtUtc, ReleaseAtUtc.AddHours(6), "no streams returned");
        Assert.NotNull(movie.NextAttemptAtUtc);

        // Not Retry(): drive the error straight from the Unavailable state, the state that
        // still carries the stale retry signal.
        movie.MarkError(ReleaseAtUtc.AddHours(6), "failed to write .strm file");

        Assert.Equal(MediaStatus.Error, movie.Status);
        Assert.Null(movie.NextAttemptAtUtc);
    }

    // --- MarkError retry signal ---

    [Fact]
    public void MarkError_WithNextAttemptAtUtc_TransitionsToErrorAndRecordsTheRetrySignal()
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        DateTime attemptedAtUtc = ReleaseAtUtc.AddMinutes(5);
        DateTime nextAttemptAtUtc = attemptedAtUtc.AddMinutes(15);

        var result = movie.MarkError(attemptedAtUtc, "stream provider unavailable", nextAttemptAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Error, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Equal(attemptedAtUtc, movie.LastAttemptAtUtc);
        Assert.Equal(nextAttemptAtUtc, movie.NextAttemptAtUtc);
        Assert.Equal("stream provider unavailable", movie.LastError);
    }

    [Fact]
    public void MarkError_WithoutNextAttemptAtUtc_TransitionsToErrorAndIsNotAutomaticallyRetryable()
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);

        var result = movie.MarkError(ReleaseAtUtc.AddMinutes(5), "failed to write .strm file");

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Error, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Equal("failed to write .strm file", movie.LastError);
    }

    [Fact]
    public void MarkError_WithoutNextAttemptAtUtc_ClearsARetrySignalLeftByAnEarlierError()
    {
        // Error (retryable) -> Error (non-retryable) without an intervening Retry: the
        // second, non-retryable failure must not inherit the first one's schedule.
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        movie.MarkError(ReleaseAtUtc, "stream provider unavailable", ReleaseAtUtc.AddMinutes(15));
        Assert.NotNull(movie.NextAttemptAtUtc);

        movie.MarkError(ReleaseAtUtc.AddMinutes(20), "failed to write .strm file");

        Assert.Equal(MediaStatus.Error, movie.Status);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Equal(2, movie.AttemptCount);
    }

    [Fact]
    public void MarkError_WithNextAttemptAtUtc_IsClearedByRetry()
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        DateTime nextAttemptAtUtc = ReleaseAtUtc.AddMinutes(15);
        movie.MarkError(ReleaseAtUtc, "stream provider unavailable", nextAttemptAtUtc);

        var retryResult = movie.Retry(nextAttemptAtUtc);

        Assert.True(retryResult.IsSuccess);
        Assert.Equal(MediaStatus.Pending, movie.Status);
        Assert.Null(movie.NextAttemptAtUtc);
    }

    // --- RecoverInterruptedProcessing ---

    [Theory]
    [InlineData(false)] // Searching
    [InlineData(true)] // Validating
    public void RecoverInterruptedProcessing_FromSearchingOrValidating_ReturnsToPendingWithoutTouchingTheAttemptRecord(bool alsoStartValidating)
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        if (alsoStartValidating)
        {
            movie.StartValidating(ReleaseAtUtc);
        }

        DateTime recoveryUtcNow = ReleaseAtUtc.AddMinutes(20);
        var result = movie.RecoverInterruptedProcessing(recoveryUtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Pending, movie.Status);
        Assert.Equal(0, movie.AttemptCount); // an interrupted run reached no outcome
        Assert.Null(movie.LastAttemptAtUtc);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Equal(recoveryUtcNow, movie.UpdatedAtUtc);
    }

    [Fact]
    public void RecoverInterruptedProcessing_PreservesAttemptCountAndLastErrorFromEarlierAttempts()
    {
        // Unavailable once (AttemptCount 1, LastError set) -> Retry -> claimed again ->
        // process dies mid-run. Recovery must not reset or bump either.
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        movie.MarkUnavailable(ReleaseAtUtc, ReleaseAtUtc.AddHours(6), "no streams returned");
        movie.Retry(ReleaseAtUtc.AddHours(6));
        movie.StartSearching(ReleaseAtUtc.AddHours(6));

        DateTime lastAttemptAtUtc = movie.LastAttemptAtUtc!.Value;
        DateTime recoveryUtcNow = ReleaseAtUtc.AddHours(7);
        var result = movie.RecoverInterruptedProcessing(recoveryUtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Pending, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Equal("no streams returned", movie.LastError);
        Assert.Equal(lastAttemptAtUtc, movie.LastAttemptAtUtc);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Equal(recoveryUtcNow, movie.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Completed)]
    [InlineData(MediaStatus.Unavailable)]
    [InlineData(MediaStatus.Error)]
    public void RecoverInterruptedProcessing_FromAnythingButSearchingOrValidating_FailsAndChangesNothing(MediaStatus status)
    {
        Movie movie = MovieInStatus(status);
        DateTime updatedAtUtc = movie.UpdatedAtUtc;
        int attemptCount = movie.AttemptCount;
        DateTime? nextAttemptAtUtc = movie.NextAttemptAtUtc;

        var result = movie.RecoverInterruptedProcessing(ReleaseAtUtc.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(status, movie.Status);
        Assert.Equal(updatedAtUtc, movie.UpdatedAtUtc);
        Assert.Equal(attemptCount, movie.AttemptCount);
        Assert.Equal(nextAttemptAtUtc, movie.NextAttemptAtUtc);
    }

    // --- Retry ---

    [Theory]
    [InlineData(true)] // Unavailable
    [InlineData(false)] // Error
    public void Retry_FromUnavailableOrError_ReturnsToPendingClearsNextAttemptAndPreservesAttemptCount(bool fromUnavailable)
    {
        Movie movie = CreateSearchingMovie(ReleaseAtUtc);
        if (fromUnavailable)
        {
            movie.MarkUnavailable(ReleaseAtUtc, ReleaseAtUtc.AddHours(6), "no streams returned");
        }
        else
        {
            movie.MarkError(ReleaseAtUtc, "ffprobe timed out");
        }

        DateTime retriedAtUtc = ReleaseAtUtc.AddHours(1);
        var result = movie.Retry(retriedAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaStatus.Pending, movie.Status);
        Assert.Null(movie.NextAttemptAtUtc);
        Assert.Equal(1, movie.AttemptCount);
        Assert.Equal(retriedAtUtc, movie.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(MediaStatus.Scheduled)]
    [InlineData(MediaStatus.Pending)]
    [InlineData(MediaStatus.Searching)]
    [InlineData(MediaStatus.Validating)]
    [InlineData(MediaStatus.Completed)]
    public void Retry_FromAnythingButUnavailableOrError_Fails(MediaStatus status)
    {
        Movie movie = MovieInStatus(status);

        var result = movie.Retry(ReleaseAtUtc.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(status, movie.Status);
    }

    // --- Completed is terminal ---

    [Fact]
    public void Completed_IsTerminal_NoTransitionLeavesIt()
    {
        Movie movie = CreateCompletedMovie(ReleaseAtUtc);
        DateTime later = ReleaseAtUtc.AddDays(1);

        Assert.True(movie.StartSearching(later).IsFailure);
        Assert.True(movie.StartValidating(later).IsFailure);
        Assert.True(movie.MarkCompleted(later).IsFailure);
        Assert.True(movie.MarkUnavailable(later, null, "x").IsFailure);
        Assert.True(movie.MarkError(later, "x").IsFailure);
        Assert.True(movie.Retry(later).IsFailure);
        Assert.True(movie.RecoverInterruptedProcessing(later).IsFailure);
        Assert.False(movie.TryBecomeEligible(later));

        Assert.Equal(MediaStatus.Completed, movie.Status);
        Assert.Equal(1, movie.AttemptCount);
    }

    private static Movie MovieInStatus(MediaStatus status)
    {
        DateTime utcNow = ReleaseAtUtc;
        Movie movie = CreateScheduledMovie(utcNow.AddMinutes(-1)); // Scheduled

        switch (status)
        {
            case MediaStatus.Scheduled:
                return movie;
            case MediaStatus.Pending:
                movie.TryBecomeEligible(utcNow);
                return movie;
            case MediaStatus.Searching:
                movie.TryBecomeEligible(utcNow);
                movie.StartSearching(utcNow);
                return movie;
            case MediaStatus.Validating:
                movie.TryBecomeEligible(utcNow);
                movie.StartSearching(utcNow);
                movie.StartValidating(utcNow);
                return movie;
            case MediaStatus.Completed:
                return CreateCompletedMovie(utcNow);
            case MediaStatus.Unavailable:
                movie.TryBecomeEligible(utcNow);
                movie.StartSearching(utcNow);
                movie.MarkUnavailable(utcNow, utcNow.AddHours(6), "no streams returned");
                return movie;
            case MediaStatus.Error:
                movie.TryBecomeEligible(utcNow);
                movie.StartSearching(utcNow);
                movie.MarkError(utcNow, "ffprobe timed out");
                return movie;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }
}
