using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Episodes;

/// <summary>POST /api/episodes/{id}/retry - flips the episode back to Pending; the actual reprocessing is a separate concern (manual API call or EpisodeProcessingWorker), not this endpoint.</summary>
public class RetryEpisodeTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RetryEpisode_FromUnavailable_ReturnsToPendingWithoutReprocessing()
    {
        HttpClient client = factory.CreateClient();
        Guid episodeId = await CreateUnavailableEpisodeAsync();

        HttpResponseMessage response = await client.PostAsync($"/api/episodes/{episodeId}/retry", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RetryResponse>();
        Assert.NotNull(result);
        Assert.Equal("Pending", result.Status);

        using IServiceScope scope = factory.Services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        Assert.Equal(MediaStatus.Pending, episode.Status);
    }

    [Fact]
    public async Task RetryEpisode_FromPending_Fails()
    {
        HttpClient client = factory.CreateClient();

        using IServiceScope setupScope = factory.Services.CreateScope();
        CatalogDbContext setupContext = setupScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (Guid episodeId, _) = await CreateEpisodeAsync(setupContext, releaseAtUtc: UtcNow.AddDays(-1));

        HttpResponseMessage response = await client.PostAsync($"/api/episodes/{episodeId}/retry", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RetryEpisode_UnknownEpisode_ReturnsNotFound()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync($"/api/episodes/{Guid.NewGuid()}/retry", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreateUnavailableEpisodeAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (Guid episodeId, Episode episode) = await CreateEpisodeAsync(context, releaseAtUtc: UtcNow.AddDays(-1));

        episode.StartSearching(UtcNow);
        episode.MarkUnavailable(UtcNow, UtcNow.AddHours(-1), "no streams returned"); // already due, but that's irrelevant to this endpoint
        await context.SaveChangesAsync();

        return episodeId;
    }

    private static async Task<(Guid EpisodeId, Episode Episode)> CreateEpisodeAsync(CatalogDbContext context, DateTime releaseAtUtc)
    {
        var series = StrmManager.Modules.Catalog.Domain.Series.Series.Create(
            new ExternalIds($"tt{Guid.NewGuid():N}"[..10], null, null), "Paradise", null, 2025, UtcNow);
        context.Set<StrmManager.Modules.Catalog.Domain.Series.Series>().Add(series);

        var season = StrmManager.Modules.Catalog.Domain.Seasons.Season.Create(series.Id, 1, UtcNow);
        context.Set<StrmManager.Modules.Catalog.Domain.Seasons.Season>().Add(season);

        Episode episode = Episode.Schedule(
            season.Id, $"{series.ExternalIds.ImdbId}:1:1", "Episode One",
            seasonNumber: 1, episodeNumber: 1, runtime: TimeSpan.FromMinutes(52),
            releaseAtUtc: releaseAtUtc, utcNow: UtcNow);
        context.Set<Episode>().Add(episode);

        await context.SaveChangesAsync();

        return (episode.Id, episode);
    }

    private sealed record RetryResponse(Guid EpisodeId, string Status);
}
