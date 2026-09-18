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

    /// <summary>Movie lookups - same safe "no candidates" default as <see cref="Handler"/>.</summary>
    public Func<MovieStreamReference, Result<IReadOnlyList<StreamCandidate>>> MovieHandler { get; set; } =
        _ => Result.Success<IReadOnlyList<StreamCandidate>>([]);

    /// <summary>
    /// When set, used instead of <see cref="MovieHandler"/> - for tests that must hold a
    /// lookup "in flight" (await a signal) while something else happens.
    /// </summary>
    public Func<MovieStreamReference, CancellationToken, Task<Result<IReadOnlyList<StreamCandidate>>>>? MovieHandlerAsync { get; set; }

    public Task<Result<IReadOnlyList<StreamCandidate>>> GetMovieStreamsAsync(
        MovieStreamReference reference, CancellationToken cancellationToken = default) =>
        MovieHandlerAsync is { } asyncHandler
            ? asyncHandler(reference, cancellationToken)
            : Task.FromResult(MovieHandler(reference));
}
