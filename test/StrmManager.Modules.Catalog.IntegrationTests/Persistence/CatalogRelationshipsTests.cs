using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.IntegrationTests.Persistence;

public class CatalogRelationshipsTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private CatalogDbContext CreateContext()
    {
        IServiceScope scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    }

    [Fact]
    public async Task Season_WithExistingSeries_PersistsSuccessfully()
    {
        await using CatalogDbContext context = CreateContext();

        SeriesEntity series = SeriesEntity.Create(
            new ExternalIds($"tt{Guid.NewGuid():N}", null, null), "Series With Season", null, 2026, UtcNow);
        context.Set<SeriesEntity>().Add(series);
        await context.SaveChangesAsync();

        Season season = Season.Create(series.Id, 1, UtcNow);
        context.Set<Season>().Add(season);
        await context.SaveChangesAsync();

        Season? persisted = await context.Set<Season>().SingleOrDefaultAsync(s => s.Id == season.Id);
        Assert.NotNull(persisted);
        Assert.Equal(series.Id, persisted.SeriesId);
    }

    [Fact]
    public async Task Season_WithNonexistentSeriesId_CannotBePersisted()
    {
        await using CatalogDbContext context = CreateContext();

        Season season = Season.Create(Guid.NewGuid(), 1, UtcNow);
        context.Set<Season>().Add(season);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Episode_WithExistingSeason_PersistsSuccessfully()
    {
        await using CatalogDbContext context = CreateContext();

        SeriesEntity series = SeriesEntity.Create(
            new ExternalIds($"tt{Guid.NewGuid():N}", null, null), "Series With Episode", null, 2026, UtcNow);
        context.Set<SeriesEntity>().Add(series);
        await context.SaveChangesAsync();

        Season season = Season.Create(series.Id, 1, UtcNow);
        context.Set<Season>().Add(season);
        await context.SaveChangesAsync();

        Episode episode = Episode.Schedule(
            season.Id, "tt0000000:1:1", "Pilot", 1, 1, TimeSpan.FromMinutes(24), UtcNow.AddDays(-1), UtcNow);
        context.Set<Episode>().Add(episode);
        await context.SaveChangesAsync();

        Episode? persisted = await context.Set<Episode>().SingleOrDefaultAsync(e => e.Id == episode.Id);
        Assert.NotNull(persisted);
        Assert.Equal(season.Id, persisted.SeasonId);
    }

    [Fact]
    public async Task Episode_WithNonexistentSeasonId_CannotBePersisted()
    {
        await using CatalogDbContext context = CreateContext();

        Episode episode = Episode.Schedule(
            Guid.NewGuid(), "tt0000000:1:1", "Pilot", 1, 1, TimeSpan.FromMinutes(24), UtcNow.AddDays(-1), UtcNow);
        context.Set<Episode>().Add(episode);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
