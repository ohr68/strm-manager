using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Appends exactly "/media/{movieId}/stream" to the configured PublicBaseUrl. It normalizes only what is needed to do that safely:
/// surrounding whitespace and trailing slashes are dropped (no double slash before "media"), while a deliberate base path such as
/// https://host/strm is kept. It refuses a base URL that is not an absolute http/https URL with a host, or that carries credentials
/// (which would end up in a .strm file), a query or a fragment (which would swallow the appended route).
///
/// Its only inputs are <see cref="PlaybackOptions"/> and the movie id: no HttpContext, no request Host or forwarded headers, no
/// provider data.
/// </summary>
public sealed class PlaybackUrlBuilder(IOptions<PlaybackOptions> options) : IPlaybackUrlBuilder
{
    public Result<string> Build(Guid movieId)
    {
        string? configured = options.Value.PublicBaseUrl?.Trim();

        if (string.IsNullOrEmpty(configured)
            || !Uri.TryCreate(configured, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(baseUri.Host)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return Result.Failure<string>(PlaybackErrors.PublicBaseUrlInvalid);
        }

        string basePath = baseUri.AbsolutePath.TrimEnd('/');

        return $"{baseUri.GetLeftPart(UriPartial.Authority)}{basePath}/media/{movieId:D}/stream";
    }
}
