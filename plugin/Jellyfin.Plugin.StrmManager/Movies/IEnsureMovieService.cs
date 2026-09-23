using Jellyfin.Plugin.StrmManager.StrmManagerClient;

namespace Jellyfin.Plugin.StrmManager.Movies;

/// <summary>
/// Composes <see cref="IStrmManagerClient"/>'s lookup and add primitives into one idempotent operation: ensure STRM
/// Manager has this movie, and return its resolved id. This is composition only - no HTTP, no BaseUrl handling, no
/// JSON parsing, no status-code mapping; all of that stays in StrmManagerClient. Not processing/provisioning.
/// </summary>
public interface IEnsureMovieService
{
    /// <summary>
    /// Looks the movie up by IMDb id; if STRM Manager does not have it, adds it. Handles exactly the lookup/add race
    /// documented on <see cref="EnsureMovieResult"/> with a single follow-up lookup - never a loop or retry. Never
    /// throws for an expected outcome - those are all represented in the returned <see cref="EnsureMovieResult"/>. A
    /// cancellation requested by <paramref name="cancellationToken"/> propagates as a normal
    /// <see cref="OperationCanceledException"/>, not as a result value (the same rule <see cref="IStrmManagerClient"/>
    /// follows, which this method composes without adding its own cancellation handling).
    /// </summary>
    Task<EnsureMovieResult> EnsureMovieAsync(string imdbId, CancellationToken cancellationToken);
}
