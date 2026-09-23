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
}
