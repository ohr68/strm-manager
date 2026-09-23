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
}
