using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.GetPopularMoviesAsync - no real network, no running plugin/Jellyfin
/// host. Matches the UI-2 report's committed scenarios; the backend's own field casing (camelCase, verified via
/// the existing MovieResponseDto/CreatedResponseDto convention) is what these fixtures use.
/// </summary>
public sealed class GetPopularMoviesAsyncTests
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
    public async Task AValidResponse_ReturnsFound_WithTheMappedMovies()
    {
        const string body = """
            {"movies":[
                {"externalId":"tt27165187","title":"The End of Oak Street","year":2026,"posterUrl":"https://images.metahub.space/poster/small/tt27165187/img"},
                {"externalId":"tt28014327","title":"Mayday","year":2026,"posterUrl":"https://images.metahub.space/poster/small/tt28014327/img"}
            ]}
            """;
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        CatalogMoviesResult result = await client.GetPopularMoviesAsync(CancellationToken.None);

        var found = Assert.IsType<CatalogMoviesResult.Found>(result);
        Assert.Equal(2, found.Movies.Count);
        Assert.Equal(new CatalogMovie("tt27165187", "The End of Oak Street", 2026, "https://images.metahub.space/poster/small/tt27165187/img"), found.Movies[0]);
    }

    [Fact]
    public async Task AnEmptyCatalog_ReturnsFound_NotAFailure()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"movies":[]}""")));

        CatalogMoviesResult result = await client.GetPopularMoviesAsync(CancellationToken.None);

        Assert.Empty(Assert.IsType<CatalogMoviesResult.Found>(result).Movies);
    }

    [Fact]
    public async Task AnItemMissingExternalIdOrTitle_IsSkippedNotFailed()
    {
        const string body = """{"movies":[{"title":"No Id"},{"externalId":"tt1"},{"externalId":"tt2","title":"Has Both"}]}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        CatalogMoviesResult result = await client.GetPopularMoviesAsync(CancellationToken.None);

        var found = Assert.IsType<CatalogMoviesResult.Found>(result);
        var movie = Assert.Single(found.Movies);
        Assert.Equal("tt2", movie.ExternalId);
    }

    [Fact]
    public async Task AServerError_ReturnsError()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        CatalogMoviesResult result = await client.GetPopularMoviesAsync(CancellationToken.None);

        Assert.IsType<CatalogMoviesResult.Error>(result);
    }

    [Fact]
    public async Task ANetworkFailure_ReturnsUnreachable()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        CatalogMoviesResult result = await client.GetPopularMoviesAsync(CancellationToken.None);

        Assert.IsType<CatalogMoviesResult.Unreachable>(result);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPopularMoviesAsync(cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("GetPopularMoviesAsync must not use IHttpClientFactory.");
    }
}
