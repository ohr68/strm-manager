namespace StrmManager.Modules.Catalog.Application.Metadata;

public sealed record CatalogSynchronizationResult(
    int SeasonsAdded,
    int EpisodesAdded,
    int EpisodesUpdated,
    int EpisodesSkipped);
