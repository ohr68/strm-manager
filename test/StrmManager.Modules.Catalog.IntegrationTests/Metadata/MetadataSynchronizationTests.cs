using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Seasons;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Metadata;

/// <summary>
/// End-to-end coverage of the Phase 2 flow through the real API: add a series, have its
/// metadata retrieved and persisted (Series -> Seasons -> Episodes), refresh it
/// idempotently, and discover a newly announced episode on a later refresh. Uses
/// FakeMetadataProvider - never live Cinemeta.
/// </summary>
public class MetadataSynchronizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly HttpClient _client;
    private readonly FakeMetadataProvider _fakeMetadataProvider;
    private readonly IServiceProvider _services;

    public MetadataSynchronizationTests(ApiWebApplicationFactory factory)
    {
        _fakeMetadataProvider = new FakeMetadataProvider();

        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IMetadataProvider>(_fakeMetadataProvider)));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    private static Result<SeriesMetadata> BuildStuartMetadataWithImdbId(string imdbId, int episodeCount)
    {
        List<EpisodeMetadata> episodes = [];

        for (int number = 1; number <= episodeCount; number++)
        {
            episodes.Add(new EpisodeMetadata(
                $"{imdbId}:1:{number}",
                $"Episode {number}",
                SeasonNumber: 1,
                EpisodeNumber: number,
                Runtime: TimeSpan.FromMinutes(20),
                ReleaseAtUtc: UtcNow.AddDays(-30).AddDays(number)));
        }

        return new SeriesMetadata(
            new ExternalIds(imdbId, null, null),
            "Stuart Fails to Save the Universe",
            null,
            2026,
            SeriesStatus.Active,
            episodes);
    }

    private async Task<(Guid SeriesId, string ImdbId)> AddSeriesAsync(string imdbId, string title)
    {
        var request = new
        {
            ImdbId = imdbId,
            TmdbId = (string?)null,
            TvdbId = (string?)null,
            Title = title,
            OriginalTitle = (string?)null,
            Year = 2026,
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/series", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(created);

        return (created.Id, imdbId);
    }

    [Fact]
    public async Task AddSeries_WithSuccessfulMetadata_PersistsSeasonAndEpisodes()
    {
        const string imdbId = "tt00000101";
        _fakeMetadataProvider.Handler = _ => BuildStuartMetadataWithImdbId(imdbId, episodeCount: 10);

        (Guid seriesId, _) = await AddSeriesAsync(imdbId, "Placeholder Title");

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{seriesId}/seasons");
        Assert.Equal(HttpStatusCode.OK, seasonsResponse.StatusCode);
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        Assert.NotNull(seasons);
        SeasonSummary season = Assert.Single(seasons);
        Assert.Equal(1, season.Number);

        HttpResponseMessage episodesResponse = await _client.GetAsync($"/api/seasons/{season.Id}/episodes");
        Assert.Equal(HttpStatusCode.OK, episodesResponse.StatusCode);
        var episodes = await episodesResponse.Content.ReadFromJsonAsync<List<EpisodeSummary>>();
        Assert.NotNull(episodes);
        Assert.Equal(10, episodes.Count);

        // The series' own Title/Year came from the (fake) provider, not the
        // placeholder the caller supplied - AddSeries overwrites with authoritative data.
        HttpResponseMessage seriesResponse = await _client.GetAsync($"/api/series/{seriesId}");
        var series = await seriesResponse.Content.ReadFromJsonAsync<SeriesSummary>();
        Assert.NotNull(series);
        Assert.Equal("Stuart Fails to Save the Universe", series.Title);
    }

    [Fact]
    public async Task AddSeries_WhenMetadataProviderFails_StillCreatesSeriesWithoutEpisodes()
    {
        const string imdbId = "tt00000102";
        _fakeMetadataProvider.Handler = externalId =>
            Result.Failure<SeriesMetadata>(MetadataProviderErrors.SeriesNotFound("Fake", externalId));

        (Guid seriesId, _) = await AddSeriesAsync(imdbId, "Caller Supplied Title");

        HttpResponseMessage seriesResponse = await _client.GetAsync($"/api/series/{seriesId}");
        Assert.Equal(HttpStatusCode.OK, seriesResponse.StatusCode);
        var series = await seriesResponse.Content.ReadFromJsonAsync<SeriesSummary>();
        Assert.NotNull(series);
        Assert.Equal("Caller Supplied Title", series.Title); // untouched - provider never responded

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{seriesId}/seasons");
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        Assert.NotNull(seasons);
        Assert.Empty(seasons);
    }

    [Fact]
    public async Task Refresh_CalledRepeatedlyWithIdenticalMetadata_DoesNotDuplicateAnything()
    {
        const string imdbId = "tt00000103";
        _fakeMetadataProvider.Handler = _ => BuildStuartMetadataWithImdbId(imdbId, episodeCount: 10);

        (Guid seriesId, _) = await AddSeriesAsync(imdbId, "Placeholder");

        HttpResponseMessage firstRefresh = await _client.PostAsync($"/api/series/{seriesId}/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);
        var firstResult = await firstRefresh.Content.ReadFromJsonAsync<SynchronizationSummary>();
        Assert.NotNull(firstResult);
        // AddSeries already synchronized once - the first explicit refresh should be a no-op.
        Assert.Equal(0, firstResult.SeasonsAdded);
        Assert.Equal(0, firstResult.EpisodesAdded);
        Assert.Equal(0, firstResult.EpisodesUpdated);

        HttpResponseMessage secondRefresh = await _client.PostAsync($"/api/series/{seriesId}/refresh", content: null);
        var secondResult = await secondRefresh.Content.ReadFromJsonAsync<SynchronizationSummary>();
        Assert.NotNull(secondResult);
        Assert.Equal(0, secondResult.SeasonsAdded);
        Assert.Equal(0, secondResult.EpisodesAdded);

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{seriesId}/seasons");
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        Assert.NotNull(seasons);
        SeasonSummary season = Assert.Single(seasons);

        HttpResponseMessage episodesResponse = await _client.GetAsync($"/api/seasons/{season.Id}/episodes");
        var episodes = await episodesResponse.Content.ReadFromJsonAsync<List<EpisodeSummary>>();
        Assert.NotNull(episodes);
        Assert.Equal(10, episodes.Count);
    }

    [Fact]
    public async Task Refresh_WhenProviderAddsANewEpisode_OnlyTheNewEpisodeIsInserted()
    {
        const string imdbId = "tt00000104";
        _fakeMetadataProvider.Handler = _ => BuildStuartMetadataWithImdbId(imdbId, episodeCount: 10);

        (Guid seriesId, _) = await AddSeriesAsync(imdbId, "Placeholder");

        _fakeMetadataProvider.Handler = _ => BuildStuartMetadataWithImdbId(imdbId, episodeCount: 11);

        HttpResponseMessage refreshResponse = await _client.PostAsync($"/api/series/{seriesId}/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var result = await refreshResponse.Content.ReadFromJsonAsync<SynchronizationSummary>();
        Assert.NotNull(result);
        Assert.Equal(0, result.SeasonsAdded);
        Assert.Equal(1, result.EpisodesAdded);
        Assert.Equal(0, result.EpisodesUpdated);

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{seriesId}/seasons");
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        SeasonSummary season = Assert.Single(seasons!);

        HttpResponseMessage episodesResponse = await _client.GetAsync($"/api/seasons/{season.Id}/episodes");
        var episodes = await episodesResponse.Content.ReadFromJsonAsync<List<EpisodeSummary>>();
        Assert.NotNull(episodes);
        Assert.Equal(11, episodes.Count);
    }

    [Fact]
    public async Task Refresh_DoesNotResetACompletedEpisodeBackToPending()
    {
        const string imdbId = "tt00000105";
        _fakeMetadataProvider.Handler = _ => BuildStuartMetadataWithImdbId(imdbId, episodeCount: 10);

        (Guid seriesId, _) = await AddSeriesAsync(imdbId, "Placeholder");

        Guid episode01Id = await MarkFirstEpisodeCompletedAsync(seriesId);

        HttpResponseMessage refreshResponse = await _client.PostAsync($"/api/series/{seriesId}/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{seriesId}/seasons");
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        SeasonSummary season = Assert.Single(seasons!);

        HttpResponseMessage episodesResponse = await _client.GetAsync($"/api/seasons/{season.Id}/episodes");
        var episodes = await episodesResponse.Content.ReadFromJsonAsync<List<EpisodeSummary>>();
        EpisodeSummary episode01 = episodes!.Single(e => e.Id == episode01Id);

        Assert.Equal("Completed", episode01.Status);
    }

    [Fact]
    public async Task Refresh_DoesNotResetAnUnavailableEpisodeBackToPending()
    {
        const string imdbId = "tt00000106";
        _fakeMetadataProvider.Handler = _ => BuildStuartMetadataWithImdbId(imdbId, episodeCount: 10);

        (Guid seriesId, _) = await AddSeriesAsync(imdbId, "Placeholder");

        Guid episode01Id = await MarkFirstEpisodeUnavailableAsync(seriesId);

        HttpResponseMessage refreshResponse = await _client.PostAsync($"/api/series/{seriesId}/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{seriesId}/seasons");
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        SeasonSummary season = Assert.Single(seasons!);

        HttpResponseMessage episodesResponse = await _client.GetAsync($"/api/seasons/{season.Id}/episodes");
        var episodes = await episodesResponse.Content.ReadFromJsonAsync<List<EpisodeSummary>>();
        EpisodeSummary episode01 = episodes!.Single(e => e.Id == episode01Id);

        Assert.Equal("Unavailable", episode01.Status);
    }

    private async Task<Guid> MarkFirstEpisodeCompletedAsync(Guid seriesId)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Season season = await context.Set<Season>().SingleAsync(s => s.SeriesId == seriesId && s.Number == 1);
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.SeasonId == season.Id && e.EpisodeNumber == 1);

        episode.StartSearching(UtcNow);
        episode.StartValidating(UtcNow);
        episode.MarkCompleted(UtcNow);

        await context.SaveChangesAsync();

        return episode.Id;
    }

    private async Task<Guid> MarkFirstEpisodeUnavailableAsync(Guid seriesId)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Season season = await context.Set<Season>().SingleAsync(s => s.SeriesId == seriesId && s.Number == 1);
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.SeasonId == season.Id && e.EpisodeNumber == 1);

        episode.StartSearching(UtcNow);
        episode.MarkUnavailable(UtcNow, UtcNow.AddHours(6), "no streams returned");

        await context.SaveChangesAsync();

        return episode.Id;
    }

    private sealed record CreatedResponse(Guid Id);

    private sealed record SeriesSummary(Guid Id, string? ImdbId, string Title, int Year, string Status);

    private sealed record SeasonSummary(Guid Id, int Number, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    private sealed record EpisodeSummary(
        Guid Id,
        int EpisodeNumber,
        string Title,
        string Status,
        DateTime ReleaseAtUtc,
        int AttemptCount,
        string? LastError);

    private sealed record SynchronizationSummary(int SeasonsAdded, int EpisodesAdded, int EpisodesUpdated, int EpisodesSkipped);
}
