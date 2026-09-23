namespace StrmManager.Modules.Catalog.Application.Maintenance;

public sealed record CatalogMaintenanceResult(
    int EpisodesReleased,
    int EpisodesRetried,
    int EpisodesRecovered,
    int MoviesRecovered,
    int SeriesMetadataRefreshed,
    int SeriesMetadataRefreshFailed);
