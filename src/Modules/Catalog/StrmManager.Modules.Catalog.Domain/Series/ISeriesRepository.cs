namespace StrmManager.Modules.Catalog.Domain.Series;

public interface ISeriesRepository
{
    Task<Series?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Series?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default);

    void Insert(Series series);
}
