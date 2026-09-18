namespace StrmManager.Modules.Catalog.Domain.Series;

public interface ISeriesRepository
{
    Task<Series?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Series?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Series>> GetDueForMetadataRefreshAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    Task<int> CountDueForMetadataRefreshAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    void Insert(Series series);
}
