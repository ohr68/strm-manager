namespace Jellyfin.Plugin.StrmManager.StrmManagerClient;

/// <summary>
/// The closed outcome of a POST /api/movies/{id}/process call - matching MovieLookupResult/AddMovieResult's shape,
/// not a general-purpose result framework.
///
/// Backend processing outcomes are kept semantically distinct from transport/client failures: <see cref="Completed"/>,
/// <see cref="Unavailable"/> and <see cref="ProcessingFailed"/> all come from a 200 response - the backend reached a
/// real, persisted outcome for the movie. <see cref="ProcessingFailed"/> is deliberately NOT named "Error" even
/// though it mirrors the backend's own status:"Error" - that name is reserved for this client's own catch-all
/// (unexpected HTTP status, malformed/unexpected-shape body, unconfigured BaseUrl), which is a different kind of
/// failure than a domain outcome the backend already recorded.
///
/// This is currently an unused primitive (see IStrmManagerClient.ProcessMovieAsync's remarks) - no controller,
/// EnsureMovieService, or background code calls it yet.
/// </summary>
public abstract record ProcessMovieResult
{
    private ProcessMovieResult()
    {
    }

    /// <summary>
    /// 200, status "Completed" - either this run wrote a new .strm, or the backend's already-Completed short-circuit
    /// returned as-is. <see cref="Provider"/>/<see cref="SourceName"/> are null on the short-circuit path (no
    /// provider selection re-runs for an already-Completed movie) - callers must not require them.
    /// </summary>
    public sealed record Completed(Guid MovieId, int Attempts, string? Provider, string? SourceName, string? StrmPath) : ProcessMovieResult;

    /// <summary>200, status "Unavailable" - the backend found no usable candidate for this movie this run.</summary>
    public sealed record Unavailable(int Attempts, string? Reason) : ProcessMovieResult;

    /// <summary>
    /// 200, status "Error" - a domain processing failure the backend already recorded (provider failure, .strm write
    /// failure, etc.), not a transport or client-side problem. See the type summary for why this is not named Error.
    /// </summary>
    public sealed record ProcessingFailed(int Attempts, string? Reason) : ProcessMovieResult;

    /// <summary>409 Movies.AlreadyBeingProcessed - another request currently holds the movie's Searching/Validating claim.</summary>
    public sealed record AlreadyBeingProcessed : ProcessMovieResult;

    /// <summary>
    /// 409 Movie.InvalidTransition - the movie is not Pending (e.g. Unavailable/Error awaiting a Retry that has not
    /// happened yet). Distinguished from AlreadyBeingProcessed by the problem response's title, which carries the
    /// backend's own error code.
    /// </summary>
    public sealed record InvalidTransition : ProcessMovieResult;

    /// <summary>404 Movies.NotFound - no movie has this id.</summary>
    public sealed record NotFound : ProcessMovieResult;

    /// <summary>The request could not reach STRM Manager at all (DNS/connect/timeout) - a transport failure.</summary>
    public sealed record Unreachable(string Reason) : ProcessMovieResult;

    /// <summary>
    /// STRM Manager was reached but something about the exchange was wrong: an unexpected HTTP status, a malformed or
    /// structurally-unexpected 200/409 body, an unrecognized status/title value, or (before any request is even
    /// sent) an invalid/unconfigured base URL. Never the backend's own in-band status:"Error" outcome - see
    /// <see cref="ProcessingFailed"/>.
    /// </summary>
    public sealed record Error(string Reason) : ProcessMovieResult;
}
