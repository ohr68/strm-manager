using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Shared;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;

namespace StrmManager.Modules.Catalog.Application.Series.AddSeries;

/// <summary>
/// Creates a Series from provider-owned metadata resolved by IMDb id and synchronizes
/// its seasons/episodes in the same operation - "add a series" should not require the
/// caller to provide title/year metadata or perform a separate manual refresh.
///
/// The metadata provider is authoritative for the Series metadata used at creation.
/// If metadata cannot be resolved, no Series is created and the provider error is
/// returned to the caller.
/// </summary>
internal sealed class AddSeriesCommandHandler(
    ISeriesRepository seriesRepository,
    IMetadataProvider metadataProvider,
    CatalogSynchronizer catalogSynchronizer,
    IUnitOfWork unitOfWork,
    IOptions<MetadataRefreshOptions> metadataRefreshOptions,
    TimeProvider timeProvider)
    : ICommandHandler<AddSeriesCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        AddSeriesCommand command,
        CancellationToken cancellationToken)
    {
        SeriesEntity? existing =
            await seriesRepository.GetByImdbIdAsync(
                command.ImdbId,
                cancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        Result<SeriesMetadata> metadataResult =
            await metadataProvider.GetSeriesAsync(
                command.ImdbId,
                cancellationToken);

        if (metadataResult.IsFailure)
        {
            return Result.Failure<Guid>(metadataResult.Error);
        }

        SeriesMetadata metadata = metadataResult.Value;

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var externalIds = new ExternalIds(
            metadata.ExternalIds.ImdbId,
            metadata.ExternalIds.TmdbId,
            metadata.ExternalIds.TvdbId);

        SeriesEntity series = SeriesEntity.Create(
            externalIds,
            metadata.Title,
            metadata.OriginalTitle,
            metadata.Year,
            utcNow);

        seriesRepository.Insert(series);

        DateTime nextRefreshAtUtc =
            utcNow.Add(
                metadataRefreshOptions.Value.ActiveSeriesRefreshInterval);

        series.MarkMetadataRefreshAttempted(
            utcNow,
            nextRefreshAtUtc);

        series.UpdateMetadata(
            metadata.Title,
            metadata.OriginalTitle,
            metadata.Year,
            metadata.Status,
            utcNow);

        await catalogSynchronizer.SynchronizeAsync(
            series.Id,
            metadata.Episodes,
            utcNow,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return series.Id;
    }
}
