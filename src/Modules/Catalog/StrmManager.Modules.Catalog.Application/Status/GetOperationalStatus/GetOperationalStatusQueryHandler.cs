using System.Reflection;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;

namespace StrmManager.Modules.Catalog.Application.Status.GetOperationalStatus;

/// <summary>
/// Aggregate-only queries (GROUP BY / COUNT), never a per-request table scan - see
/// EpisodeRepository.GetStatusCountsAsync/SeriesRepository.Count*Async. Never includes
/// stream URLs or SourceAttempt internals - only counts.
/// </summary>
internal sealed class GetOperationalStatusQueryHandler(
    IEpisodeRepository episodeRepository,
    ISeriesRepository seriesRepository,
    ISchedulerStatusProvider schedulerStatusProvider,
    IOptions<BuildInfoOptions> buildInfoOptions,
    TimeProvider timeProvider)
    : IQueryHandler<GetOperationalStatusQuery, OperationalStatusResponse>
{
    public async Task<Result<OperationalStatusResponse>> Handle(GetOperationalStatusQuery query, CancellationToken cancellationToken)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        IReadOnlyDictionary<MediaStatus, int> episodeCounts = await episodeRepository.GetStatusCountsAsync(cancellationToken);
        int activeSeries = await seriesRepository.CountActiveAsync(cancellationToken);
        int metadataRefreshDue = await seriesRepository.CountDueForMetadataRefreshAsync(utcNow, cancellationToken);

        var response = new OperationalStatusResponse(
            new BuildInfoResponse(GetAssemblyVersion(), NormalizeCommit(buildInfoOptions.Value.Commit)),
            new SchedulerStatusResponse(schedulerStatusProvider.Enabled),
            new EpisodeStatusCounts(
                episodeCounts.GetValueOrDefault(MediaStatus.Scheduled),
                episodeCounts.GetValueOrDefault(MediaStatus.Pending),
                episodeCounts.GetValueOrDefault(MediaStatus.Searching),
                episodeCounts.GetValueOrDefault(MediaStatus.Validating),
                episodeCounts.GetValueOrDefault(MediaStatus.Completed),
                episodeCounts.GetValueOrDefault(MediaStatus.Unavailable),
                episodeCounts.GetValueOrDefault(MediaStatus.Error)),
            new SeriesStatusCounts(activeSeries, metadataRefreshDue));

        return response;
    }

    // The SDK's own default informational version (no <Version> is set anywhere in this
    // repo, so this is whatever .NET already produces on its own - "1.0.0" today). No
    // versioning scheme was introduced for this.
    private static string GetAssemblyVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    // The Dockerfile sets Build__Commit="" when no --build-arg GIT_COMMIT is supplied
    // (e.g. a local `docker build`) - an empty env var, not an absent one - so this
    // normalizes that to a clean `null` rather than exposing "" through the API.
    private static string? NormalizeCommit(string? commit) =>
        string.IsNullOrWhiteSpace(commit) ? null : commit;
}
