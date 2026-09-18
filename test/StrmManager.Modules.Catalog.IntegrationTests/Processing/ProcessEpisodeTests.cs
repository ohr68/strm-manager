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
/// End-to-end coverage of POST /api/episodes/{id}/process through the real API, using
/// FakeStreamProvider/FakeMediaValidator (never a real FrostStream call or ffprobe
/// process - see ApiWebApplicationFactory) and each factory's own temp .strm root.
/// FakeMetadataProvider is reused only to get real Series/Season/Episode rows to
/// process against, the same way MetadataSynchronizationTests does.
/// </summary>
public class ProcessEpisodeTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly HttpClient _client;
    private readonly FakeMetadataProvider _fakeMetadataProvider;
    private readonly FakeStreamProvider _fakeStreamProvider;
    private readonly FakeMediaValidator _fakeMediaValidator;
    private readonly IServiceProvider _services;

    public ProcessEpisodeTests(ApiWebApplicationFactory factory)
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

    private static Result<SeriesMetadata> BuildSingleEpisodeMetadata(string imdbId, DateTime releaseAtUtc, TimeSpan? runtime) =>
        new SeriesMetadata(
            new ExternalIds(imdbId, null, null),
            "Paradise",
            null,
            2025,
            SeriesStatus.Active,
            [new EpisodeMetadata($"{imdbId}:1:1", "Episode One", SeasonNumber: 1, EpisodeNumber: 1, Runtime: runtime, ReleaseAtUtc: releaseAtUtc)]);

    private async Task<Guid> AddSeriesWithSingleEpisodeAsync(string imdbId, DateTime releaseAtUtc, TimeSpan? runtime = null)
    {
        runtime ??= TimeSpan.FromMinutes(52);
        _fakeMetadataProvider.Handler = _ => BuildSingleEpisodeMetadata(imdbId, releaseAtUtc, runtime);

        var request = new
        {
            ImdbId = imdbId,
            TmdbId = (string?)null,
            TvdbId = (string?)null,
            Title = "Placeholder",
            OriginalTitle = (string?)null,
            Year = 2025,
        };

        HttpResponseMessage response = await _client.PostAsJsonAsync("/api/series", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(created);

        HttpResponseMessage seasonsResponse = await _client.GetAsync($"/api/series/{created.Id}/seasons");
        var seasons = await seasonsResponse.Content.ReadFromJsonAsync<List<SeasonSummary>>();
        SeasonSummary season = Assert.Single(seasons!);

        HttpResponseMessage episodesResponse = await _client.GetAsync($"/api/seasons/{season.Id}/episodes");
        var episodes = await episodesResponse.Content.ReadFromJsonAsync<List<EpisodeSummary>>();
        EpisodeSummary episode = Assert.Single(episodes!);

        return episode.Id;
    }

    [Fact]
    public async Task ProcessEpisode_FirstCandidateRejectedSecondApproved_CompletesAndWritesStrmFile()
    {
        const string imdbId = "tt00000201";
        Guid episodeId = await AddSeriesWithSingleEpisodeAsync(imdbId, UtcNow.AddDays(-1), TimeSpan.FromMinutes(52));

        var rejectedCandidate = new StreamCandidate("FrostStream", "Bad Source S01E01", null, "https://media.example.test/bad");
        var approvedCandidate = new StreamCandidate("FrostStream", "Good Source S01E01", null, "https://media.example.test/good");

        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([rejectedCandidate, approvedCandidate]);
        _fakeMediaValidator.Handler = (candidate, reference) => candidate.Url == approvedCandidate.Url
            ? MediaValidationResult.ForApproval(TimeSpan.FromMinutes(52), reference.ExpectedRuntime, 0, "h264", "aac")
            : MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "Deliberately rejected in test.");

        HttpResponseMessage response = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessEpisodeResponse>();
        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, result.Attempts);
        Assert.NotNull(result.SelectedSource);
        Assert.Equal("Good Source S01E01", result.SelectedSource!.Name);
        Assert.NotNull(result.StrmPath);
        Assert.True(File.Exists(result.StrmPath));
        Assert.Equal("https://media.example.test/good", await File.ReadAllTextAsync(result.StrmPath!));

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        List<SourceAttempt> attempts = await context.Set<SourceAttempt>()
            .Where(a => a.EpisodeId == episodeId)
            .OrderBy(a => a.AttemptedAtUtc)
            .ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(SourceAttemptResult.Rejected, attempts[0].Result);
        Assert.Equal(SourceAttemptResult.Approved, attempts[1].Result);

        StrmFile? strmFile = await context.Set<StrmFile>().SingleOrDefaultAsync(f => f.EpisodeId == episodeId);
        Assert.NotNull(strmFile);
        Assert.Equal(result.StrmPath, strmFile.Path);

        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        Assert.Equal(MediaStatus.Completed, episode.Status);
    }

    [Fact]
    public async Task ProcessEpisode_NoCandidatesReturned_MarksUnavailable()
    {
        const string imdbId = "tt00000202";
        Guid episodeId = await AddSeriesWithSingleEpisodeAsync(imdbId, UtcNow.AddDays(-1));

        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([]);

        HttpResponseMessage response = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessEpisodeResponse>();
        Assert.NotNull(result);
        Assert.Equal("Unavailable", result.Status);
        Assert.Null(result.SelectedSource);
        Assert.Null(result.StrmPath);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        Assert.Equal(MediaStatus.Unavailable, episode.Status);
        Assert.NotNull(episode.NextAttemptAtUtc);
    }

    [Fact]
    public async Task ProcessEpisode_AllCandidatesRejected_MarksUnavailable()
    {
        const string imdbId = "tt00000203";
        Guid episodeId = await AddSeriesWithSingleEpisodeAsync(imdbId, UtcNow.AddDays(-1));

        var candidate = new StreamCandidate("FrostStream", "Source S01E01", null, "https://media.example.test/only");
        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([candidate]);
        _fakeMediaValidator.Handler = (_, _) => MediaValidationResult.ForRejection(SourceAttemptResult.InvalidMedia, "No audio stream found.");

        HttpResponseMessage response = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessEpisodeResponse>();
        Assert.NotNull(result);
        Assert.Equal("Unavailable", result.Status);
        Assert.Equal(1, result.Attempts);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        List<SourceAttempt> attempts = await context.Set<SourceAttempt>().Where(a => a.EpisodeId == episodeId).ToListAsync();
        SourceAttempt attempt = Assert.Single(attempts);
        Assert.Equal(SourceAttemptResult.InvalidMedia, attempt.Result);
    }

    [Fact]
    public async Task ProcessEpisode_StreamProviderFailsTechnically_MarksError()
    {
        const string imdbId = "tt00000204";
        Guid episodeId = await AddSeriesWithSingleEpisodeAsync(imdbId, UtcNow.AddDays(-1));

        _fakeStreamProvider.Handler = _ => Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.ProviderUnavailable("FrostStream"));

        HttpResponseMessage response = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessEpisodeResponse>();
        Assert.NotNull(result);
        Assert.Equal("Error", result.Status);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        Assert.Equal(MediaStatus.Error, episode.Status);
        Assert.NotNull(episode.LastError);
    }

    [Fact]
    public async Task ProcessEpisode_ScheduledEpisode_ReturnsConflictAndDoesNotProcess()
    {
        const string imdbId = "tt00000205";
        Guid episodeId = await AddSeriesWithSingleEpisodeAsync(imdbId, UtcNow.AddDays(30)); // future release date -> stays Scheduled

        HttpResponseMessage response = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Episode episode = await context.Set<Episode>().SingleAsync(e => e.Id == episodeId);
        Assert.Equal(MediaStatus.Scheduled, episode.Status);
    }

    [Fact]
    public async Task ProcessEpisode_AlreadyCompletedEpisode_IsIdempotentAndNotReprocessed()
    {
        const string imdbId = "tt00000206";
        Guid episodeId = await AddSeriesWithSingleEpisodeAsync(imdbId, UtcNow.AddDays(-1), TimeSpan.FromMinutes(52));

        var candidate = new StreamCandidate("FrostStream", "Source S01E01", null, "https://media.example.test/first");
        _fakeStreamProvider.Handler = _ => Result.Success<IReadOnlyList<StreamCandidate>>([candidate]);
        _fakeMediaValidator.Handler = (_, reference) => MediaValidationResult.ForApproval(TimeSpan.FromMinutes(52), reference.ExpectedRuntime, 0, "h264", "aac");

        HttpResponseMessage firstResponse = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<ProcessEpisodeResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal("Completed", firstResult.Status);

        // Prove the second call short-circuits before ever consulting the stream
        // provider again - if the pipeline reran, this throw would surface as a 500.
        _fakeStreamProvider.Handler = _ => throw new InvalidOperationException(
            "Stream provider should not be called for an already-Completed episode.");

        HttpResponseMessage secondResponse = await _client.PostAsync($"/api/episodes/{episodeId}/process", content: null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<ProcessEpisodeResponse>();
        Assert.NotNull(secondResult);
        Assert.Equal("Completed", secondResult.Status);
        Assert.Equal(firstResult.StrmPath, secondResult.StrmPath);

        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        List<SourceAttempt> attempts = await context.Set<SourceAttempt>().Where(a => a.EpisodeId == episodeId).ToListAsync();
        Assert.Single(attempts); // only the first run recorded an attempt
    }

    private sealed record CreatedResponse(Guid Id);

    private sealed record SeasonSummary(Guid Id, int Number, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

    private sealed record EpisodeSummary(
        Guid Id, int EpisodeNumber, string Title, string Status, DateTime ReleaseAtUtc, int AttemptCount, string? LastError);

    private sealed record SelectedSourceResponse(string Provider, string Name);

    private sealed record ProcessEpisodeResponse(
        Guid EpisodeId, string Status, int Attempts, SelectedSourceResponse? SelectedSource, string? StrmPath, string? Reason);
}
