namespace StrmManager.Modules.Catalog.Domain.Seasons;

public interface ISeasonRepository
{
    Task<Season?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Season?> GetBySeriesAndNumberAsync(Guid seriesId, int number, CancellationToken cancellationToken = default);

    void Insert(Season season);
}
