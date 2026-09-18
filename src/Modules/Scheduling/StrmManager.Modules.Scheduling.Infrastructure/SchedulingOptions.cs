using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.Scheduling.Infrastructure;

/// <summary>
/// Purely operational: how often the workers wake up and how much work they take per
/// tick. Business-rule timing (retry delays, stale threshold, metadata refresh cadence)
/// deliberately lives with the domain that owns it instead - Catalog.Application's
/// ProcessingOptions/MetadataRefreshOptions - not here. See ADR-012.
/// </summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>Master switch - false disables both workers without affecting the API, manual processing, or health checks. See ADR-012.</summary>
    public bool Enabled { get; set; } = true;

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);

    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan ProcessingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Bounds simultaneous ffprobe/FrostStream activity - the host also runs Jellyfin and other services, so this defaults conservatively.</summary>
    [Range(1, 20)]
    public int MaxConcurrentEpisodeProcessing { get; set; } = 2;

    /// <summary>How many Pending episodes EpisodeProcessingWorker claims per tick.</summary>
    [Range(1, 200)]
    public int BatchSize { get; set; } = 10;
}
