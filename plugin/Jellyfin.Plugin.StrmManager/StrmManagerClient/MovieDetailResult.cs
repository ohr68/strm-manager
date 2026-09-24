namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>
/// One movie's rich display metadata (UI-6c) - deliberately separate from CatalogMovie (catalog/search cards) and
/// from MovieResponseDto/Watch-related models (domain/processing status). This is metadata-only, never widened
/// with anything provider/source/ffprobe/processing-related.
/// </summary>
public sealed record MovieDetail(
    string ExternalId,
    string Title,
    int Year,
    int? RuntimeMinutes,
    string? Description,
    IReadOnlyList<string> Genres,
    string? ImdbRating,
    string? PosterUrl,
    string? BackdropUrl);

/// <summary>
/// The closed outcome of a GET api/catalog/movies/{imdbId}/detail call - same five-case shape as MovieLookupResult
/// (GET /api/movies/by-imdb/{imdbId}), since both are "look up one thing by IMDb id" calls with the same
/// found/not-found/invalid/unreachable/error taxonomy. Not a general-purpose result framework.
/// </summary>
public abstract record MovieDetailResult
{
    private MovieDetailResult()
    {
    }

    /// <summary>STRM Manager has display metadata for this movie.</summary>
    public sealed record Found(MovieDetail Detail) : MovieDetailResult;

    /// <summary>STRM Manager returned 404: no metadata for this IMDb id.</summary>
    public sealed record NotFound : MovieDetailResult;

    /// <summary>STRM Manager returned 400: the IMDb id was rejected as malformed.</summary>
    public sealed record Invalid : MovieDetailResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : MovieDetailResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed
    /// or structurally-unexpected 200 body, or (before any request is even sent) an invalid/unconfigured base URL.
    /// </summary>
    public sealed record Error(string Reason) : MovieDetailResult;
}
