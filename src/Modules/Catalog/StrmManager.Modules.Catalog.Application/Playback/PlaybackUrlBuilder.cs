using Microsoft.Extensions.Options;
using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Builds stable playback URLs from Playback:PublicBaseUrl.
///
/// The configured base URL is normalized only as needed to append a playback
/// route safely. Surrounding whitespace and trailing slashes are removed,
/// while a deliberate base path such as https://host/strm is preserved.
///
/// The base URL must be an absolute http/https URL with a host and must not
/// contain credentials, query or fragment.
/// </summary>
public sealed class PlaybackUrlBuilder(IOptions<PlaybackOptions> options) : IPlaybackUrlBuilder
{
    public Result<string> Build(Guid movieId) =>
        BuildUrl($"media/{movieId:D}/stream");

    public Result<string> BuildEpisode(Guid episodeId) =>
        BuildUrl($"media/episodes/{episodeId:D}/stream");

    private Result<string> BuildUrl(string relativePath)
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

        return $"{baseUri.GetLeftPart(UriPartial.Authority)}{basePath}/{relativePath}";
    }
}
