using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Test double for IMediaValidator - keeps integration tests off a real ffprobe process.
/// Defaults to rejecting everything (mirrors FakeMetadataProvider/FakeStreamProvider's
/// safe-default convention), so tests that need a candidate approved must say so
/// explicitly via Handler.
/// </summary>
public sealed class FakeMediaValidator : IMediaValidator
{
    public Func<StreamCandidate, MediaValidationReference, MediaValidationResult> Handler { get; set; } =
        (_, _) => MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "FakeMediaValidator: no handler configured.");

    public Task<MediaValidationResult> ValidateAsync(
        StreamCandidate candidate, MediaValidationReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Handler(candidate, reference));
}
