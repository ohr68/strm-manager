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

    public async Task<IReadOnlyList<Episode>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default) =>
        await context.Episodes
            .Where(episode => episode.SeasonId == seasonId)
            .OrderBy(episode => episode.EpisodeNumber)
            .ToListAsync(cancellationToken);

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

    public async Task<IReadOnlyList<Episode>> GetStaleProcessingAsync(DateTime staleThresholdUtc, CancellationToken cancellationToken = default) =>
        await context.Episodes
            .Where(episode =>
                (episode.Status == MediaStatus.Searching || episode.Status == MediaStatus.Validating) &&
                episode.UpdatedAtUtc <= staleThresholdUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Episode>> GetPendingForProcessingAsync(int limit, CancellationToken cancellationToken = default) =>
        await context.Episodes
            .Where(episode => episode.Status == MediaStatus.Pending)
            .OrderBy(episode => episode.UpdatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<MediaStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        List<StatusCount> counts = await context.Episodes
            .GroupBy(episode => episode.Status)
            .Select(group => new StatusCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(count => count.Status, count => count.Count);
    }

    public async Task<IReadOnlyList<Episode>> GetByStatusAsync(MediaStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        IQueryable<Episode> query = context.Episodes;

        if (status is { } value)
        {
            query = query.Where(episode => episode.Status == value);
        }

        return await query
            .OrderByDescending(episode => episode.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Insert(Episode episode) => context.Episodes.Add(episode);

    private sealed record StatusCount(MediaStatus Status, int Count);
}
