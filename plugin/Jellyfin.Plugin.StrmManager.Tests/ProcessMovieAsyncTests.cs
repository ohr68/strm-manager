using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.ProcessMovieAsync - no real network, no running plugin/Jellyfin host.
/// Each test drives a fake HttpMessageHandler directly, matching the P4 report's committed scenarios. P1/P2's own
/// tests (StrmManagerClientTests.cs/AddMovieAsyncTests.cs) are untouched and not re-run here. ProcessMovieAsync is
/// currently an unused primitive - these tests exercise the client in isolation only.
/// </summary>
public sealed class ProcessMovieAsyncTests
{
    private static readonly Guid MovieId = Guid.Parse("1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6");

    private static StrmManagerClient.StrmManagerClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new FakeHandler(handle)) { BaseAddress = new Uri("http://strm-manager.test/") };

        // ProcessMovieAsync now resolves its transport through IHttpClientFactory (see the timeout-isolation
        // report) - the factory here hands back a client wired to the SAME fake handler, so every existing
        // scenario below keeps exercising the exact response it was written for.
        var factory = new RecordingHttpClientFactory(_ =>
            new HttpClient(new FakeHandler(handle)) { BaseAddress = new Uri("http://strm-manager.test/") });

        return new StrmManagerClient.StrmManagerClient(httpClient, factory, NullLogger<StrmManagerClient.StrmManagerClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AFreshRunThatCompletes_Returns200WithSelectedSource_AsCompleted()
    {
        const string body = """
            {"movieId":"1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6","status":"Completed","attempts":2,
             "selectedSource":{"provider":"FrostStream","name":"1080p WEB-DL"},
             "strmPath":"/data/strm/Fight Club (1999).strm","reason":null}
            """;
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        ProcessMovieResult result = await client.ProcessMovieAsync(MovieId, CancellationToken.None);

        var completed = Assert.IsType<ProcessMovieResult.Completed>(result);
        Assert.Equal(MovieId, completed.MovieId);
        Assert.Equal(2, completed.Attempts);
        Assert.Equal("FrostStream", completed.Provider);
        Assert.Equal("1080p WEB-DL", completed.SourceName);
        Assert.Equal("/data/strm/Fight Club (1999).strm", completed.StrmPath);
    }

    [Fact]
    public async Task AnAlreadyCompletedShortCircuit_Returns200WithoutSelectedSource_AsCompleted()
    {
        const string body = """
            {"movieId":"1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6","status":"Completed","attempts":1,
             "selectedSource":null,"strmPath":"/data/strm/Fight Club (1999).strm",
             "reason":"Movie is already Completed - not reprocessed."}
            """;
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        ProcessMovieResult result = await client.ProcessMovieAsync(MovieId, CancellationToken.None);

        var completed = Assert.IsType<ProcessMovieResult.Completed>(result);
        Assert.Equal(MovieId, completed.MovieId);
        Assert.Null(completed.Provider);
        Assert.Null(completed.SourceName);
        Assert.Equal("/data/strm/Fight Club (1999).strm", completed.StrmPath);
    }

    [Theory]
    [InlineData("Unavailable", typeof(ProcessMovieResult.Unavailable))]
    [InlineData("Error", typeof(ProcessMovieResult.ProcessingFailed))]
    public async Task ADomainTerminalOutcome_IsMappedToItsOwnCase_NotTheClientErrorCase(string status, Type expectedType)
    {
        string body = $$"""
            {"movieId":"1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6","status":"{{status}}","attempts":3,
             "selectedSource":null,"strmPath":null,"reason":"No candidate passed identity/media validation."}
            """;
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        ProcessMovieResult result = await client.ProcessMovieAsync(MovieId, CancellationToken.None);

        Assert.IsType(expectedType, result);
    }

    [Theory]
    [InlineData("Movies.AlreadyBeingProcessed", typeof(ProcessMovieResult.AlreadyBeingProcessed))]
    [InlineData("Movie.InvalidTransition", typeof(ProcessMovieResult.InvalidTransition))]
    public async Task A409Conflict_IsDisambiguatedByProblemTitle(string problemTitle, Type expectedType)
    {
        string body = $$"""{"type":"about:blank","title":"{{problemTitle}}","status":409,"detail":"x","errorType":"Conflict"}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Conflict, body)));

        ProcessMovieResult result = await client.ProcessMovieAsync(MovieId, CancellationToken.None);

        Assert.IsType(expectedType, result);
    }

    [Fact]
    public async Task AnUnknownMovieId_Returns404AsNotFound()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        ProcessMovieResult result = await client.ProcessMovieAsync(MovieId, CancellationToken.None);

        Assert.IsType<ProcessMovieResult.NotFound>(result);
    }

    [Fact]
    public async Task ACallerCancellation_PropagatesAsOperationCanceledException_NotAsAResult()
    {
        using var cts = new CancellationTokenSource();
        StrmManagerClient.StrmManagerClient client = CreateClient((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await cts.CancelAsync();

        // Same reasoning as P1/P2's equivalent tests: HttpClient wraps a cancellation as TaskCanceledException (a
        // subclass of OperationCanceledException), so this checks the base type, not the exact concrete type.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ProcessMovieAsync(MovieId, cts.Token));
    }

    [Fact]
    public async Task ProcessMovieAsync_RequestsTheDedicatedNamedHttpClient_NotTheOrdinaryInjectedOne()
    {
        var factory = new RecordingHttpClientFactory(_ =>
            new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))))
            {
                BaseAddress = new Uri("http://strm-manager.test/"),
            });
        var ordinaryHttpClient = new HttpClient(new FakeHandler((_, _) =>
            throw new InvalidOperationException("ProcessMovieAsync must not use the ordinary injected HttpClient.")))
        {
            BaseAddress = new Uri("http://strm-manager.test/"),
        };
        var client = new StrmManagerClient.StrmManagerClient(ordinaryHttpClient, factory, NullLogger<StrmManagerClient.StrmManagerClient>.Instance);

        await client.ProcessMovieAsync(MovieId, CancellationToken.None);

        Assert.Equal([StrmManagerClient.StrmManagerClient.ProcessMovieHttpClientName], factory.RequestedNames);
    }

    [Theory]
    [InlineData(true)] // GetByImdbIdAsync
    [InlineData(false)] // AddMovieAsync
    public async Task OrdinaryMetadataCalls_NeverUseTheHttpClientFactory(bool useLookup)
    {
        var factory = new RecordingHttpClientFactory(_ =>
            new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
            {
                BaseAddress = new Uri("http://strm-manager.test/"),
            });
        var httpClient = new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))))
        {
            BaseAddress = new Uri("http://strm-manager.test/"),
        };
        var client = new StrmManagerClient.StrmManagerClient(httpClient, factory, NullLogger<StrmManagerClient.StrmManagerClient>.Instance);

        if (useLookup)
        {
            await client.GetByImdbIdAsync("tt0137523", CancellationToken.None);
        }
        else
        {
            await client.AddMovieAsync("tt0137523", CancellationToken.None);
        }

        Assert.Empty(factory.RequestedNames);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class RecordingHttpClientFactory(Func<string, HttpClient> createClient) : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return createClient(name);
        }
    }
}
