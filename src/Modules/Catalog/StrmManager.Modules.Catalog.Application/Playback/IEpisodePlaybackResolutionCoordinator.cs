namespace StrmManager.Modules.Catalog.Application.Playback;

public interface IEpisodePlaybackResolutionCoordinator
{
    Task<PlaybackCoordinationResult> ResolveAsync(
        Guid episodeId,
        CancellationToken waiterToken);
}
