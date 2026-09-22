namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>The plugin's only channel to STRM Manager's public HTTP API. P1 implements one read-only call.</summary>
public interface IStrmManagerClient
{
    /// <summary>
    /// Calls GET {BaseUrl}/api/movies/by-imdb/{imdbId}. Never throws for an expected outcome (not found, invalid id,
    /// unreachable, malformed response) - those are all represented in the returned <see cref="MovieLookupResult"/>.
    /// A cancellation requested by <paramref name="cancellationToken"/> propagates as a normal
    /// <see cref="OperationCanceledException"/>, not as a result value.
    /// </summary>
    Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken);
}
