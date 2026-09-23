using StrmManager.Modules.Catalog.Application.Catalog.GetPopularMovies;

namespace StrmManager.Modules.Catalog.Application.Catalog.GetMovieRows;

public sealed record MovieRowsResponse(IReadOnlyList<MovieRowResponse> Rows);

/// <summary>
/// Reuses GetPopularMovies' own CatalogMovieResponse rather than declaring a second, identical movie-card shape -
/// "the current catalog movie response" the API contract asks to stay compatible with is this one. Genres are
/// deliberately not included here (row membership is decided server-side; the browser does not need them).
/// </summary>
public sealed record MovieRowResponse(string Id, string Name, IReadOnlyList<CatalogMovieResponse> Movies);
