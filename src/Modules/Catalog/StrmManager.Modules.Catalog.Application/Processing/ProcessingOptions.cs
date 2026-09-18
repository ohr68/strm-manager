using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.Catalog.Application.Processing;

/// <summary>
/// Consumed directly by ProcessEpisodeCommandHandler/RunCatalogMaintenanceCommandHandler
/// (Application layer) rather than by an Infrastructure implementation - these are
/// business-rule numbers about the Episode processing pipeline itself (how long to wait
/// before retrying, how long a stuck run is considered abandoned), not the Scheduling
/// module's own operational polling cadence (see ADR-012/ADR-013 - Scheduling's own
/// SchedulingOptions holds only "how often does the poller wake up", nothing about
/// episode-processing timing policy).
/// </summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan UnavailableRetryDelay { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How soon a retryable technical Error (e.g. a stream provider outage) is retried - deliberately shorter than UnavailableRetryDelay, which reflects "we looked thoroughly and found nothing to show" rather than a transient failure.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "1.00:00:00")]
    public TimeSpan RetryableErrorDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an episode may sit in Searching/Validating before the maintenance worker treats the run as abandoned (crash/restart/cancelled shutdown) and recovers it back to Pending. See ADR-013.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan StaleProcessingThreshold { get; set; } = TimeSpan.FromMinutes(15);
}
