using StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;

namespace StrmManager.Modules.Catalog.Application.Catalog.SearchMovies;

/// <summary>
/// A neutral name of its own rather than reusing PopularMoviesResponse (that name would be misleading for search
/// results) - but the inner card shape is the existing CatalogMovieResponse, not a duplicate.
/// </summary>
public sealed record SearchMoviesResponse(IReadOnlyList<CatalogMovieResponse> Movies);
