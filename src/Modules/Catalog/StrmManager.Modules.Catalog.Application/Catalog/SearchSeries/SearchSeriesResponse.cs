using StrmManager.Modules.Catalog.Application.Catalog.GetPopularSeries;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchSeries;

public sealed record SearchSeriesResponse(
    IReadOnlyList<CatalogSeriesResponse> Series);
