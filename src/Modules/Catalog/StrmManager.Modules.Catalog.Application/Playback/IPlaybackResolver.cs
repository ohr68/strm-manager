namespace StrmManager.Modules.Catalog.Application.Playback;

public interface IPlaybackResolver
{
    /// <summary>
    /// Resolves a movie, by its internal id, to a freshly discovered and validated source. Read-only: it never
    /// changes the movie, records attempts, saves, or writes a .strm. Expected outcomes are values of
    /// <see cref="PlaybackResolutionResult"/>; only cancellation (and genuine bugs) surface as exceptions.
    /// </summary>
    Task<PlaybackResolutionResult> ResolveAsync(Guid movieId, CancellationToken cancellationToken);
}
