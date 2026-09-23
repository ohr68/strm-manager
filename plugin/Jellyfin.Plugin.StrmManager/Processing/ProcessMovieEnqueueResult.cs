namespace Jellyfin.Plugin.StrmManager.Processing;

/// <summary>The outcome of <see cref="IProcessMovieQueue.TryEnqueue"/>.</summary>
public enum ProcessMovieEnqueueResult
{
    /// <summary>The movie id was accepted into the queue.</summary>
    Accepted,

    /// <summary>This movie id is already queued or already being processed - not re-added.</summary>
    AlreadyQueued,

    /// <summary>The queue is at capacity right now.</summary>
    Full,
}
