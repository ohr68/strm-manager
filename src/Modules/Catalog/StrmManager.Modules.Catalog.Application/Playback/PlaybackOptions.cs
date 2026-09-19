namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Explicit playback configuration (ADR-015): where a media server reaches THIS service. Nothing is inferred - not from a
/// request's Host or X-Forwarded-* headers, not from localhost, not from the server's binding.
///
/// Deliberately NOT validated at startup: a deployment with no PublicBaseUrl must still start (every non-playback feature keeps
/// working). The value is checked where it matters instead - IPlaybackUrlBuilder refuses to build a URL from a missing or unusable
/// value, and ProcessMovie fails BEFORE claiming a movie rather than write anything else into a .strm.
/// </summary>
public sealed class PlaybackOptions
{
    public const string SectionName = "Playback";

    /// <summary>
    /// The absolute http or https URL of this service as the media server (Jellyfin) sees it, e.g. http://strm-manager:8080 or
    /// https://media.example.com. A base path is allowed and kept; a trailing slash is not needed. Environment variable form:
    /// Playback__PublicBaseUrl.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
