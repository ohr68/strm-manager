using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Operational safeguards for just-in-time playback resolution (ADR-015) - how much provider/ffprobe work may run at
/// once, how long a resolution may wait for its turn, and how long it may run. NOT freshness policy: nothing here
/// keeps a resolved URL or result for later reuse (there is no cache, TTL or reuse window), and no value here decides
/// how "old" a source may be. Every default is valid on its own, so an existing deployment needs no configuration and
/// a missing section cannot fail startup.
/// </summary>
public sealed class PlaybackResolutionOptions
{
    public const string SectionName = "Playback:Resolution";

    /// <summary>
    /// The most underlying resolutions - each a provider lookup plus ffprobe runs - executing at once, across ALL movies.
    /// Callers waiting on an already-running movie do not count. Defaults to 2, the same conservative bound as
    /// SchedulingOptions.MaxConcurrentEpisodeProcessing, because the host also runs Jellyfin and other services.
    /// </summary>
    [Range(1, 20)]
    public int MaxConcurrentResolutions { get; set; } = 2;

    /// <summary>
    /// How long a NEW resolution may wait for one of the execution slots before its callers are told the service is busy.
    /// Kept short on purpose: a player is better served by a prompt "try again" than by an unbounded queue behind slow
    /// probes. Zero means "never queue": no free slot is refused at once.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00")]
    public TimeSpan QueueWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest an admitted resolution may run before it is cancelled and its callers get a generic failure. A guard
    /// that frees the slot from a stuck provider or ffprobe, not a statement about how long a good resolution takes:
    /// it must exceed the provider's own timeout plus at least one ffprobe (30 s by default), or it would cut off
    /// resolutions that were about to succeed. It is a starting point, not a measured value.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
    public TimeSpan ResolutionBudget { get; set; } = TimeSpan.FromSeconds(60);
}
