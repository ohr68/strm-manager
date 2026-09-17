namespace StrmManager.Modules.Catalog.Domain.StrmFiles;

public interface IStrmFileRepository
{
    Task<StrmFile?> GetForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default);

    Task<StrmFile?> GetForMovieAsync(Guid movieId, CancellationToken cancellationToken = default);

    void Insert(StrmFile strmFile);
}
