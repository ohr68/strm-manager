using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Builds the stable playback URLs stored in .strm files.
/// </summary>
public interface IPlaybackUrlBuilder
{
    /// <summary>
    /// Builds the stable movie playback URL:
    /// {PublicBaseUrl}/media/{movieId}/stream.
    /// </summary>
    Result<string> Build(Guid movieId);

    /// <summary>
    /// Builds the stable episode playback URL:
    /// {PublicBaseUrl}/media/episodes/{episodeId}/stream.
    /// </summary>
    Result<string> BuildEpisode(Guid episodeId);
}
