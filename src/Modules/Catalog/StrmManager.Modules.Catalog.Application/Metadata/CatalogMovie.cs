namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral catalog preview - deliberately narrow (only what a browse row needs: identity, title, year,
/// poster), not a projection of everything a given catalog provider's API happens to return. Provider-specific DTOs
/// never cross this boundary (see ICatalogProvider). Unlike MovieMetadata, this never becomes a Movie aggregate -
/// catalog browsing is metadata-only (see the UI-2 report).
/// </summary>
public sealed record CatalogMovie(string ExternalId, string Title, int? Year, string? PosterUrl);
