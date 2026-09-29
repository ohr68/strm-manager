namespace StrmManager.Modules.Catalog.Application.Catalog.GetPopularSeries;

public sealed record PopularSeriesResponse(
    IReadOnlyList<CatalogSeriesResponse> Series);

public sealed record CatalogSeriesResponse(
    string ExternalId,
    string Title,
    int? Year,
    string? PosterUrl);
