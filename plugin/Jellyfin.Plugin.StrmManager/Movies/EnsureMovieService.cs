using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StrmManager.Movies;

/// <summary>
/// See <see cref="IEnsureMovieService"/>. Pure composition of <see cref="IStrmManagerClient"/>'s two calls - it
/// never touches HttpClient, BaseUrl or JSON itself, and never catches OperationCanceledException, so a caller
/// cancellation propagates unchanged through whichever client call is in flight. Logs only its own final outcome
/// (IMDb id, outcome kind, movie id when there is one); the client already logs each individual call.
/// </summary>
public sealed partial class EnsureMovieService(IStrmManagerClient client, ILogger<EnsureMovieService> logger) : IEnsureMovieService
{
    public async Task<EnsureMovieResult> EnsureMovieAsync(string imdbId, CancellationToken cancellationToken)
    {
        MovieLookupResult lookup = await client.GetByImdbIdAsync(imdbId, cancellationToken).ConfigureAwait(false);

        switch (lookup)
        {
            case MovieLookupResult.Found found:
                return LogAndReturn(imdbId, new EnsureMovieResult.Existing(found.MovieId, found.Status));
            case MovieLookupResult.Invalid:
                return LogAndReturn(imdbId, new EnsureMovieResult.Invalid());
            case MovieLookupResult.Unreachable unreachable:
                return LogAndReturn(imdbId, new EnsureMovieResult.Unreachable(unreachable.Reason));
            case MovieLookupResult.Error error:
                return LogAndReturn(imdbId, new EnsureMovieResult.Error(error.Reason));
            case MovieLookupResult.NotFound:
                break; // the one case that continues below: nothing to add yet, try to add it
            default:
                return LogAndReturn(imdbId, new EnsureMovieResult.Error("Unexpected lookup outcome."));
        }

        AddMovieResult added = await client.AddMovieAsync(imdbId, cancellationToken).ConfigureAwait(false);

        switch (added)
        {
            case AddMovieResult.Created created:
                return LogAndReturn(imdbId, new EnsureMovieResult.Created(created.MovieId));
            case AddMovieResult.Invalid:
                return LogAndReturn(imdbId, new EnsureMovieResult.Invalid());
            case AddMovieResult.NotFound:
                return LogAndReturn(imdbId, new EnsureMovieResult.NotFound());
            case AddMovieResult.Unreachable unreachable:
                return LogAndReturn(imdbId, new EnsureMovieResult.Unreachable(unreachable.Reason));
            case AddMovieResult.Error error:
                return LogAndReturn(imdbId, new EnsureMovieResult.Error(error.Reason));
            case AddMovieResult.AlreadyExists:
                break; // the documented race: someone else created it between our lookup and our add
            default:
                return LogAndReturn(imdbId, new EnsureMovieResult.Error("Unexpected add outcome."));
        }

        // Exactly ONE race-recovery lookup - never a loop or retry (see EnsureMovieResult.Error's own doc comment).
        MovieLookupResult recovery = await client.GetByImdbIdAsync(imdbId, cancellationToken).ConfigureAwait(false);

        return recovery is MovieLookupResult.Found recoveredFound
            ? LogAndReturn(imdbId, new EnsureMovieResult.Existing(recoveredFound.MovieId, recoveredFound.Status))
            : LogAndReturn(imdbId, new EnsureMovieResult.Error("STRM Manager reported the movie already exists, but a follow-up lookup could not confirm it."));
    }

    private EnsureMovieResult LogAndReturn(string imdbId, EnsureMovieResult result)
    {
        switch (result)
        {
            case EnsureMovieResult.Existing existing:
                LogExisting(existing.MovieId, imdbId, existing.Status);
                break;
            case EnsureMovieResult.Created created:
                LogCreated(created.MovieId, imdbId);
                break;
            case EnsureMovieResult.Unreachable or EnsureMovieResult.Error:
                LogFailed(imdbId, result.GetType().Name);
                break;
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "EnsureMovie: STRM Manager already has movie {MovieId} for {ImdbId} ({Status})")]
    private partial void LogExisting(Guid movieId, string imdbId, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "EnsureMovie: created movie {MovieId} for {ImdbId}")]
    private partial void LogCreated(Guid movieId, string imdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "EnsureMovie: could not ensure {ImdbId} ({OutcomeKind})")]
    private partial void LogFailed(string imdbId, string outcomeKind);
}
