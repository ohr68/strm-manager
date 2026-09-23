using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.StrmManager.Api;

/// <summary>
/// UI-2's catalog proxy - the browser never talks to the configured STRM Manager backend directly, only to this
/// plugin controller, which already knows BaseUrl. Admin-only, same as MoviesController/LibrariesController; this
/// stays part of the native Dashboard admin UI for this slice (see the UI-2 report) - no non-admin architecture
/// yet. Cards returned here are metadata only: no AddMovie/EnsureMovie/ProcessMovie/.strm/scan/playback happens by
/// browsing the catalog.
///
/// Response bodies use explicit lowercase-first-letter anonymous properties (not a PascalCase record) - this
/// Jellyfin host does not apply a camelCase JSON policy (confirmed the hard way fixing UI-1's LibraryOption bug),
/// so only an already-lowercase C# name is guaranteed to reach the page as written.
/// </summary>
[ApiController]
[Route("StrmManager")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class CatalogController(IStrmManagerClient client) : ControllerBase
{
    [HttpGet("Catalog/Movies/Popular")]
    public async Task<IActionResult> GetPopularMovies(CancellationToken cancellationToken)
    {
        CatalogMoviesResult result = await client.GetPopularMoviesAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            CatalogMoviesResult.Found found => Ok(new
            {
                movies = found.Movies.Select(movie => new
                {
                    externalId = movie.ExternalId,
                    title = movie.Title,
                    year = movie.Year,
                    posterUrl = movie.PosterUrl,
                }),
            }),
            CatalogMoviesResult.Unreachable unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = unreachable.Reason }),
            CatalogMoviesResult.Error error => StatusCode(StatusCodes.Status502BadGateway, new { reason = error.Reason }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// UI-4's multi-row Discover proxy. Pure pass-through, same as GetPopularMovies above (kept for compatibility,
    /// unchanged) - row identity/order/which rows exist is entirely the backend's decision (see UI-4a's
    /// MovieRowPolicy); this action never filters, re-orders, or invents rows.
    /// </summary>
    [HttpGet("Catalog/Movies/Rows")]
    public async Task<IActionResult> GetMovieRows(CancellationToken cancellationToken)
    {
        MovieRowsResult result = await client.GetMovieRowsAsync(cancellationToken).ConfigureAwait(false);

        return result switch
        {
            MovieRowsResult.Found found => Ok(new
            {
                rows = found.Rows.Select(row => new
                {
                    id = row.Id,
                    name = row.Name,
                    movies = row.Movies.Select(movie => new
                    {
                        externalId = movie.ExternalId,
                        title = movie.Title,
                        year = movie.Year,
                        posterUrl = movie.PosterUrl,
                    }),
                }),
            }),
            MovieRowsResult.Unreachable unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = unreachable.Reason }),
            MovieRowsResult.Error error => StatusCode(StatusCodes.Status502BadGateway, new { reason = error.Reason }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// UI-5c's Discover search proxy. Pure pass-through like GetPopularMovies/GetMovieRows above - no
    /// AddMovie/EnsureMovie/ProcessMovie/scan happens by searching, the browser only ever gets metadata cards back.
    /// An empty query string reaches the backend as-is and comes back 400 (Invalid), same as the backend's own
    /// validation contract - this action never pre-validates or trims itself.
    /// </summary>
    [HttpGet("Catalog/Movies/Search")]
    public async Task<IActionResult> SearchMovies([FromQuery] string? query, CancellationToken cancellationToken)
    {
        SearchMoviesResult result = await client.SearchMoviesAsync(query ?? string.Empty, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            SearchMoviesResult.Found found => Ok(new
            {
                movies = found.Movies.Select(movie => new
                {
                    externalId = movie.ExternalId,
                    title = movie.Title,
                    year = movie.Year,
                    posterUrl = movie.PosterUrl,
                }),
            }),
            SearchMoviesResult.Invalid => BadRequest(),
            SearchMoviesResult.Unreachable unreachable => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason = unreachable.Reason }),
            SearchMoviesResult.Error error => StatusCode(StatusCodes.Status502BadGateway, new { reason = error.Reason }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
