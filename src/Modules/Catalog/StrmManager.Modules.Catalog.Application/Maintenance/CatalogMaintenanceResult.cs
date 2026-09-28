namespace StrmManager.Modules.Catalog.Application.Maintenance;

public sealed record CatalogMaintenanceResult(
    int EpisodesReleased,
    int EpisodesRetried,
    int EpisodesRecovered,
    int MoviesReleased,
    int MoviesRetried,
    int MoviesRecovered,
    int SeriesMetadataRefreshed,
    int SeriesMetadataRefreshFailed);
