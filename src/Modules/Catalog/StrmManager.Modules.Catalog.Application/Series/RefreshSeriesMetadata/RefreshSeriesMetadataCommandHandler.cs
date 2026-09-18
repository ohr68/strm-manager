using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Metadata;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesErrors = StrmManager.Modules.Catalog.Domain.Series.SeriesErrors;

namespace StrmManager.Modules.Catalog.Application.Series.RefreshSeriesMetadata;

internal sealed class RefreshSeriesMetadataCommandHandler(
    ISeriesRepository seriesRepository,
    IMetadataProvider metadataProvider,
    CatalogSynchronizer catalogSynchronizer,
    IUnitOfWork unitOfWork,
    IOptions<MetadataRefreshOptions> metadataRefreshOptions,
    TimeProvider timeProvider)
    : ICommandHandler<RefreshSeriesMetadataCommand, CatalogSynchronizationResult>
{
    public async Task<Result<CatalogSynchronizationResult>> Handle(RefreshSeriesMetadataCommand command, CancellationToken cancellationToken)
    {
        SeriesEntity? series = await seriesRepository.GetAsync(command.SeriesId, cancellationToken);

        if (series is null)
        {
            return Result.Failure<CatalogSynchronizationResult>(SeriesErrors.NotFound(command.SeriesId));
        }

        string? imdbId = series.ExternalIds.ImdbId;

        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return Result.Failure<CatalogSynchronizationResult>(SeriesErrors.MissingImdbId(series.Id));
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        DateTime nextRefreshAtUtc = utcNow.Add(metadataRefreshOptions.Value.ActiveSeriesRefreshInterval);

        // Marked as attempted (and the next check scheduled) before the provider call,
        // not after - a provider failure below must still push the schedule forward, or
        // a down provider would be hammered on every maintenance tick instead of waiting
        // for the next interval (see ADR-013).
        series.MarkMetadataRefreshAttempted(utcNow, nextRefreshAtUtc);

        // HTTP call happens before any further persistence work starts - no SQLite
        // transaction is open while we wait on the provider (see ADR-007).
        Result<SeriesMetadata> metadataResult = await metadataProvider.GetSeriesAsync(imdbId, cancellationToken);

        if (metadataResult.IsFailure)
        {
            // Existing catalog data (seasons/episodes already synced) is left untouched -
            // only the refresh-tracking fields above are persisted.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<CatalogSynchronizationResult>(metadataResult.Error);
        }

        SeriesMetadata metadata = metadataResult.Value;

        series.UpdateMetadata(metadata.Title, metadata.OriginalTitle, metadata.Year, metadata.Status, utcNow);

        CatalogSynchronizationResult result = await catalogSynchronizer.SynchronizeAsync(
            series.Id, metadata.Episodes, utcNow, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
