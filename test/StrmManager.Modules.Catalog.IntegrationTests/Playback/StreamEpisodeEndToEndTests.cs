using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;

namespace StrmManager.Modules.Catalog.IntegrationTests.Playback;

/// <summary>
/// Whole-stack coverage behind the stable Episode playback endpoint:
/// real coordinator, resolver, SQLite repository, EpisodeSourceSelector and
/// episode identity rules. Only the external stream provider and ffprobe are
/// replaced with deterministic test doubles.
/// </summary>
public class StreamEpisodeEndToEndTests(
    ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow =
        new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ConcurrentQueue<string> _events = new();
    private readonly ConcurrentQueue<EpisodeStreamReference> _references = new();

    private RecordingLoggerProvider Logs { get; } = new();

    private Func<EpisodeStreamReference, Result<IReadOnlyList<StreamCandidate>>> _provider =
        _ => Result.Success<IReadOnlyList<StreamCandidate>>([]);

    private Func<StreamCandidate, MediaValidationResult> _validate =
        _ => throw new InvalidOperationException(
            "ffprobe was not expected.");

    private (
        HttpClient Client,
        IServiceProvider Services,
        FakeMetadataProvider Metadata) NewHost()
    {
        var metadata = new FakeMetadataProvider();

        WebApplicationFactory<Program> isolated =
            factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IMetadataProvider>(
                        metadata);

                    services.AddSingleton<IStreamProvider>(
                        new FakeStreamProvider
                        {
                            Handler = reference =>
                            {
                                _events.Enqueue("provider");
                                _references.Enqueue(reference);

                                return _provider(reference);
                            },
                        });

                    services.AddSingleton<IMediaValidator>(
                        new FakeMediaValidator
                        {
                            Handler = (candidate, _) =>
                            {
                                _events.Enqueue("ffprobe");
                                return _validate(candidate);
                            },
                        });

                    services.AddLogging(logging =>
                    {
                        logging.SetMinimumLevel(LogLevel.Trace);
                        logging.AddFilter(
                            "Microsoft.AspNetCore",
                            LogLevel.Trace);
                        logging.AddProvider(Logs);
                    });
                }));

        return (
            isolated.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                }),
            isolated.Services,
            metadata);
    }

    private static Result<SeriesMetadata> BuildMetadata(
        string imdbId,
        DateTime releaseAtUtc,
        TimeSpan runtime) =>
        new SeriesMetadata(
            new ExternalIds(imdbId, null, null),
            "Paradise",
            null,
            2025,
            SeriesStatus.Active,
            [
                new EpisodeMetadata(
                    $"{imdbId}:2:3",
                    "Episode Three",
                    SeasonNumber: 2,
                    EpisodeNumber: 3,
                    Runtime: runtime,
                    ReleaseAtUtc: releaseAtUtc),
            ]);

    private static async Task<Guid> AddEpisodeAsync(
        HttpClient client,
        FakeMetadataProvider metadata,
        string imdbId)
    {
        metadata.Handler =
            _ => BuildMetadata(
                imdbId,
                UtcNow.AddDays(-1),
                TimeSpan.FromMinutes(52));

        var request = new
        {
            ImdbId = imdbId,
            TmdbId = (string?)null,
            TvdbId = (string?)null,
            Title = "Placeholder",
            OriginalTitle = (string?)null,
            Year = 2025,
        };

        HttpResponseMessage createResponse =
            await client.PostAsJsonAsync(
                "/api/series",
                request);

        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);

        CreatedResponse? created =
            await createResponse.Content
                .ReadFromJsonAsync<CreatedResponse>();

        Assert.NotNull(created);

        List<SeasonSummary>? seasons =
            await client.GetFromJsonAsync<List<SeasonSummary>>(
                $"/api/series/{created.Id}/seasons");

        SeasonSummary season = Assert.Single(seasons!);

        Assert.Equal(2, season.Number);

        List<EpisodeSummary>? episodes =
            await client.GetFromJsonAsync<List<EpisodeSummary>>(
                $"/api/seasons/{season.Id}/episodes");

        EpisodeSummary episode = Assert.Single(episodes!);

        Assert.Equal(3, episode.EpisodeNumber);

        return episode.Id;
    }

    private static async Task<Episode> LoadEpisodeAsync(
        IServiceProvider services,
        Guid episodeId)
    {
        using IServiceScope scope = services.CreateScope();

        CatalogDbContext context =
            scope.ServiceProvider
                .GetRequiredService<CatalogDbContext>();

        return await context
            .Set<Episode>()
            .AsNoTracking()
            .SingleAsync(e => e.Id == episodeId);
    }

    private static object Snapshot(Episode episode) =>
        (
            episode.Id,
            episode.SeasonId,
            episode.ExternalId,
            episode.Title,
            episode.SeasonNumber,
            episode.EpisodeNumber,
            episode.Runtime,
            episode.ReleaseAtUtc,
            episode.Status,
            episode.LastAttemptAtUtc,
            episode.NextAttemptAtUtc,
            episode.AttemptCount,
            episode.LastError,
            episode.UpdatedAtUtc
        );

    private static async Task<ObservedResponse> SendAsync(
        HttpClient client,
        string method,
        Guid episodeId)
    {
        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                StablePlaybackHost.EpisodeRouteFor(episodeId));

        using HttpResponseMessage response =
            await client.SendAsync(request);

        return await ObservedResponse.FromAsync(response);
    }

    private void AssertNoUrlAnywhereButLocation(
        ObservedResponse response)
    {
        foreach (string secret in
                 new[] { "media.example.test", "episode-secret-marker" })
        {
            Assert.DoesNotContain(
                secret,
                response.EverythingButLocation,
                StringComparison.Ordinal);

            foreach (string line in Logs.Entries)
            {
                Assert.DoesNotContain(
                    secret,
                    line,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ACompletedEpisode_ResolvesJitWithCorrectIdentity_AndPersistsNothing()
    {
        (HttpClient client, IServiceProvider services, FakeMetadataProvider metadata) =
            NewHost();

        Guid episodeId =
            await AddEpisodeAsync(
                client,
                metadata,
                "tt00000301");

        _provider =
            _ => Result.Success<IReadOnlyList<StreamCandidate>>(
            [
                new StreamCandidate(
                    "FrostStream",
                    "Initial Source S02E03",
                    null,
                    "https://media.example.test/initial"),
            ]);

        _validate =
            _ => MediaValidationResult.ForApproval(
                TimeSpan.FromMinutes(52),
                TimeSpan.FromMinutes(52),
                0,
                "h264",
                "aac");

        HttpResponseMessage process =
            await client.PostAsync(
                $"/api/episodes/{episodeId}/process",
                content: null);

        Assert.Equal(HttpStatusCode.OK, process.StatusCode);

        Episode completed =
            await LoadEpisodeAsync(services, episodeId);

        Assert.Equal(MediaStatus.Completed, completed.Status);

        object before = Snapshot(completed);

        _events.Clear();
        _references.Clear();

        const string freshUrl =
            "https://media.example.test/episode-secret-marker";

        _provider =
            _ => Result.Success<IReadOnlyList<StreamCandidate>>(
            [
                new StreamCandidate(
                    "FrostStream",
                    "Fresh Source S02E03",
                    null,
                    freshUrl),
            ]);

        _validate =
            _ => MediaValidationResult.ForApproval(
                TimeSpan.FromMinutes(52),
                TimeSpan.FromMinutes(52),
                0,
                "h264",
                "aac");

        ObservedResponse response =
            await SendAsync(
                client,
                "GET",
                episodeId);

        Assert.Equal(307, response.Status);
        Assert.Equal(freshUrl, response.Location);
        Assert.Equal(
            "no-store",
            response.Header("Cache-Control"));
        Assert.Empty(response.Body);

        Assert.Equal(
            ["provider", "ffprobe"],
            _events.ToArray());

        EpisodeStreamReference reference =
            Assert.Single(_references);

        Assert.Equal(2, reference.SeasonNumber);
        Assert.Equal(3, reference.EpisodeNumber);
        Assert.Equal(
            TimeSpan.FromMinutes(52),
            reference.ExpectedRuntime);

        AssertNoUrlAnywhereButLocation(response);

        Assert.Equal(
            before,
            Snapshot(
                await LoadEpisodeAsync(
                    services,
                    episodeId)));
    }

    [Fact]
    public async Task TwoPlaybackRequests_ResolveTwice_ThereIsNoCache()
    {
        (HttpClient client, _, FakeMetadataProvider metadata) =
            NewHost();

        Guid episodeId =
            await AddEpisodeAsync(
                client,
                metadata,
                "tt00000302");

        _provider =
            _ => Result.Success<IReadOnlyList<StreamCandidate>>(
            [
                new StreamCandidate(
                    "FrostStream",
                    "Initial Source S02E03",
                    null,
                    "https://media.example.test/initial"),
            ]);

        _validate =
            _ => MediaValidationResult.ForApproval(
                TimeSpan.FromMinutes(52),
                TimeSpan.FromMinutes(52),
                0,
                "h264",
                "aac");

        HttpResponseMessage process =
            await client.PostAsync(
                $"/api/episodes/{episodeId}/process",
                content: null);

        Assert.Equal(HttpStatusCode.OK, process.StatusCode);

        _events.Clear();
        _references.Clear();

        int request = 0;

        _provider = _ =>
        {
            request++;

            return Result.Success<IReadOnlyList<StreamCandidate>>(
            [
                new StreamCandidate(
                    "FrostStream",
                    $"Fresh Source {request} S02E03",
                    null,
                    $"https://media.example.test/fresh-{request}"),
            ]);
        };

        await SendAsync(client, "GET", episodeId);
        await SendAsync(client, "GET", episodeId);

        Assert.Equal(
            ["provider", "ffprobe", "provider", "ffprobe"],
            _events.ToArray());

        Assert.Equal(2, _references.Count);
        Assert.Equal(2, request);
    }

    private sealed record CreatedResponse(Guid Id);

    private sealed record SeasonSummary(
        Guid Id,
        int Number,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record EpisodeSummary(
        Guid Id,
        int EpisodeNumber,
        string Title,
        string Status,
        DateTime ReleaseAtUtc,
        int AttemptCount,
        string? LastError);
}
