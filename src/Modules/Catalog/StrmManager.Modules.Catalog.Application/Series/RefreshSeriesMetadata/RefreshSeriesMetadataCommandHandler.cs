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

        // HTTP call happens before any persistence work starts - no SQLite transaction
        // is open while we wait on the provider (see ADR-007).
        Result<SeriesMetadata> metadataResult = await metadataProvider.GetSeriesAsync(imdbId, cancellationToken);

        if (metadataResult.IsFailure)
        {
            return Result.Failure<CatalogSynchronizationResult>(metadataResult.Error);
        }

        SeriesMetadata metadata = metadataResult.Value;
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        series.UpdateMetadata(metadata.Title, metadata.OriginalTitle, metadata.Year, metadata.Status, utcNow);

        CatalogSynchronizationResult result = await catalogSynchronizer.SynchronizeAsync(
            series.Id, metadata.Episodes, utcNow, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
