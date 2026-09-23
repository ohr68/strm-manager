namespace StrmManager.Modules.Catalog.Application.Metadata;

/// <summary>
/// Provider-neutral catalog preview - deliberately narrow (only what a browse row needs: identity, title, year,
/// poster, genre), not a projection of everything a given catalog provider's API happens to return. Provider-
/// specific DTOs never cross this boundary (see ICatalogProvider). Unlike MovieMetadata, this never becomes a Movie
/// aggregate - catalog browsing is metadata-only (see the UI-2 report).
///
/// Genres is metadata only, exactly as the provider tagged it (see CinemetaCatalogMapper) - it exists so
/// Application-level row composition (see Catalog/GetMovieRows) can bucket movies without a second provider
/// request; it is not exposed on the public catalog HTTP response (see GetPopularMovies/GetMovieRows'
/// CatalogMovieResponse) unless a future slice needs it there.
/// </summary>
public sealed record CatalogMovie(string ExternalId, string Title, int? Year, string? PosterUrl, IReadOnlyList<string> Genres);
