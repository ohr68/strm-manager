using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Catalog.GetPopularSeries;
using StrmManager.Modules.Catalog.Application.Metadata;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchSeries;

internal sealed class SearchSeriesQueryHandler(
    ICatalogProvider catalogProvider)
    : IQueryHandler<SearchSeriesQuery, SearchSeriesResponse>
{
    private const int SeriesLimit = 20;

    public async Task<Result<SearchSeriesResponse>> Handle(
        SearchSeriesQuery query,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<CatalogSeries>> result =
            await catalogProvider.SearchSeriesAsync(
                query.Query,
                SeriesLimit,
                cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<SearchSeriesResponse>(
                result.Error);
        }

        List<CatalogSeriesResponse> series = result.Value
            .Select(item =>
                new CatalogSeriesResponse(
                    item.ExternalId,
                    item.Title,
                    item.Year,
                    item.PosterUrl))
            .ToList();

        return new SearchSeriesResponse(series);
    }
}
