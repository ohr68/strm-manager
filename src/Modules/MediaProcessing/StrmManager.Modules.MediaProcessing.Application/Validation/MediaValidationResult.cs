using StrmManager.Modules.Catalog.Domain.SourceAttempts;

namespace StrmManager.Modules.MediaProcessing.Application.Validation;

/// <summary>
/// Reuses Catalog.Domain's SourceAttemptResult rather than defining a competing
/// enum - MediaProcessing.Application depends on Catalog.Domain (the innermost,
/// dependency-free layer) for this one type, which does not create a cycle (the reverse
/// - Catalog.Domain depending on MediaProcessing - never happens, and is enforced by
/// architecture tests).
/// </summary>
public sealed record MediaValidationResult(
    bool Approved,
    SourceAttemptResult Result,
    TimeSpan? Duration,
    TimeSpan? ExpectedDuration,
    double? DifferencePercentage,
    string? VideoCodec,
    string? AudioCodec,
    string? FailureReason)
{
    public static MediaValidationResult ForApproval(
        TimeSpan duration,
        TimeSpan? expectedDuration,
        double? differencePercentage,
        string? videoCodec,
        string? audioCodec) =>
        new(true, SourceAttemptResult.Approved, duration, expectedDuration, differencePercentage, videoCodec, audioCodec, null);

    public static MediaValidationResult ForRejection(
        SourceAttemptResult result,
        string failureReason,
        TimeSpan? duration = null,
        TimeSpan? expectedDuration = null,
        double? differencePercentage = null,
        string? videoCodec = null,
        string? audioCodec = null) =>
        new(false, result, duration, expectedDuration, differencePercentage, videoCodec, audioCodec, failureReason);
}
