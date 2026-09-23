namespace Jellyfin.Plugin.StrmManager.Movies;

/// <summary>
/// The closed outcome of EnsureMovieAsync - small, matching MovieLookupResult/AddMovieResult's shape, not a
/// general-purpose result framework.
///
/// Six cases, following the suggested set, but "NotFound" is not a third failure-ish bucket alongside Invalid -
/// it is the direct projection of AddMovieResult.NotFound: the IMDb id is well-formed, STRM Manager just does not
/// have (and, per its own metadata provider, cannot create) a movie for it. That is a real, reachable, useful-to-
/// distinguish outcome, not an edge case.
/// </summary>
public abstract record EnsureMovieResult
{
    private EnsureMovieResult()
    {
    }

    /// <summary>STRM Manager already had this movie - either the first lookup found it, or the race-recovery lookup after a 409 did.</summary>
    public sealed record Existing(Guid MovieId, string Status) : EnsureMovieResult;

    /// <summary>STRM Manager did not have this movie; it was created just now. No status - POST /api/movies's body never returns one.</summary>
    public sealed record Created(Guid MovieId) : EnsureMovieResult;

    /// <summary>The IMDb id was rejected as invalid - by the lookup, or by the add.</summary>
    public sealed record Invalid : EnsureMovieResult;

    /// <summary>The id is well-formed, but STRM Manager's metadata provider has no data for it (AddMovieResult.NotFound).</summary>
    public sealed record NotFound : EnsureMovieResult;

    /// <summary>STRM Manager could not be reached for one of the calls this operation made.</summary>
    public sealed record Unreachable(string Reason) : EnsureMovieResult;

    /// <summary>
    /// An unexpected outcome from either call, OR the one inconsistency this operation detects but does not resolve:
    /// AddMovie reported AlreadyExists (409) yet the single race-recovery lookup that follows could not confirm the
    /// movie exists. No loop or retry is attempted to reconcile that - it is reported as-is.
    /// </summary>
    public sealed record Error(string Reason) : EnsureMovieResult;
}
