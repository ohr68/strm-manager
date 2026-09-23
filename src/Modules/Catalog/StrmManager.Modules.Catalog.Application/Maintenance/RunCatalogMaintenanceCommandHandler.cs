using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Application.Processing;
using StrmManager.Modules.Catalog.Application.Series.RefreshSeriesMetadata;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Movies;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.Application.Maintenance;

/// <summary>
/// The housekeeping steps an autonomous server needs, bundled into one testable use case
/// rather than one tiny handler/BackgroundService per step (see ADR-012): promote released
/// Scheduled episodes, retry due Unavailable/Error episodes, recover stale
/// Searching/Validating episodes AND movies (an interrupted processing run - see
/// Episode.RecoverInterruptedProcessing/Movie.RecoverInterruptedProcessing/ADR-013), and
/// refresh metadata for Series whose schedule is due. Called by Scheduling's
/// CatalogMaintenanceWorker, and directly by tests - the worker adds nothing but a timer
/// around this.
/// </summary>
internal sealed partial class RunCatalogMaintenanceCommandHandler(
    IEpisodeRepository episodeRepository,
    IMovieRepository movieRepository,
    ISeriesRepository seriesRepository,
    IUnitOfWork unitOfWork,
    ICommandHandler<RefreshSeriesMetadataCommand, CatalogSynchronizationResult> refreshSeriesMetadataHandler,
    IOptions<ProcessingOptions> processingOptions,
    TimeProvider timeProvider,
    ILogger<RunCatalogMaintenanceCommandHandler> logger)
    : ICommandHandler<RunCatalogMaintenanceCommand, CatalogMaintenanceResult>
{
    public async Task<Result<CatalogMaintenanceResult>> Handle(RunCatalogMaintenanceCommand command, CancellationToken cancellationToken)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        int released = await PromoteScheduledEpisodesAsync(utcNow, cancellationToken);
        int retried = await RetryDueEpisodesAsync(utcNow, cancellationToken);
        int recovered = await RecoverStaleProcessingAsync(utcNow, cancellationToken);
        int moviesRecovered = await RecoverStaleMovieProcessingAsync(utcNow, cancellationToken);

        // One save for all four batches above - they're independent, cheap, in-memory
        // domain transitions with no external I/O between them.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        (int refreshed, int refreshFailed) = await RefreshDueSeriesMetadataAsync(utcNow, cancellationToken);

        LogMaintenanceCompleted(logger, released, retried, recovered, refreshed, refreshFailed);

        return new CatalogMaintenanceResult(released, retried, recovered, moviesRecovered, refreshed, refreshFailed);
    }

    private async Task<int> PromoteScheduledEpisodesAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        IReadOnlyList<Episode> due = await episodeRepository.GetScheduledDueAsync(utcNow, cancellationToken);

        int promoted = 0;

        foreach (Episode episode in due)
        {
            if (episode.TryBecomeEligible(utcNow))
            {
                promoted++;
            }
        }

        return promoted;
    }

    private async Task<int> RetryDueEpisodesAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        IReadOnlyList<Episode> retryable = await episodeRepository.GetRetryableAsync(utcNow, cancellationToken);

        foreach (Episode episode in retryable)
        {
            episode.Retry(utcNow);
        }

        return retryable.Count;
    }

    private async Task<int> RecoverStaleProcessingAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        DateTime staleThresholdUtc = utcNow.Subtract(processingOptions.Value.StaleProcessingThreshold);
        IReadOnlyList<Episode> stale = await episodeRepository.GetStaleProcessingAsync(staleThresholdUtc, cancellationToken);

        foreach (Episode episode in stale)
        {
            episode.RecoverInterruptedProcessing(utcNow);
            LogEpisodeRecovered(logger, episode.Id);
        }

        return stale.Count;
    }

    private async Task<int> RecoverStaleMovieProcessingAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        DateTime staleThresholdUtc = utcNow.Subtract(processingOptions.Value.StaleProcessingThreshold);
        IReadOnlyList<Movie> stale = await movieRepository.GetStaleProcessingAsync(staleThresholdUtc, cancellationToken);

        foreach (Movie movie in stale)
        {
            movie.RecoverInterruptedProcessing(utcNow);
            LogMovieRecovered(logger, movie.Id);
        }

        return stale.Count;
    }

    private async Task<(int Refreshed, int Failed)> RefreshDueSeriesMetadataAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        IReadOnlyList<SeriesEntity> due = await seriesRepository.GetDueForMetadataRefreshAsync(utcNow, cancellationToken);

        int refreshed = 0;
        int failed = 0;

        foreach (SeriesEntity series in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Metadata refresh failures must never corrupt/delete existing catalog data
            // (see ADR-007/ADR-013) - RefreshSeriesMetadataCommandHandler already
            // guarantees this; a failure here just means this series is skipped for now
            // and retried on its own schedule, not that the whole tick aborts.
            Result<CatalogSynchronizationResult> result = await refreshSeriesMetadataHandler.Handle(
                new RefreshSeriesMetadataCommand(series.Id), cancellationToken);

            if (result.IsSuccess)
            {
                refreshed++;
            }
            else
            {
                failed++;
                LogMetadataRefreshFailed(logger, series.Id, result.Error.Code);
            }
        }

        return (refreshed, failed);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recovered interrupted processing for episode {EpisodeId}")]
    private static partial void LogEpisodeRecovered(ILogger logger, Guid episodeId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recovered interrupted processing for movie {MovieId}")]
    private static partial void LogMovieRecovered(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Metadata refresh failed for series {SeriesId}: {ErrorCode}")]
    private static partial void LogMetadataRefreshFailed(ILogger logger, Guid seriesId, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog maintenance: {Released} released, {Retried} retried, {Recovered} recovered, {Refreshed} series refreshed ({RefreshFailed} failed)")]
    private static partial void LogMaintenanceCompleted(ILogger logger, int released, int retried, int recovered, int refreshed, int refreshFailed);
}
