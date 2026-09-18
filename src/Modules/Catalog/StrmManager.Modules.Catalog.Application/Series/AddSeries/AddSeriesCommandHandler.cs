using Microsoft.Extensions.Logging;
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
/// Creates the Series and, best-effort, synchronizes it against the metadata provider
/// in the same operation - "add a series" should not require a separate manual step to
/// get its seasons/episodes (see ADR-007). If the provider call fails (down, series not
/// found there, timeout, ...), the Series is still created from the caller-supplied
/// data; the command still succeeds, and a later POST /api/series/{id}/refresh can
/// retry. This intentionally does not surface the sync outcome in the response - GET
/// the series (or its seasons) to see what came through.
/// </summary>
internal sealed partial class AddSeriesCommandHandler(
    ISeriesRepository seriesRepository,
    IMetadataProvider metadataProvider,
    CatalogSynchronizer catalogSynchronizer,
    IUnitOfWork unitOfWork,
    IOptions<MetadataRefreshOptions> metadataRefreshOptions,
    TimeProvider timeProvider,
    ILogger<AddSeriesCommandHandler> logger)
    : ICommandHandler<AddSeriesCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddSeriesCommand command, CancellationToken cancellationToken)
    {
        SeriesEntity? existing = await seriesRepository.GetByImdbIdAsync(command.ImdbId, cancellationToken);

        if (existing is not null)
        {
            return existing.Id;
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var externalIds = new ExternalIds(command.ImdbId, command.TmdbId, command.TvdbId);

        SeriesEntity series = SeriesEntity.Create(externalIds, command.Title, command.OriginalTitle, command.Year, utcNow);

        seriesRepository.Insert(series);

        Result<SeriesMetadata> metadataResult = await metadataProvider.GetSeriesAsync(command.ImdbId, cancellationToken);

        // Tracked the same way a background refresh would be, so the maintenance
        // worker's next-due check is meaningful immediately rather than relying on its
        // "NextMetadataRefreshAtUtc == null means due now" fallback.
        DateTime nextRefreshAtUtc = utcNow.Add(metadataRefreshOptions.Value.ActiveSeriesRefreshInterval);
        series.MarkMetadataRefreshAttempted(utcNow, nextRefreshAtUtc);

        if (metadataResult.IsSuccess)
        {
            SeriesMetadata metadata = metadataResult.Value;
            series.UpdateMetadata(metadata.Title, metadata.OriginalTitle, metadata.Year, metadata.Status, utcNow);
            await catalogSynchronizer.SynchronizeAsync(series.Id, metadata.Episodes, utcNow, cancellationToken);
        }
        else
        {
            LogMetadataSyncSkipped(logger, command.ImdbId, metadataResult.Error.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return series.Id;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Series {ImdbId} was added without metadata - provider sync failed with {ErrorCode}")]
    private static partial void LogMetadataSyncSkipped(ILogger logger, string imdbId, string errorCode);
}
