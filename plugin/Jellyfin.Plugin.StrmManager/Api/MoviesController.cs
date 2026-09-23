using Jellyfin.Plugin.StrmManager.Movies;
using Jellyfin.Plugin.StrmManager.Processing;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StrmManager.Api;

/// <summary>
/// Admin-only verification surface for the plugin's STRM Manager client and orchestration. This exists to manually
/// exercise <see cref="IStrmManagerClient"/>/<see cref="IEnsureMovieService"/>/<see cref="IProcessMovieQueue"/> from
/// a running Jellyfin instance - it is not the plugin's eventual admin UI/trigger and nothing renders it. Each
/// action is a thin, independent wrapper around one call; P1's lookup and P2's add actions are unchanged by P3/P5.
/// </summary>
[ApiController]
[Route("StrmManager")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed partial class MoviesController(
    IStrmManagerClient client,
    IEnsureMovieService ensureMovieService,
    IProcessMovieQueue processMovieQueue,
    ILogger<MoviesController> logger) : ControllerBase
{
    [HttpGet("Movies/By-Imdb/{imdbId}")]
    public async Task<IActionResult> GetByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        MovieLookupResult result = await client.GetByImdbIdAsync(imdbId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            MovieLookupResult.Found found => Ok(new
            {
                movieId = found.MovieId,
                imdbId = found.ImdbId,
                title = found.Title,
                year = found.Year,
                status = found.Status,
            }),
            MovieLookupResult.NotFound => NotFound(),
            MovieLookupResult.Invalid => BadRequest(),
            MovieLookupResult.Unreachable unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = unreachable.Reason }),
            MovieLookupResult.Error error => StatusCode(StatusCodes.Status502BadGateway, new { reason = error.Reason }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>P2 verification only - a real, mutating call to STRM Manager. Not combined with the lookup action above.</summary>
    [HttpPost("Movies/Add/{imdbId}")]
    public async Task<IActionResult> AddMovie(string imdbId, CancellationToken cancellationToken)
    {
        AddMovieResult result = await client.AddMovieAsync(imdbId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            AddMovieResult.Created created => Created($"StrmManager/Movies/By-Imdb/{imdbId}", new { movieId = created.MovieId }),
            AddMovieResult.AlreadyExists => Conflict(),
            AddMovieResult.Invalid => BadRequest(),
            AddMovieResult.NotFound => NotFound(),
            AddMovieResult.Unreachable unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = unreachable.Reason }),
            AddMovieResult.Error error => StatusCode(StatusCodes.Status502BadGateway, new { reason = error.Reason }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>P3 verification only - composes the two calls above via IEnsureMovieService. Not combined with either action above.</summary>
    [HttpPost("Movies/Ensure/{imdbId}")]
    public async Task<IActionResult> EnsureMovie(string imdbId, CancellationToken cancellationToken)
    {
        EnsureMovieResult result = await ensureMovieService.EnsureMovieAsync(imdbId, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            EnsureMovieResult.Existing existing => Ok(new { movieId = existing.MovieId, status = existing.Status, created = false }),
            EnsureMovieResult.Created created => Created($"StrmManager/Movies/By-Imdb/{imdbId}", new { movieId = created.MovieId, created = true }),
            EnsureMovieResult.Invalid => BadRequest(),
            EnsureMovieResult.NotFound => NotFound(),
            EnsureMovieResult.Unreachable unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = unreachable.Reason }),
            EnsureMovieResult.Error error => StatusCode(StatusCodes.Status502BadGateway, new { reason = error.Reason }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// P5's first producer - enqueues an already-known Movie into ProcessMovieWorker's queue and returns
    /// immediately. Never calls ProcessMovieAsync itself, and never looks the movie up first: a syntactically valid
    /// but unknown/wrong-state id is a worker/P4 outcome (NotFound/InvalidTransition/AlreadyBeingProcessed/etc.),
    /// not an enqueue outcome - this action only ever reports queue admission, never processing. Synchronous and
    /// takes no CancellationToken - TryEnqueue is non-blocking, and the browser/request's lifetime must never reach
    /// the queued operation (see the P5 report's cancellation-ownership rule).
    /// </summary>
    [HttpPost("Movies/Process/{movieId:guid}")]
    public IActionResult ProcessMovie(Guid movieId)
    {
        ProcessMovieEnqueueResult result = processMovieQueue.TryEnqueue(movieId);

        LogEnqueueResult(movieId, result);

        return result switch
        {
            ProcessMovieEnqueueResult.Accepted => Accepted(new { movieId, queueStatus = result.ToString() }),
            ProcessMovieEnqueueResult.AlreadyQueued => Conflict(new { movieId, queueStatus = result.ToString() }),
            ProcessMovieEnqueueResult.Full => StatusCode(StatusCodes.Status429TooManyRequests, new { movieId, queueStatus = result.ToString() }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Process Movie enqueue {MovieId}: {QueueStatus}")]
    private partial void LogEnqueueResult(Guid movieId, ProcessMovieEnqueueResult queueStatus);
}
