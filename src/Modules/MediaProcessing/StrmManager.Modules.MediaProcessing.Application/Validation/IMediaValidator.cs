using StrmManager.Modules.MediaProcessing.Application.Streams;

namespace StrmManager.Modules.MediaProcessing.Application.Validation;

public interface IMediaValidator
{
    /// <summary>
    /// Always produces a diagnosable outcome (approved or a specific rejection reason) -
    /// never throws for an expected "this candidate is bad" scenario. A validator
    /// implementation crashing unexpectedly is a genuine bug, not modeled here.
    /// </summary>
    Task<MediaValidationResult> ValidateAsync(
        StreamCandidate candidate,
        MediaValidationReference reference,
        CancellationToken cancellationToken = default);
}
