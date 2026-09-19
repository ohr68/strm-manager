using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>Builds the stable playback URL a movie's .strm contains: {PublicBaseUrl}/media/{movieId}/stream (ADR-015).</summary>
public interface IPlaybackUrlBuilder
{
    /// <summary>
    /// The URL for a movie, or <see cref="PlaybackErrors.PublicBaseUrlInvalid"/> if the configured base URL cannot be used. It depends only
    /// on the configured base URL and the movie's internal id - never on a request, and never on any provider data.
    /// </summary>
    Result<string> Build(Guid movieId);
}
