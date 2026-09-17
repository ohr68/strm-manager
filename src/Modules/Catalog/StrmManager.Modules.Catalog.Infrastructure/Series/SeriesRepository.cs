using Microsoft.EntityFrameworkCore;
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

    public void Insert(SeriesEntity series) => context.Series.Add(series);
}
