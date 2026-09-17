using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.Infrastructure.Episodes;

internal sealed class EpisodeRepository(CatalogDbContext context) : IEpisodeRepository
{
    public async Task<Episode?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Episodes.SingleOrDefaultAsync(episode => episode.Id == id, cancellationToken);

    public async Task<Episode?> GetBySeasonAndNumberAsync(Guid seasonId, int episodeNumber, CancellationToken cancellationToken = default) =>
        await context.Episodes.SingleOrDefaultAsync(
            episode => episode.SeasonId == seasonId && episode.EpisodeNumber == episodeNumber,
            cancellationToken);

    public async Task<IReadOnlyList<Episode>> GetScheduledDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await context.Episodes
            .Where(episode => episode.Status == MediaStatus.Scheduled && episode.ReleaseAtUtc <= utcNow)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Episode>> GetRetryableAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await context.Episodes
            .Where(episode =>
                (episode.Status == MediaStatus.Unavailable || episode.Status == MediaStatus.Error) &&
                episode.NextAttemptAtUtc != null &&
                episode.NextAttemptAtUtc <= utcNow)
            .ToListAsync(cancellationToken);

    public void Insert(Episode episode) => context.Episodes.Add(episode);
}
