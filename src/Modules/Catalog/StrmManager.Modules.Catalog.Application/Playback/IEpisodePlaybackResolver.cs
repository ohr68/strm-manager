namespace StrmManager.Modules.Catalog.Application.Playback;

public interface IEpisodePlaybackResolver
{
    /// <summary>
    /// Resolves an episode, by its internal id, to a freshly discovered and
    /// validated source.
    ///
    /// Read-only: it never changes the episode, records attempts, saves,
    /// or writes a .strm.
    /// </summary>
    Task<PlaybackResolutionResult> ResolveAsync(
        Guid episodeId,
        CancellationToken cancellationToken);
}
