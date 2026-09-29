using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Series.GetSeries;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesErrors = StrmManager.Modules.Catalog.Domain.Series.SeriesErrors;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeriesByImdbId;

internal sealed class GetSeriesByImdbIdQueryHandler(
    ISeriesRepository seriesRepository)
    : IQueryHandler<GetSeriesByImdbIdQuery, SeriesResponse>
{
    public async Task<Result<SeriesResponse>> Handle(
        GetSeriesByImdbIdQuery query,
        CancellationToken cancellationToken)
    {
        SeriesEntity? series =
            await seriesRepository.GetByImdbIdAsync(
                query.ImdbId,
                cancellationToken);

        if (series is null)
        {
            return Result.Failure<SeriesResponse>(
                SeriesErrors.NotFoundByImdbId(query.ImdbId));
        }

        return new SeriesResponse(
            series.Id,
            series.ExternalIds.ImdbId,
            series.ExternalIds.TmdbId,
            series.ExternalIds.TvdbId,
            series.Title,
            series.OriginalTitle,
            series.Year,
            series.Status.ToString(),
            series.CreatedAtUtc,
            series.UpdatedAtUtc);
    }
}
