using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using SeriesEntity = StrmManager.Modules.Catalog.Domain.Series.Series;

namespace StrmManager.Modules.Catalog.IntegrationTests.Persistence;

public class EpisodeRepositoryTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow =
        new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPendingForProcessing_FirstAttemptHasPriorityOverOlderRetry()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        CatalogDbContext context =
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        IEpisodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IEpisodeRepository>();

        SeriesEntity series = SeriesEntity.Create(
            new ExternalIds($"tt{Guid.NewGuid():N}", null, null),
            "Priority Test",
            null,
            2026,
            UtcNow.AddDays(-30));

        context.Set<SeriesEntity>().Add(series);

        Season season = Season.Create(
            series.Id,
            1,
            UtcNow.AddDays(-30));

        context.Set<Season>().Add(season);

        Episode retry = Episode.Schedule(
            season.Id,
            $"{series.ExternalIds.ImdbId}:1:1",
            "Older Retry",
            1,
            1,
            TimeSpan.FromMinutes(45),
            UtcNow.AddDays(-10),
            UtcNow.AddDays(-10));

        // Produce a real retry Pending state through domain transitions:
        // Pending -> Searching -> Unavailable -> Pending.
        retry.StartSearching(UtcNow.AddHours(-2));
        retry.MarkUnavailable(
            UtcNow.AddHours(-2),
            UtcNow.AddHours(-1),
            "no candidates");
        retry.Retry(UtcNow.AddHours(-1));

        Episode firstAttempt = Episode.Schedule(
            season.Id,
            $"{series.ExternalIds.ImdbId}:1:2",
            "New Release",
            1,
            2,
            TimeSpan.FromMinutes(45),
            UtcNow.AddMinutes(-1),
            UtcNow);

        context.Set<Episode>().AddRange(retry, firstAttempt);

        await context.SaveChangesAsync();

        IReadOnlyList<Episode> pending =
            await repository.GetPendingForProcessingAsync(1);

        Episode selected = Assert.Single(pending);

        Assert.Equal(firstAttempt.Id, selected.Id);
        Assert.Equal(0, selected.AttemptCount);
        Assert.Equal(1, retry.AttemptCount);
    }

    [Fact]
    public async Task GetPendingForProcessing_WithinSamePriority_SelectsOldestPendingFirst()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        CatalogDbContext context =
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        IEpisodeRepository repository =
            scope.ServiceProvider.GetRequiredService<IEpisodeRepository>();

        SeriesEntity series = SeriesEntity.Create(
            new ExternalIds($"tt{Guid.NewGuid():N}", null, null),
            "Pending Order Test",
            null,
            2026,
            UtcNow.AddDays(-30));

        context.Set<SeriesEntity>().Add(series);

        Season season = Season.Create(
            series.Id,
            1,
            UtcNow.AddDays(-30));

        context.Set<Season>().Add(season);

        Episode oldest = Episode.Schedule(
            season.Id,
            $"{series.ExternalIds.ImdbId}:1:1",
            "Oldest Pending",
            1,
            1,
            TimeSpan.FromMinutes(45),
            UtcNow.AddDays(-2),
            UtcNow.AddHours(-2));

        Episode newest = Episode.Schedule(
            season.Id,
            $"{series.ExternalIds.ImdbId}:1:2",
            "Newest Pending",
            1,
            2,
            TimeSpan.FromMinutes(45),
            UtcNow.AddDays(-1),
            UtcNow.AddHours(-1));

        context.Set<Episode>().AddRange(oldest, newest);

        await context.SaveChangesAsync();

        IReadOnlyList<Episode> pending =
            await repository.GetPendingForProcessingAsync(1);

        Episode selected = Assert.Single(pending);

        Assert.Equal(oldest.Id, selected.Id);
        Assert.Equal(0, selected.AttemptCount);
        Assert.Equal(0, newest.AttemptCount);
    }
}
