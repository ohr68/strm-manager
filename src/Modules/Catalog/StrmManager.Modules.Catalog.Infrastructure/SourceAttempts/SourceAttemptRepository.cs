using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.Infrastructure.SourceAttempts;

internal sealed class SourceAttemptRepository(CatalogDbContext context) : ISourceAttemptRepository
{
    public async Task<IReadOnlyList<SourceAttempt>> GetForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default) =>
        await context.SourceAttempts
            .Where(attempt => attempt.EpisodeId == episodeId)
            .OrderBy(attempt => attempt.AttemptedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SourceAttempt>> GetForMovieAsync(Guid movieId, CancellationToken cancellationToken = default) =>
        await context.SourceAttempts
            .Where(attempt => attempt.MovieId == movieId)
            .OrderBy(attempt => attempt.AttemptedAtUtc)
            .ToListAsync(cancellationToken);

    public void Insert(SourceAttempt attempt) => context.SourceAttempts.Add(attempt);
}
