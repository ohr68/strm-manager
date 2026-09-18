using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;
using ISeriesRepository = StrmManager.Modules.Catalog.Domain.Series.ISeriesRepository;

namespace StrmManager.Modules.Catalog.Infrastructure.Series;

internal sealed class SeriesRepository(CatalogDbContext context) : ISeriesRepository
{
    public async Task<SeriesEntity?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Series.SingleOrDefaultAsync(series => series.Id == id, cancellationToken);

    public async Task<SeriesEntity?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default) =>
        await context.Series.SingleOrDefaultAsync(series => series.ExternalIds.ImdbId == imdbId, cancellationToken);

    public async Task<IReadOnlyList<SeriesEntity>> GetDueForMetadataRefreshAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await context.Series
            .Where(series =>
                series.Status == SeriesStatus.Active &&
                (series.NextMetadataRefreshAtUtc == null || series.NextMetadataRefreshAtUtc <= utcNow))
            .ToListAsync(cancellationToken);

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        await context.Series.CountAsync(series => series.Status == SeriesStatus.Active, cancellationToken);

    public async Task<int> CountDueForMetadataRefreshAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await context.Series.CountAsync(
            series =>
                series.Status == SeriesStatus.Active &&
                (series.NextMetadataRefreshAtUtc == null || series.NextMetadataRefreshAtUtc <= utcNow),
            cancellationToken);

    public void Insert(SeriesEntity series) => context.Series.Add(series);
}
