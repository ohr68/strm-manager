using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Playback;

namespace StrmManager.Modules.Catalog.Presentation.Episodes;

/// <summary>
/// The stable playback URL an episode's .strm points at:
/// GET/HEAD media/episodes/{episodeId}/stream resolves a fresh, validated
/// provider source just in time and redirects the client to it.
///
/// STRM Manager never proxies, reads, buffers or forwards the media.
///
/// Contract:
///   resolved                                      -> 307 + Location
///   unknown episode, or an episode not Completed  -> 404
///   unavailable / coordination failure            -> 503
///   resolution capacity exhausted                 -> 429 + Retry-After: 5
///
/// Every response has an empty body and Cache-Control: no-store.
///
/// The redirect is deliberately written by hand. The provider URL is revealed
/// only when assigning the Location header. Do not replace this with a
/// framework redirect helper, and do not add logging of the destination.
/// </summary>
internal sealed class StreamEpisode : IEndpoint
{
    private const string RetryAfterSeconds = "5";

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods(
            "media/episodes/{episodeId:guid}/stream",
            [HttpMethods.Get, HttpMethods.Head],
            HandleAsync);

    private static async Task HandleAsync(
        Guid episodeId,
        IEpisodePlaybackResolutionCoordinator coordinator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.Response;

        response.Headers.CacheControl = "no-store";
        response.ContentLength = 0;

        PlaybackCoordinationResult result =
            await coordinator.ResolveAsync(
                episodeId,
                cancellationToken);

        if (result is PlaybackCoordinationResult.Completed
            {
                Resolution: PlaybackResolutionResult.Resolved resolved
            }
            && IsSafeHeaderValue(resolved.Location))
        {
            response.StatusCode =
                StatusCodes.Status307TemporaryRedirect;

            response.Headers.Location =
                resolved.Location.Reveal();

            return;
        }

        response.StatusCode = result switch
        {
            PlaybackCoordinationResult.Completed
            {
                Resolution: PlaybackResolutionResult.NotFound
            } => StatusCodes.Status404NotFound,

            PlaybackCoordinationResult.Busy =>
                StatusCodes.Status429TooManyRequests,

            _ =>
                StatusCodes.Status503ServiceUnavailable,
        };

        if (result is PlaybackCoordinationResult.Busy)
        {
            response.Headers.RetryAfter =
                RetryAfterSeconds;
        }
    }

    private static bool IsSafeHeaderValue(
        PlaybackLocation location)
    {
        foreach (char character in location.Reveal())
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}
