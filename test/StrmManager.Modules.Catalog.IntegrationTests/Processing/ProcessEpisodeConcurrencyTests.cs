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
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// Proves the optimistic-concurrency claim (Episode.UpdatedAtUtc as a concurrency token,
/// checked by ProcessEpisodeCommandHandler's post-StartSearching save - see ADR-013)
/// actually prevents the same episode from being processed twice when two requests race
/// for it, the way a manual API call and EpisodeProcessingWorker's own tick could.
/// </summary>
public class ProcessEpisodeConcurrencyTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeMetadataProvider _fakeMetadataProvider;
    private readonly FakeStreamProvider _fakeStreamProvider;
    private readonly FakeMediaValidator _fakeMediaValidator;
    private readonly IServiceProvider _services;

    public ProcessEpisodeConcurrencyTests(ApiWebApplicationFactory factory)
    {
        _fakeMetadataProvider = new FakeMetadataProvider();
        _fakeStreamProvider = new FakeStreamProvider();
        _fakeMediaValidator = new FakeMediaValidator();

        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IMetadataProvider>(_fakeMetadataProvider);
                services.AddSingleton<IStreamProvider>(_fakeStreamProvider);
                services.AddSingleton<IMediaValidator>(_fakeMediaValidator);
            }));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    [Fact]
    public async Task ProcessEpisode_TwoConcurrentRequestsForTheSameEpisode_ExactlyOneWins()
    {
        const string imdbId = "tt00000401";
        DateTime releaseAtUtc = DateTime.UtcNow.AddDays(-1);

        _fakeMetadataProvider.Handler = _ => new SeriesMetadata(
            new ExternalIds(imdbId, null, null), "Paradise", null, 2025, SeriesStatus.Active,
            [new EpisodeMetadata($"{imdbId}:1:1", "Episode One", 1, 1, TimeSpan.FromMinutes(52), releaseAtUtc)]);

        var request = new { ImdbId = imdbId, TmdbId = (string?)null, TvdbId = (string?)null, Title = "Placeholder", OriginalTitle = (string?)null, Year = 2025 };
        HttpResponseMessage addResponse = await _client.PostAsJsonAsync("/api/series", request);
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        Guid episodeId;
        using (IServiceScope scope = _services.CreateScope())
        {
            CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Episode episode = await context.Set<Episode>().SingleAsync(e => e.ExternalId == $"{imdbId}:1:1");
            episodeId = episode.Id;
        }

        var candidate = new StreamCandidate("FrostStream", "Source S01E01", null, "https://media.example.test/race");
        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([candidate]);
        _fakeMediaValidator.Handler = (_, reference) => MediaValidationResult.ForApproval(TimeSpan.FromMinutes(52), reference.ExpectedRuntime, 0, "h264", "aac");

        Task<HttpResponseMessage> firstCall = _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);
        Task<HttpResponseMessage> secondCall = _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);

        HttpResponseMessage[] responses = await Task.WhenAll(firstCall, secondCall);

        // Every response is either 200 (the winner, or a request that only saw the
        // episode after it was already Completed and got the idempotent no-op response)
        // or 409 (lost the claim - EpisodeErrors.AlreadyBeingProcessed). Never a 500 -
        // the concurrency conflict is an expected outcome, not an unhandled exception.
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"Unexpected status {r.StatusCode}"));

        using IServiceScope verifyScope = _services.CreateScope();
        CatalogDbContext verifyContext = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Episode finalEpisode = await verifyContext.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        Assert.Equal(MediaStatus.Completed, finalEpisode.Status);

        int strmFileCount = await verifyContext.Set<StrmFile>().CountAsync(f => f.EpisodeId == episodeId);
        Assert.Equal(1, strmFileCount);

        int approvedAttempts = await verifyContext.Set<SourceAttempt>()
            .CountAsync(a => a.EpisodeId == episodeId && a.Result == SourceAttemptResult.Approved);
        Assert.Equal(1, approvedAttempts); // the candidate was validated/approved exactly once, not twice
    }
}
