namespace StrmManager.Modules.Catalog.Application.Status.GetOperationalStatus;

/// <summary>
/// Version is the assembly's own informational version (whatever the SDK's default
/// versioning already produces - no versioning scheme was invented for this). Commit is
/// deliberately separate and nullable - it only has a value when supplied explicitly at
/// Docker build time (see BuildInfoOptions); never conflate the two.
/// </summary>
public sealed record BuildInfoResponse(string Version, string? Commit);

public sealed record SchedulerStatusResponse(bool Enabled);

public sealed record EpisodeStatusCounts(
    int Scheduled,
    int Pending,
    int Searching,
    int Validating,
    int Completed,
    int Unavailable,
    int Error);

public sealed record SeriesStatusCounts(int Active, int MetadataRefreshDue);

public sealed record OperationalStatusResponse(
    BuildInfoResponse Build,
    SchedulerStatusResponse Scheduler,
    EpisodeStatusCounts Episodes,
    SeriesStatusCounts Series);
