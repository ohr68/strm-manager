using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;

namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral series metadata - what CatalogSynchronizer needs, not everything a
/// given provider's API happens to return. Provider-specific DTOs never cross this
/// boundary (see IMetadataProvider).
/// </summary>
public sealed record SeriesMetadata(
    ExternalIds ExternalIds,
    string Title,
    string? OriginalTitle,
    int Year,
    SeriesStatus Status,
    IReadOnlyList<EpisodeMetadata> Episodes);
