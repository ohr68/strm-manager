namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>One catalog card's worth of data - deliberately narrow, matching the backend's own CatalogMovieResponse.</summary>
public sealed record CatalogMovie(string ExternalId, string Title, int? Year, string? PosterUrl);

/// <summary>
/// The closed outcome of a GET api/catalog/movies/popular call - matching MovieLookupResult/AddMovieResult's shape.
/// No "not found" case: an empty catalog is still Found with zero movies, not a failure.
/// </summary>
public abstract record CatalogMoviesResult
{
    private CatalogMoviesResult()
    {
    }

    public sealed record Found(IReadOnlyList<CatalogMovie> Movies) : CatalogMoviesResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : CatalogMoviesResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed
    /// or structurally-unexpected 200 body, or (before any request is even sent) an invalid/unconfigured base URL.
    /// </summary>
    public sealed record Error(string Reason) : CatalogMoviesResult;
}
