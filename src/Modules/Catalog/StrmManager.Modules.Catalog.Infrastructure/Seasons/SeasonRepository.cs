using Microsoft.EntityFrameworkCore;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.Infrastructure.Seasons;

internal sealed class SeasonRepository(CatalogDbContext context) : ISeasonRepository
{
    public async Task<Season?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Seasons.SingleOrDefaultAsync(season => season.Id == id, cancellationToken);

    public async Task<Season?> GetBySeriesAndNumberAsync(Guid seriesId, int number, CancellationToken cancellationToken = default) =>
        await context.Seasons.SingleOrDefaultAsync(
            season => season.SeriesId == seriesId && season.Number == number,
            cancellationToken);

    public async Task<IReadOnlyList<Season>> GetAllBySeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
        await context.Seasons
            .Where(season => season.SeriesId == seriesId)
            .OrderBy(season => season.Number)
            .ToListAsync(cancellationToken);

    public void Insert(Season season) => context.Seasons.Add(season);
}
