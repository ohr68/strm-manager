namespace StrmManager.Modules.Catalog.Domain.Episodes;

public interface IEpisodeRepository
{
    Task<Episode?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Episode?> GetBySeasonAndNumberAsync(Guid seasonId, int episodeNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Episode>> GetBySeasonAsync(Guid seasonId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Episode>> GetScheduledDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Episode>> GetRetryableAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    void Insert(Episode episode);
}
