using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Modules.MediaProcessing.Application.StrmGeneration;

public interface IStrmWriter
{
    /// <summary>Returns the absolute path written on success. sourceUrl is written as the file's content, never returned/logged.</summary>
    Task<Result<string>> WriteEpisodeAsync(
        EpisodeStrmReference reference,
        string sourceUrl,
        CancellationToken cancellationToken = default);

    /// <summary>Same contract as <see cref="WriteEpisodeAsync"/>, for a movie: the absolute path written on success; sourceUrl is the file's content, never returned/logged.</summary>
    Task<Result<string>> WriteMovieAsync(
        MovieStrmReference reference,
        string sourceUrl,
        CancellationToken cancellationToken = default);
}
