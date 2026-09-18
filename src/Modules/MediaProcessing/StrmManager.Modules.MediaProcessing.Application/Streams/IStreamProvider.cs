using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.MediaProcessing.Application.Streams;

public interface IStreamProvider
{
    /// <summary>
    /// An empty (but successful) list is a normal outcome - the provider simply has no
    /// candidates for this episode right now. Only genuine technical failures (HTTP
    /// failure after resilience is exhausted, malformed response, timeout) are a
    /// Result.Failure - see StreamProviderErrors.
    /// </summary>
    Task<Result<IReadOnlyList<StreamCandidate>>> GetEpisodeStreamsAsync(
        EpisodeStreamReference reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same contract as <see cref="GetEpisodeStreamsAsync"/>, for a movie identified by its
    /// IMDb id: an empty successful list means "no candidates right now", and only genuine
    /// technical failures are a Result.Failure.
    /// </summary>
    Task<Result<IReadOnlyList<StreamCandidate>>> GetMovieStreamsAsync(
        MovieStreamReference reference,
        CancellationToken cancellationToken = default);
}
