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
}
