namespace StrmManager.Modules.Catalog.Domain.SourceAttempts;

public interface ISourceAttemptRepository
{
    Task<IReadOnlyList<SourceAttempt>> GetForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceAttempt>> GetForMovieAsync(Guid movieId, CancellationToken cancellationToken = default);

    void Insert(SourceAttempt attempt);
}
