using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesErrors = StrmManager.Modules.Catalog.Domain.Series.SeriesErrors;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeries;

internal sealed class GetSeriesQueryHandler(ISeriesRepository seriesRepository)
    : IQueryHandler<GetSeriesQuery, SeriesResponse>
{
    public async Task<Result<SeriesResponse>> Handle(GetSeriesQuery query, CancellationToken cancellationToken)
    {
        SeriesEntity? series = await seriesRepository.GetAsync(query.SeriesId, cancellationToken);

        if (series is null)
        {
            return Result.Failure<SeriesResponse>(SeriesErrors.NotFound(query.SeriesId));
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
