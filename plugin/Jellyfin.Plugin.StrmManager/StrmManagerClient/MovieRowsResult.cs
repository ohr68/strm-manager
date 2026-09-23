namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>One catalog row - a stable id, a display name, and its movies in backend order. Reuses CatalogMovie, not a second movie DTO.</summary>
public sealed record MovieRow(string Id, string Name, IReadOnlyList<CatalogMovie> Movies);

/// <summary>
/// The closed outcome of a GET api/catalog/movies/rows call - matching CatalogMoviesResult's shape. No "not found"
/// case: zero rows is still Found with an empty list, not a failure. Row identity/order/which-are-omitted is
/// entirely the backend's decision (see UI-4a's MovieRowPolicy) - this client never filters or re-orders rows.
/// </summary>
public abstract record MovieRowsResult
{
    private MovieRowsResult()
    {
    }

    public sealed record Found(IReadOnlyList<MovieRow> Rows) : MovieRowsResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : MovieRowsResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed
    /// or structurally-unexpected 200 body, or (before any request is even sent) an invalid/unconfigured base URL.
    /// </summary>
    public sealed record Error(string Reason) : MovieRowsResult;
}
