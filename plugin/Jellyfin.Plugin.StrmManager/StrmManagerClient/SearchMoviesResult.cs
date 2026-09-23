namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>
/// The closed outcome of a GET api/catalog/movies/search call - matching CatalogMoviesResult's shape plus an
/// Invalid case for the backend's 400 (empty/whitespace-only or over-100-character query), since search takes
/// free-text user input the other catalog calls don't.
/// </summary>
public abstract record SearchMoviesResult
{
    private SearchMoviesResult()
    {
    }

    /// <summary>Includes a genuinely empty match list - no results is a successful outcome, never a failure.</summary>
    public sealed record Found(IReadOnlyList<CatalogMovie> Movies) : SearchMoviesResult;

    /// <summary>400: the backend rejected the query (empty/whitespace-only after trimming, or over 100 characters).</summary>
    public sealed record Invalid : SearchMoviesResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : SearchMoviesResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed
    /// or structurally-unexpected 200 body, or (before any request is even sent) an invalid/unconfigured base URL.
    /// </summary>
    public sealed record Error(string Reason) : SearchMoviesResult;
}
