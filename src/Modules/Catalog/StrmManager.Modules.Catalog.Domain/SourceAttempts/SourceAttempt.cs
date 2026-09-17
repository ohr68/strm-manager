using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.SourceAttempts;

/// <summary>
/// Records the outcome of validating one candidate source against an episode or movie.
/// Never stores the full source URL - only what is needed to diagnose "why wasn't this created".
/// </summary>
public sealed class SourceAttempt : Entity
{
    private SourceAttempt()
    {
    }

    public Guid Id { get; private set; }

    public Guid? EpisodeId { get; private set; }

    public Guid? MovieId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string SourceName { get; private set; } = string.Empty;

    public DateTime AttemptedAtUtc { get; private set; }

    public SourceAttemptResult Result { get; private set; }

    public TimeSpan? Duration { get; private set; }

    public TimeSpan? ExpectedDuration { get; private set; }

    public double? DifferencePercentage { get; private set; }

    public string? VideoCodec { get; private set; }

    public string? AudioCodec { get; private set; }

    public string? FailureReason { get; private set; }

    public static SourceAttempt ForEpisode(
        Guid episodeId,
        string provider,
        string sourceName,
        DateTime utcNow,
        SourceAttemptResult result,
        TimeSpan? duration,
        TimeSpan? expectedDuration,
        double? differencePercentage,
        string? videoCodec,
        string? audioCodec,
        string? failureReason) =>
        new()
        {
            Id = Guid.NewGuid(),
            EpisodeId = episodeId,
            Provider = provider,
            SourceName = sourceName,
            AttemptedAtUtc = utcNow,
            Result = result,
            Duration = duration,
            ExpectedDuration = expectedDuration,
            DifferencePercentage = differencePercentage,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            FailureReason = failureReason,
        };

    public static SourceAttempt ForMovie(
        Guid movieId,
        string provider,
        string sourceName,
        DateTime utcNow,
        SourceAttemptResult result,
        TimeSpan? duration,
        TimeSpan? expectedDuration,
        double? differencePercentage,
        string? videoCodec,
        string? audioCodec,
        string? failureReason) =>
        new()
        {
            Id = Guid.NewGuid(),
            MovieId = movieId,
            Provider = provider,
            SourceName = sourceName,
            AttemptedAtUtc = utcNow,
            Result = result,
            Duration = duration,
            ExpectedDuration = expectedDuration,
            DifferencePercentage = differencePercentage,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            FailureReason = failureReason,
        };
}
