using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Status;

/// <summary>
/// GET /api/status against a controlled dataset built directly through the DbContext
/// (fast, precise state - no need to drive every episode through the real pipeline just
/// to get it into a given status). Never asserts on stream URLs or SourceAttempt
/// internals - the endpoint doesn't expose them.
/// </summary>
public class GetStatusTests : IDisposable
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    private readonly ApiWebApplicationFactory _factory = new();
    private readonly HttpClient _client;
    private readonly FakeMetadataProvider _fakeMetadataProvider;
    private readonly IServiceProvider _services;

    public GetStatusTests()
    {
        _fakeMetadataProvider = new FakeMetadataProvider();

        WebApplicationFactory<Program> isolatedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IMetadataProvider>(_fakeMetadataProvider)));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetStatus_ReturnsExactEpisodeAndSeriesCounts()
    {
        // 2 Scheduled, 1 Pending, 1 Searching, 1 Validating, 8 Completed, 2 Unavailable, 1 Error.
        using (IServiceScope scope = _services.CreateScope())
        {
            CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            var series = StrmManager.Modules.Catalog.Domain.Series.Series.Create(
                new ExternalIds("tt00000501", null, null), "Paradise", null, 2025, UtcNow);
            context.Set<StrmManager.Modules.Catalog.Domain.Series.Series>().Add(series);

            var season = StrmManager.Modules.Catalog.Domain.Seasons.Season.Create(series.Id, 1, UtcNow);
            context.Set<StrmManager.Modules.Catalog.Domain.Seasons.Season>().Add(season);

            var episodes = new List<Episode>();
            int number = 1;

            for (int i = 0; i < 2; i++)
            {
                episodes.Add(CreateEpisode(season.Id, number++, UtcNow.AddDays(30))); // Scheduled
            }

            episodes.Add(CreateEpisode(season.Id, number++, UtcNow.AddDays(-1))); // Pending (auto-eligible)

            Episode searching = CreateEpisode(season.Id, number++, UtcNow.AddDays(-1));
            searching.StartSearching(UtcNow);
            episodes.Add(searching);

            Episode validating = CreateEpisode(season.Id, number++, UtcNow.AddDays(-1));
            validating.StartSearching(UtcNow);
            validating.StartValidating(UtcNow);
            episodes.Add(validating);

            for (int i = 0; i < 8; i++)
            {
                Episode completed = CreateEpisode(season.Id, number++, UtcNow.AddDays(-1));
                completed.StartSearching(UtcNow);
                completed.StartValidating(UtcNow);
                completed.MarkCompleted(UtcNow);
                episodes.Add(completed);
            }

            for (int i = 0; i < 2; i++)
            {
                Episode unavailable = CreateEpisode(season.Id, number++, UtcNow.AddDays(-1));
                unavailable.StartSearching(UtcNow);
                unavailable.MarkUnavailable(UtcNow, UtcNow.AddHours(6), "no streams returned");
                episodes.Add(unavailable);
            }

            Episode error = CreateEpisode(season.Id, number++, UtcNow.AddDays(-1));
            error.StartSearching(UtcNow);
            error.MarkError(UtcNow, "technical failure");
            episodes.Add(error);

            context.Set<Episode>().AddRange(episodes);
            await context.SaveChangesAsync();
        }

        HttpResponseMessage response = await _client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(status);

        Assert.False(status.Scheduler.Enabled); // ApiWebApplicationFactory sets Scheduling:Enabled=false
        Assert.Equal(2, status.Episodes.Scheduled);
        Assert.Equal(1, status.Episodes.Pending);
        Assert.Equal(1, status.Episodes.Searching);
        Assert.Equal(1, status.Episodes.Validating);
        Assert.Equal(8, status.Episodes.Completed);
        Assert.Equal(2, status.Episodes.Unavailable);
        Assert.Equal(1, status.Episodes.Error);
        Assert.Equal(1, status.Series.Active);
    }

    private static Episode CreateEpisode(Guid seasonId, int episodeNumber, DateTime releaseAtUtc) =>
        Episode.Schedule(
            seasonId, $"tt00000501:1:{episodeNumber}", $"Episode {episodeNumber}",
            seasonNumber: 1, episodeNumber: episodeNumber, runtime: TimeSpan.FromMinutes(52),
            releaseAtUtc: releaseAtUtc, utcNow: UtcNow);

    private sealed record SchedulerStatusResponse(bool Enabled);

    private sealed record EpisodeStatusCounts(
        int Scheduled, int Pending, int Searching, int Validating, int Completed, int Unavailable, int Error);

    private sealed record SeriesStatusCounts(int Active, int MetadataRefreshDue);

    private sealed record StatusResponse(SchedulerStatusResponse Scheduler, EpisodeStatusCounts Episodes, SeriesStatusCounts Series);
}
