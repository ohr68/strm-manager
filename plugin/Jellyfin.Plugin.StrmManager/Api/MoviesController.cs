using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.StrmManager.Api;

/// <summary>
/// Admin-only verification surface for the plugin's STRM Manager client. This exists to manually exercise <see
/// cref="IStrmManagerClient"/> from a running Jellyfin instance - it is not the plugin's eventual admin UI/trigger
/// and nothing renders it. Each action is a thin, independent wrapper around one client call; P1's lookup action is
/// unchanged by P2.
/// </summary>
[ApiController]
[Route("StrmManager")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class MoviesController(IStrmManagerClient client) : ControllerBase
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
}
