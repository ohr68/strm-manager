using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.Catalog.Domain.StrmFiles;

/// <summary>
/// Records the on-disk .strm file that was written for an episode or movie,
/// so it can be located/overwritten without recomputing the Jellyfin-compatible path.
/// </summary>
public sealed class StrmFile : Entity
{
    private StrmFile()
    {
    }

    public Guid Id { get; private set; }

    public Guid? EpisodeId { get; private set; }

    public Guid? MovieId { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static StrmFile ForEpisode(Guid episodeId, string path, DateTime utcNow) =>
        new()
        {
            Id = Guid.NewGuid(),
            EpisodeId = episodeId,
            Path = path,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

    public static StrmFile ForMovie(Guid movieId, string path, DateTime utcNow) =>
        new()
        {
            Id = Guid.NewGuid(),
            MovieId = movieId,
            Path = path,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

    public void Overwrite(string path, DateTime utcNow)
    {
        Path = path;
        UpdatedAtUtc = utcNow;
    }
}
