namespace StrmManager.Modules.Catalog.Domain.Movies;

public interface IMovieRepository
{
    Task<Movie?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Movie?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Movie>> GetScheduledDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Movie>> GetRetryableAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    void Insert(Movie movie);
}
