using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.GetByImdbIdAsync - no real network, no running plugin/Jellyfin host. Each
/// test drives a fake HttpMessageHandler directly, matching the six scenarios the P1 report committed to.
/// </summary>
public sealed class StrmManagerClientTests
{
    private const string ImdbId = "tt0137523";

    private static StrmManagerClient.StrmManagerClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new FakeHandler(handle)) { BaseAddress = new Uri("http://strm-manager.test/") };
        return new StrmManagerClient.StrmManagerClient(httpClient, new NeverCalledHttpClientFactory(), NullLogger<StrmManagerClient.StrmManagerClient>.Instance);
    }

    /// <summary>
    /// GetByImdbIdAsync never uses IHttpClientFactory (only ProcessMovieAsync does - see ProcessMovieAsyncTests.cs)
    /// - a throwing fake makes any accidental future use of it here fail loudly instead of silently.
    /// </summary>
    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("GetByImdbIdAsync must not use IHttpClientFactory.");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AValidMovie_ReturnsFound_WithTheEssentialFields()
    {
        const string body = """
            {"id":"1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6","imdbId":"tt0137523","tmdbId":"550","tvdbId":null,
             "title":"Fight Club","year":1999,"runtime":"02:19:00","releaseAtUtc":"1999-10-15T00:00:00Z",
             "status":"Completed","createdAtUtc":"2026-09-19T10:00:00Z","updatedAtUtc":"2026-09-19T10:00:00Z"}
            """;
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        MovieLookupResult result = await client.GetByImdbIdAsync(ImdbId, CancellationToken.None);

        var found = Assert.IsType<MovieLookupResult.Found>(result);
        Assert.Equal(Guid.Parse("1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6"), found.MovieId);
        Assert.Equal("tt0137523", found.ImdbId);
        Assert.Equal("Fight Club", found.Title);
        Assert.Equal(1999, found.Year);
        Assert.Equal("Completed", found.Status);
    }

    [Fact]
    public async Task AnUnknownImdbId_ReturnsNotFound()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        MovieLookupResult result = await client.GetByImdbIdAsync(ImdbId, CancellationToken.None);

        Assert.IsType<MovieLookupResult.NotFound>(result);
    }

    [Fact]
    public async Task AMalformedImdbId_ReturnsInvalid()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        MovieLookupResult result = await client.GetByImdbIdAsync("not-an-imdb-id", CancellationToken.None);

        Assert.IsType<MovieLookupResult.Invalid>(result);
    }

    [Fact]
    public async Task ANetworkFailure_ReturnsUnreachable()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        MovieLookupResult result = await client.GetByImdbIdAsync(ImdbId, CancellationToken.None);

        Assert.IsType<MovieLookupResult.Unreachable>(result);
    }

    [Fact]
    public async Task AMalformed200Response_ReturnsError()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{ this is not valid json")));

        MovieLookupResult result = await client.GetByImdbIdAsync(ImdbId, CancellationToken.None);

        Assert.IsType<MovieLookupResult.Error>(result);
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

        // HttpClient itself wraps a cancellation as TaskCanceledException (a subclass of OperationCanceledException),
        // so this checks the base type a caller actually cares about, not the exact concrete type.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetByImdbIdAsync(ImdbId, cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }
}
