using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.GetMovieRowsAsync - no real network, no running plugin/Jellyfin host.
/// Mirrors GetPopularMoviesAsyncTests exactly (same fixture-casing convention, same skip-not-fail rule) since this
/// call follows the identical established client pattern.
/// </summary>
public sealed class GetMovieRowsAsyncTests
{
    private static StrmManagerClient.StrmManagerClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new FakeHandler(handle)) { BaseAddress = new Uri("http://strm-manager.test/") };
        return new StrmManagerClient.StrmManagerClient(httpClient, new NeverCalledHttpClientFactory(), NullLogger<StrmManagerClient.StrmManagerClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AValidResponse_ReturnsFound_WithTheMappedRowsAndMovies()
    {
        const string body = """
            {"rows":[
                {"id":"popular","name":"Popular","movies":[{"externalId":"tt1","title":"A Movie","year":2026,"posterUrl":"https://example.test/1.jpg"}]},
                {"id":"action","name":"Action","movies":[{"externalId":"tt2","title":"Another Movie","year":2025,"posterUrl":"https://example.test/2.jpg"}]}
            ]}
            """;
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        MovieRowsResult result = await client.GetMovieRowsAsync(CancellationToken.None);

        var found = Assert.IsType<MovieRowsResult.Found>(result);
        Assert.Equal(2, found.Rows.Count);
        Assert.Equal("popular", found.Rows[0].Id);
        Assert.Equal("Popular", found.Rows[0].Name);
        Assert.Equal(new CatalogMovie("tt1", "A Movie", 2026, "https://example.test/1.jpg"), found.Rows[0].Movies[0]);
        Assert.Equal("action", found.Rows[1].Id);
        Assert.Equal(new CatalogMovie("tt2", "Another Movie", 2025, "https://example.test/2.jpg"), found.Rows[1].Movies[0]);
    }

    [Fact]
    public async Task AnEmptyRowsArray_ReturnsFound_NotAFailure()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"rows":[]}""")));

        MovieRowsResult result = await client.GetMovieRowsAsync(CancellationToken.None);

        Assert.Empty(Assert.IsType<MovieRowsResult.Found>(result).Rows);
    }

    [Fact]
    public async Task ARowMissingIdOrName_IsSkippedNotFailed()
    {
        const string body = """{"rows":[{"name":"No Id","movies":[]},{"id":"r1","movies":[]},{"id":"r2","name":"Has Both","movies":[]}]}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        MovieRowsResult result = await client.GetMovieRowsAsync(CancellationToken.None);

        var found = Assert.IsType<MovieRowsResult.Found>(result);
        var row = Assert.Single(found.Rows);
        Assert.Equal("r2", row.Id);
    }

    [Fact]
    public async Task AMovieMissingExternalIdOrTitle_IsSkippedWithinItsRow()
    {
        const string body = """{"rows":[{"id":"popular","name":"Popular","movies":[{"title":"No Id"},{"externalId":"tt1"},{"externalId":"tt2","title":"Has Both"}]}]}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        MovieRowsResult result = await client.GetMovieRowsAsync(CancellationToken.None);

        var found = Assert.IsType<MovieRowsResult.Found>(result);
        var movie = Assert.Single(found.Rows[0].Movies);
        Assert.Equal("tt2", movie.ExternalId);
    }

    [Fact]
    public async Task AServerError_ReturnsError()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        MovieRowsResult result = await client.GetMovieRowsAsync(CancellationToken.None);

        Assert.IsType<MovieRowsResult.Error>(result);
    }

    [Fact]
    public async Task ANetworkFailure_ReturnsUnreachable()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        MovieRowsResult result = await client.GetMovieRowsAsync(CancellationToken.None);

        Assert.IsType<MovieRowsResult.Unreachable>(result);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMovieRowsAsync(cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("GetMovieRowsAsync must not use IHttpClientFactory.");
    }
}
