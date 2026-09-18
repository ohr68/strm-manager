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
}
