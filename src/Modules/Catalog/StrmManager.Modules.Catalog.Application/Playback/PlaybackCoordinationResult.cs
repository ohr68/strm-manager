namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Why a coordinated resolution produced no <see cref="PlaybackResolutionResult"/>. Stable and free of exception,
/// provider or ffprobe text; a later HTTP layer can map every value to one generic failure.
/// </summary>
public enum PlaybackCoordinationFailure
{
    /// <summary>The resolution ran longer than the configured execution budget and was cancelled.</summary>
    BudgetExceeded,

    /// <summary>The application is shutting down; the shared work was cancelled (or never started).</summary>
    ShuttingDown,

    /// <summary>The resolution threw unexpectedly. The exception is deliberately not carried - its text may hold a URL.</summary>
    Faulted,
}

/// <summary>
/// The outcome of a coordinated (single-flight, bounded) resolution: a closed set of values, never an exception for an
/// expected outcome. Every waiter of one shared resolution receives the same value.
/// </summary>
public abstract record PlaybackCoordinationResult
{
    // Closed: only the nested cases below can derive from this type.
    private PlaybackCoordinationResult()
    {
    }

    /// <summary>
    /// The underlying resolver ran to completion; <see cref="Resolution"/> is its result, passed through unchanged (a
    /// Resolved one still carries its URL only inside a <see cref="PlaybackLocation"/>).
    /// </summary>
    public sealed record Completed(PlaybackResolutionResult Resolution) : PlaybackCoordinationResult;

    /// <summary>No execution slot became free within the queue wait; the resolver was not called.</summary>
    public sealed record Busy : PlaybackCoordinationResult;

    public sealed record Failed(PlaybackCoordinationFailure Reason) : PlaybackCoordinationResult;
}
