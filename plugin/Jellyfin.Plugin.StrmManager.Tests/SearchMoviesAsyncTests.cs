using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.SearchMoviesAsync - no real network, no running plugin/Jellyfin host.
/// Mirrors GetMovieRowsAsyncTests/GetPopularMoviesAsyncTests exactly (same fixture-casing convention, same
/// skip-not-fail rule) since this call follows the identical established client pattern, plus a BadRequest -&gt;
/// Invalid case unique to search.
/// </summary>
public sealed class SearchMoviesAsyncTests
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
    public async Task AValidResponse_RequestsTheEncodedSearchUrl_AndReturnsFound_WithTheMappedMovies()
    {
        const string body = """{"movies":[{"externalId":"tt0468569","title":"The Dark Knight","year":2008,"posterUrl":"https://example.test/1.jpg"}]}""";
        HttpRequestMessage? captured = null;
        StrmManagerClient.StrmManagerClient client = CreateClient((request, _) =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
        });

        SearchMoviesResult result = await client.SearchMoviesAsync("The Dark Knight", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("http://strm-manager.test/api/catalog/movies/search?query=The%20Dark%20Knight", captured.RequestUri!.AbsoluteUri);

        var found = Assert.IsType<SearchMoviesResult.Found>(result);
        Assert.Equal(new CatalogMovie("tt0468569", "The Dark Knight", 2008, "https://example.test/1.jpg"), Assert.Single(found.Movies));
    }

    [Fact]
    public async Task AnEmptyMoviesArray_ReturnsFound_NotAFailure()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"movies":[]}""")));

        SearchMoviesResult result = await client.SearchMoviesAsync("zzzzstrmmanagerthismoviedoesnotexist99999", CancellationToken.None);

        Assert.Empty(Assert.IsType<SearchMoviesResult.Found>(result).Movies);
    }

    [Fact]
    public async Task AMovieMissingExternalIdOrTitle_IsSkippedNotFailed()
    {
        const string body = """{"movies":[{"title":"No Id"},{"externalId":"tt1"},{"externalId":"tt2","title":"Has Both"}]}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        SearchMoviesResult result = await client.SearchMoviesAsync("query", CancellationToken.None);

        var found = Assert.IsType<SearchMoviesResult.Found>(result);
        var movie = Assert.Single(found.Movies);
        Assert.Equal("tt2", movie.ExternalId);
    }

    [Fact]
    public async Task ABadRequestStatus_ReturnsInvalid()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        SearchMoviesResult result = await client.SearchMoviesAsync(string.Empty, CancellationToken.None);

        Assert.IsType<SearchMoviesResult.Invalid>(result);
    }

    [Fact]
    public async Task AServerError_ReturnsError()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        SearchMoviesResult result = await client.SearchMoviesAsync("The Dark Knight", CancellationToken.None);

        Assert.IsType<SearchMoviesResult.Error>(result);
    }

    [Fact]
    public async Task ANetworkFailure_ReturnsUnreachable()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        SearchMoviesResult result = await client.SearchMoviesAsync("The Dark Knight", CancellationToken.None);

        Assert.IsType<SearchMoviesResult.Unreachable>(result);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchMoviesAsync("The Dark Knight", cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("SearchMoviesAsync must not use IHttpClientFactory.");
    }
}
