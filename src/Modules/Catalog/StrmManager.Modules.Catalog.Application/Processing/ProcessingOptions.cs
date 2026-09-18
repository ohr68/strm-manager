using System.ComponentModel.DataAnnotations;

namespace StrmManager.Modules.Catalog.Application.Processing;

/// <summary>
/// Consumed directly by ProcessEpisodeCommandHandler (Application layer) rather than by
/// an Infrastructure implementation - these are business-rule numbers (how long to wait
/// before an Unavailable episode is worth trying again), not integration details. No
/// automatic retry exists yet (see ADR-011/Phase 4) - this only computes the
/// NextAttemptAtUtc value that Episode.MarkUnavailable already needed a value for.
/// </summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    [Range(typeof(TimeSpan), "00:01:00", "30.00:00:00")]
    public TimeSpan UnavailableRetryDelay { get; set; } = TimeSpan.FromHours(6);
}
