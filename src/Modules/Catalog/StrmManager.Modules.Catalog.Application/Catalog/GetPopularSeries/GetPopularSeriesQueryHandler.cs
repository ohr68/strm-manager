using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetPopularSeries;

internal sealed class GetPopularSeriesQueryHandler(
    ICatalogProvider catalogProvider)
    : IQueryHandler<
        GetPopularSeriesQuery,
        PopularSeriesResponse>
{
    private const int SeriesLimit = 20;

    public async Task<Result<PopularSeriesResponse>> Handle(
        GetPopularSeriesQuery query,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CatalogSeries>> result =
            await catalogProvider.GetPopularSeriesAsync(
                SeriesLimit,
                cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<PopularSeriesResponse>(
                result.Error);
        }

        List<CatalogSeriesResponse> series =
            result.Value
                .Select(item =>
                    new CatalogSeriesResponse(
                        item.ExternalId,
                        item.Title,
                        item.Year,
                        item.PosterUrl))
                .ToList();

        return new PopularSeriesResponse(series);
    }
}
