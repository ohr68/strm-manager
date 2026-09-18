namespace StrmManager.Modules.Catalog.Application.Status.GetOperationalStatus;

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
    SchedulerStatusResponse Scheduler,
    EpisodeStatusCounts Episodes,
    SeriesStatusCounts Series);
