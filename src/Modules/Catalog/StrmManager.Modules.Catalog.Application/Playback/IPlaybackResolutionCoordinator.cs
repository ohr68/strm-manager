namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Bounded, single-flight access to <see cref="IPlaybackResolver"/>. Concurrent callers for the same movie share ONE
/// in-progress resolution; a caller arriving after it has finished starts a new one (there is no result caching);
/// resolutions for different movies are bounded by a global concurrency limit. The playback endpoint calls this rather
/// than the resolver.
/// </summary>
public interface IPlaybackResolutionCoordinator
{
    /// <summary>
    /// <paramref name="waiterToken"/> controls only THIS caller's wait: cancelling it makes this call throw
    /// <see cref="OperationCanceledException"/> and leaves the shared resolution, and any other waiter, unaffected.
    /// The shared work is cancelled only by application shutdown or its execution budget.
    /// </summary>
    Task<PlaybackCoordinationResult> ResolveAsync(Guid movieId, CancellationToken waiterToken);
}
