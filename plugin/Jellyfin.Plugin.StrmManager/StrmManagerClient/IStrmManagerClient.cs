namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>The plugin's only channel to STRM Manager's public HTTP API.</summary>
public interface IStrmManagerClient
{
    /// <summary>
    /// Calls GET {BaseUrl}/api/movies/by-imdb/{imdbId}. Never throws for an expected outcome (not found, invalid id,
    /// unreachable, malformed response) - those are all represented in the returned <see cref="MovieLookupResult"/>.
    /// A cancellation requested by <paramref name="cancellationToken"/> propagates as a normal
    /// <see cref="OperationCanceledException"/>, not as a result value.
    /// </summary>
    Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken);

    /// <summary>
    /// Calls POST {BaseUrl}/api/movies with just the IMDb id (that is the entire request body STRM Manager accepts
    /// or needs - title/year/etc. always come from its own metadata provider, never from the caller). Never throws
    /// for an expected outcome - those are all represented in the returned <see cref="AddMovieResult"/>. A
    /// cancellation requested by <paramref name="cancellationToken"/> propagates as a normal
    /// <see cref="OperationCanceledException"/>, not as a result value.
    /// </summary>
    Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken);

    /// <summary>
    /// Calls POST {BaseUrl}/api/movies/{movieId}/process. Never throws for an expected outcome - those are all
    /// represented in the returned <see cref="ProcessMovieResult"/>. A cancellation requested by
    /// <paramref name="cancellationToken"/> propagates as a normal <see cref="OperationCanceledException"/>, not as
    /// a result value.
    ///
    /// This is currently an unused primitive - the backend endpoint is synchronous and can legitimately run longer
    /// than a Jellyfin controller/browser request lifetime, and a caller cancellation after the backend's durable
    /// Pending -&gt; Searching claim can leave the movie stuck (Movie stale-processing recovery is not wired into
    /// backend maintenance yet). It is intentionally not called from any controller, EnsureMovieService, or
    /// scheduled/background code in this slice - see ProcessMovieResult's own remarks.
    /// </summary>
    Task<ProcessMovieResult> ProcessMovieAsync(Guid movieId, CancellationToken cancellationToken);

    /// <summary>
    /// Calls GET {BaseUrl}/api/catalog/movies/popular - UI-2's "Popular Movies" row. Never throws for an expected
    /// outcome - those are all represented in the returned <see cref="CatalogMoviesResult"/>. A cancellation
    /// requested by <paramref name="cancellationToken"/> propagates as a normal
    /// <see cref="OperationCanceledException"/>, not as a result value.
    /// </summary>
    Task<CatalogMoviesResult> GetPopularMoviesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Calls GET {BaseUrl}/api/catalog/movies/rows - UI-4's multi-row Discover presentation (Popular plus whichever
    /// genre rows the backend's own row policy currently has movies for). Never throws for an expected outcome -
    /// those are all represented in the returned <see cref="MovieRowsResult"/>. A cancellation requested by
    /// <paramref name="cancellationToken"/> propagates as a normal <see cref="OperationCanceledException"/>, not as
    /// a result value.
    /// </summary>
    Task<MovieRowsResult> GetMovieRowsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Calls GET {BaseUrl}/api/catalog/movies/search?query=... - UI-5c's Discover search box. Never throws for an
    /// expected outcome - those are all represented in the returned <see cref="SearchMoviesResult"/>. A
    /// cancellation requested by <paramref name="cancellationToken"/> propagates as a normal
    /// <see cref="OperationCanceledException"/>, not as a result value.
    /// </summary>
    Task<SearchMoviesResult> SearchMoviesAsync(string query, CancellationToken cancellationToken);
}
