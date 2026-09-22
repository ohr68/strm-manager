namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>
/// The closed outcome of a GET /api/movies/by-imdb/{imdbId} call - deliberately small (five cases, no fields beyond
/// what a caller of this slice needs), not a general-purpose result framework.
/// </summary>
public abstract record MovieLookupResult
{
    private MovieLookupResult()
    {
    }

    /// <summary>STRM Manager knows this movie. Fields mirror only what P1 needs - not the full MovieResponse.</summary>
    public sealed record Found(Guid MovieId, string ImdbId, string Title, int Year, string Status) : MovieLookupResult;

    /// <summary>STRM Manager returned 404: no movie has this IMDb id.</summary>
    public sealed record NotFound : MovieLookupResult;

    /// <summary>STRM Manager returned 400: the IMDb id was rejected as malformed.</summary>
    public sealed record Invalid : MovieLookupResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : MovieLookupResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed
    /// or structurally-unexpected 200 body, or (before any request is even sent) an invalid/unconfigured base URL.
    /// </summary>
    public sealed record Error(string Reason) : MovieLookupResult;
}
