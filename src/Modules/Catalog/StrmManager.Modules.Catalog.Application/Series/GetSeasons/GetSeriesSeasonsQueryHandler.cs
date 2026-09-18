using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Seasons;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;
using SeriesErrors = StrmManager.Modules.Catalog.Domain.Series.SeriesErrors;

namespace StrmManager.Modules.Catalog.Application.Series.GetSeasons;

internal sealed class GetSeriesSeasonsQueryHandler(
    ISeriesRepository seriesRepository,
    ISeasonRepository seasonRepository)
    : IQueryHandler<GetSeriesSeasonsQuery, IReadOnlyList<SeasonResponse>>
{
    public async Task<Result<IReadOnlyList<SeasonResponse>>> Handle(GetSeriesSeasonsQuery query, CancellationToken cancellationToken)
    {
        SeriesEntity? series = await seriesRepository.GetAsync(query.SeriesId, cancellationToken);

        if (series is null)
        {
            return Result.Failure<IReadOnlyList<SeasonResponse>>(SeriesErrors.NotFound(query.SeriesId));
        }

        IReadOnlyList<Season> seasons = await seasonRepository.GetAllBySeriesAsync(query.SeriesId, cancellationToken);

        IReadOnlyList<SeasonResponse> response = seasons
            .Select(season => new SeasonResponse(season.Id, season.Number, season.CreatedAtUtc, season.UpdatedAtUtc))
            .ToList();

        return Result.Success(response);
    }
}
