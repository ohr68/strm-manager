namespace StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;

public sealed record PopularMoviesResponse(IReadOnlyList<CatalogMovieResponse> Movies);

/// <summary>
/// The public API shape for one catalog card - deliberately its own type rather than reusing CatalogMovie
/// (Application/Metadata) directly, matching the project's existing rule that a provider-neutral Application model
/// and its public API response are kept separate (see MovieMetadata vs MovieResponse) even when the fields happen
/// to match today.
/// </summary>
public sealed record CatalogMovieResponse(string ExternalId, string Title, int? Year, string? PosterUrl);
