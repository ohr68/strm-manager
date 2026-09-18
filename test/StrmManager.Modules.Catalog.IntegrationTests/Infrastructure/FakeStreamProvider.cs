using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.Streams;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

/// <summary>
/// Test double for IStreamProvider - keeps integration tests off live FrostStream.
/// Defaults to "no candidates found", the same safe/deterministic default pattern as
/// FakeMetadataProvider, so tests that don't care about processing get a harmless
/// no-op. Tests that need specific candidates set Handler explicitly.
/// </summary>
public sealed class FakeStreamProvider : IStreamProvider
{
    public Func<EpisodeStreamReference, Result<IReadOnlyList<StreamCandidate>>> Handler { get; set; } =
        _ => Result.Success<IReadOnlyList<StreamCandidate>>([]);

    public Task<Result<IReadOnlyList<StreamCandidate>>> GetEpisodeStreamsAsync(
        EpisodeStreamReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(Handler(reference));
}
