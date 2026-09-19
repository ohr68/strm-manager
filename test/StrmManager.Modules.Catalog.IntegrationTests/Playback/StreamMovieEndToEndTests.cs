using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Playback;

/// <summary>
/// The whole stack behind the endpoint - real coordinator, real resolver, real SQLite database, real selector and identity
/// rules - with only the provider and ffprobe faked (never the real ones). What the contract tests cannot show: a stored
/// movie in each state really produces the documented status, nothing about the request persists anything, and the
/// provider URL that travels the whole path still reaches only the Location header.
/// </summary>
public class StreamMovieEndToEndTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ConcurrentQueue<string> _events = new();
    private RecordingLoggerProvider Logs { get; } = new();

    private Func<MovieStreamReference, Result<IReadOnlyList<StreamCandidate>>> _provider =
        _ => Result.Success<IReadOnlyList<StreamCandidate>>([]);

    private Func<StreamCandidate, MediaValidationResult> _validate = _ => throw new InvalidOperationException("ffprobe was not expected.");

    private (HttpClient Client, IServiceProvider Services) NewHost()
    {
        WebApplicationFactory<Program> isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStreamProvider>(new FakeStreamProvider
                {
                    MovieHandlerAsync = (reference, _) =>
                    {
                        _events.Enqueue("provider");
                        return Task.FromResult(_provider(reference));
                    },
                });

                services.AddSingleton<IMediaValidator>(new FakeMediaValidator
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
                    logging.AddFilter("Microsoft.AspNetCore", LogLevel.Trace); // so a framework redirect could not hide its destination
                    logging.AddProvider(Logs);
                });
            }));

        return (isolated.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }), isolated.Services);
    }

    private static async Task<ObservedResponse> SendAsync(HttpClient client, string method, Guid movieId)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), StablePlaybackHost.RouteFor(movieId));
        using HttpResponseMessage response = await client.SendAsync(request);
        return await ObservedResponse.FromAsync(response);
    }

    private static object Snapshot(Movie m) =>
        (m.Id, m.ExternalIds, m.Title, m.Year, m.Runtime, m.Status, m.LastAttemptAtUtc, m.NextAttemptAtUtc, m.AttemptCount, m.LastError, m.UpdatedAtUtc);

    private void AssertNoUrlAnywhereButTheLocation(ObservedResponse response)
    {
        foreach (string secret in new[] { "media.example.test", SecretMarker })
        {
            Assert.DoesNotContain(secret, response.EverythingButLocation, StringComparison.Ordinal);

            foreach (string line in Logs.Entries)
            {
                Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task ACompletedMovie_RedirectsToTheFreshlyResolvedSource_AndPersistsNothing(string method)
    {
        (HttpClient client, IServiceProvider services) = NewHost();
        Guid movieId = await SeedMovieAsync(services, MediaStatus.Completed);
        object before = Snapshot(await LoadMovieAsync(services, movieId));
        _provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A")]);
        _validate = _ => Approval;

        ObservedResponse response = await SendAsync(client, method, movieId);

        Assert.Equal(307, response.Status);
        Assert.Equal(UrlFor("Source A"), response.Location);
        Assert.Equal("no-store", response.Header("Cache-Control"));
        Assert.Empty(response.Body);
        Assert.Equal(["provider", "ffprobe"], _events.ToArray()); // resolved just in time, once

        AssertNoUrlAnywhereButTheLocation(response);
        Assert.Equal(before, Snapshot(await LoadMovieAsync(services, movieId))); // the movie is exactly as it was
    }

    [Fact]
    public async Task ATwoRequestSequence_ResolvesTwice_ThereIsNoCache()
    {
        (HttpClient client, IServiceProvider services) = NewHost();
        Guid movieId = await SeedMovieAsync(services, MediaStatus.Completed);
        _provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A")]);
        _validate = _ => Approval;

        await SendAsync(client, "GET", movieId);
        await SendAsync(client, "GET", movieId);

        Assert.Equal(["provider", "ffprobe", "provider", "ffprobe"], _events.ToArray());
    }

    [Fact]
    public async Task AnUnknownMovie_AndAMovieThatIsNotCompleted_AreIndistinguishable()
    {
        (HttpClient client, IServiceProvider services) = NewHost();
        Guid pending = await SeedMovieAsync(services, MediaStatus.Pending);
        Guid unavailable = await SeedMovieAsync(services, MediaStatus.Unavailable);
        Guid error = await SeedMovieAsync(services, MediaStatus.Error);
        _provider = _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A")]);

        ObservedResponse unknown = await SendAsync(client, "GET", Guid.NewGuid());
        var others = new List<ObservedResponse>();

        foreach (Guid id in new[] { pending, unavailable, error })
        {
            others.Add(await SendAsync(client, "GET", id));
        }

        Assert.Equal(404, unknown.Status);
        Assert.Empty(_events); // the provider was never asked about any of them

        foreach (ObservedResponse other in others)
        {
            Assert.Equal(unknown.Status, other.Status);
            Assert.Equal(unknown.Body, other.Body);
            Assert.Equal(unknown.Location, other.Location);
            Assert.Equal(unknown.OtherHeaders.OrderBy(h => h.Key).Where(h => h.Key != "Date"), other.OtherHeaders.OrderBy(h => h.Key).Where(h => h.Key != "Date"));
        }
    }

    [Theory]
    [InlineData("no-candidates")]
    [InlineData("none-approved")]
    [InlineData("provider-failure")]
    public async Task ACompletedMovieWithNoUsableSource_Is503_WithNothingAboutWhy_AndStaysCompleted(string reason)
    {
        (HttpClient client, IServiceProvider services) = NewHost();
        Guid movieId = await SeedMovieAsync(services, MediaStatus.Completed);
        object before = Snapshot(await LoadMovieAsync(services, movieId));
        _provider = reason switch
        {
            "provider-failure" => _ => Result.Failure<IReadOnlyList<StreamCandidate>>(StreamProviderErrors.Timeout("FrostStream")),
            "no-candidates" => _ => Result.Success<IReadOnlyList<StreamCandidate>>([]),
            _ => _ => Result.Success<IReadOnlyList<StreamCandidate>>([Candidate("Source A")]),
        };
        _validate = _ => Rejection($"ffprobe said no about {UrlFor("Source A")}");

        ObservedResponse response = await SendAsync(client, "GET", movieId);

        Assert.Equal(503, response.Status);
        Assert.Empty(response.Body);
        Assert.Null(response.Location);
        Assert.Equal("no-store", response.Header("Cache-Control"));
        AssertNoUrlAnywhereButTheLocation(response);
        Assert.Equal(before, Snapshot(await LoadMovieAsync(services, movieId))); // a playback failure changes nothing (Phase 6.3 is not this)
    }
}
