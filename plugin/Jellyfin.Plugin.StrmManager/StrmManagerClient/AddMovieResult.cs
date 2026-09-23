namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>
/// The closed outcome of a POST /api/movies call - small, matching MovieLookupResult's shape, not a general-purpose
/// result framework.
///
/// Six cases, not five: inspecting AddMovieCommandHandler showed POST /api/movies can genuinely 404 - not a routing
/// 404, but a real problem-details response (Metadata.MovieNotFound) when the IMDb id is syntactically valid but
/// STRM Manager's metadata provider has no data for it. That is a different, useful-to-distinguish outcome from a
/// malformed id (400 Invalid): the id was fine, there is just nothing to add. Folding it into Invalid or Error would
/// hide real information the two different HTTP statuses already carry, so it gets its own case instead.
/// </summary>
public abstract record AddMovieResult
{
    private AddMovieResult()
    {
    }

    /// <summary>201: the movie was created. STRM Manager's own canonical id - not derived or guessed by the plugin.</summary>
    public sealed record Created(Guid MovieId) : AddMovieResult;

    /// <summary>409: a movie with this IMDb id already exists. STRM Manager's conflict response carries no id, so none is invented here.</summary>
    public sealed record AlreadyExists : AddMovieResult;

    /// <summary>400: the IMDb id was rejected (malformed, or - a second, distinct 400 case - metadata has no usable release date).</summary>
    public sealed record Invalid : AddMovieResult;

    /// <summary>404: the id is well-formed, but STRM Manager's metadata provider has no data for it - see the type summary.</summary>
    public sealed record NotFound : AddMovieResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : AddMovieResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed
    /// or structurally-unexpected 201 body, or (before any request is even sent) an invalid/unconfigured base URL.
    /// </summary>
    public sealed record Error(string Reason) : AddMovieResult;
}
