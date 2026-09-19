using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrmManager.Common.Presentation.Endpoints;
using StrmManager.Modules.Catalog.Application.Playback;

namespace StrmManager.Modules.Catalog.Presentation.Movies;

/// <summary>
/// The stable playback URL a movie's .strm points at (ADR-015): GET/HEAD media/{movieId}/stream resolves a fresh, validated
/// provider source just in time and answers with a redirect to it. STRM Manager never touches the media itself - it does
/// not proxy, read, buffer or forward a byte - the client follows the redirect and talks to the provider directly.
///
/// The contract, deliberately generic so nothing about the movie or the provider can be learned from a failure:
///   resolved                                   -> 307, Location: the provider URL
///   unknown movie, or a movie not Completed    -> 404 (indistinguishable)
///   unavailable / any failure / anything else  -> 503
///   resolution capacity exhausted              -> 429, Retry-After: 5
/// Every response has an empty body and Cache-Control: no-store; only the 307 carries a Location. Range is not interpreted:
/// the client sends it to the provider after following the redirect. Other methods are refused by routing (405).
///
/// The redirect is written by hand on purpose. The provider URL is revealed at exactly one place - the assignment to the
/// Location header - and passes through no framework redirect helper, because those log their destination
/// ("Executing RedirectResult, redirecting to {Destination}"). Do not "tidy" this into Results.Redirect. There is no
/// logger here either, so nothing in this class can log a URL.
///
/// A caller that disconnects makes the coordinator throw OperationCanceledException from ResolveAsync (only that caller's
/// wait is cancelled - the shared resolution carries on). It is deliberately not caught: request cancellation is not a 503.
/// </summary>
internal sealed class StreamMovie : IEndpoint
{
    private const string RetryAfterSeconds = "5"; // matches the default QueueWait; fixed, not derived from any provider data

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapMethods("media/{movieId:guid}/stream", [HttpMethods.Get, HttpMethods.Head], HandleAsync);

    private static async Task HandleAsync(
        Guid movieId,
        IPlaybackResolutionCoordinator coordinator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.Response;

        // On every outcome of this endpoint, and never a body.
        response.Headers.CacheControl = "no-store";
        response.ContentLength = 0;

        PlaybackCoordinationResult result = await coordinator.ResolveAsync(movieId, cancellationToken);

        if (result is PlaybackCoordinationResult.Completed { Resolution: PlaybackResolutionResult.Resolved resolved }
            && IsSafeHeaderValue(resolved.Location))
        {
            response.StatusCode = StatusCodes.Status307TemporaryRedirect;

            // The one and only place the provider URL leaves the service.
            response.Headers.Location = resolved.Location.Reveal();
            return;
        }

        response.StatusCode = result switch
        {
            PlaybackCoordinationResult.Completed { Resolution: PlaybackResolutionResult.NotFound } => StatusCodes.Status404NotFound,
            PlaybackCoordinationResult.Busy => StatusCodes.Status429TooManyRequests,

            // Unavailable, every coordination failure, and (fail closed) a Resolved whose URL cannot be sent as a header.
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        if (result is PlaybackCoordinationResult.Busy)
        {
            response.Headers.RetryAfter = RetryAfterSeconds;
        }
    }

    /// <summary>
    /// A header value may only be printable ASCII. Anything else (a control character, a line break, non-ASCII text) could split
    /// the response or be rejected by the server after the fact, so such a URL is treated as unavailable instead. The value is
    /// inspected here and not kept, formatted or logged.
    /// </summary>
    private static bool IsSafeHeaderValue(PlaybackLocation location)
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
