using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Playback;

public static class PlaybackErrors
{
    /// <summary>
    /// The playback base URL is missing or unusable. One error for every cause on purpose, and its text never echoes the configured
    /// value (nor could it contain a provider URL): it names the setting to fix and nothing else.
    /// </summary>
    public static readonly Error PublicBaseUrlInvalid = Error.Failure(
        "Playback.PublicBaseUrlInvalid",
        "A stable playback URL cannot be built: Playback:PublicBaseUrl is not configured, or is not an absolute http/https URL "
        + "(without credentials, query or fragment).");
}
