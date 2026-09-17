using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.Infrastructure.Movies;

internal sealed class MovieRepository(CatalogDbContext context) : IMovieRepository
{
    public async Task<Movie?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Movies.SingleOrDefaultAsync(movie => movie.Id == id, cancellationToken);

    public async Task<Movie?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default) =>
        await context.Movies.SingleOrDefaultAsync(movie => movie.ExternalIds.ImdbId == imdbId, cancellationToken);

    public async Task<IReadOnlyList<Movie>> GetScheduledDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await context.Movies
            .Where(movie => movie.Status == MediaStatus.Scheduled && movie.ReleaseAtUtc <= utcNow)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Movie>> GetRetryableAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await context.Movies
            .Where(movie =>
                (movie.Status == MediaStatus.Unavailable || movie.Status == MediaStatus.Error) &&
                movie.NextAttemptAtUtc != null &&
                movie.NextAttemptAtUtc <= utcNow)
            .ToListAsync(cancellationToken);

    public void Insert(Movie movie) => context.Movies.Add(movie);
}
