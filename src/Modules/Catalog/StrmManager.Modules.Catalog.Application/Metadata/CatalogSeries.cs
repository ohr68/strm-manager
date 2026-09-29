namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral series catalog preview used for browse/discovery.
/// Catalog browsing is metadata-only and never creates or mutates a Series aggregate.
/// </summary>
public sealed record CatalogSeries(
    string ExternalId,
    string Title,
    int? Year,
    string? PosterUrl,
    IReadOnlyList<string> Genres);
