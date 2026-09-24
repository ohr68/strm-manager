using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.GetMovieDetailAsync (UI-6c) - no real network, no running plugin/
/// Jellyfin host. Mirrors GetByImdbIdAsync's own test shape (same five-case result taxonomy, same fake-handler
/// approach) since this call follows the identical "look up one thing by IMDb id" client pattern.
/// </summary>
public sealed class GetMovieDetailAsyncTests
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
    public async Task AValidResponse_RequestsTheEscapedDetailUrl_AndReturnsFound_WithTheMappedFields()
    {
        const string body = """
            {"externalId":"tt0468569","title":"The Dark Knight","year":2008,"runtimeMinutes":152,"description":"When a menace...","genres":["Action","Crime","Drama"],"imdbRating":"9.1","posterUrl":"https://example.test/poster.jpg","backdropUrl":"https://example.test/backdrop.jpg"}
            """;
        HttpRequestMessage? captured = null;
        StrmManagerClient.StrmManagerClient client = CreateClient((request, _) =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
        });

        MovieDetailResult result = await client.GetMovieDetailAsync("tt0468569", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("http://strm-manager.test/api/catalog/movies/tt0468569/detail", captured.RequestUri!.AbsoluteUri);

        var found = Assert.IsType<MovieDetailResult.Found>(result);
        Assert.Equal("tt0468569", found.Detail.ExternalId);
        Assert.Equal("The Dark Knight", found.Detail.Title);
        Assert.Equal(2008, found.Detail.Year);
        Assert.Equal(152, found.Detail.RuntimeMinutes);
        Assert.Equal("When a menace...", found.Detail.Description);
        Assert.Equal(["Action", "Crime", "Drama"], found.Detail.Genres);
        Assert.Equal("9.1", found.Detail.ImdbRating);
        Assert.Equal("https://example.test/poster.jpg", found.Detail.PosterUrl);
        Assert.Equal("https://example.test/backdrop.jpg", found.Detail.BackdropUrl);
    }

    [Fact]
    public async Task AResponseMissingOptionalFields_StillReturnsFound()
    {
        const string body = """{"externalId":"tt0468569","title":"The Dark Knight","year":2008}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        MovieDetailResult result = await client.GetMovieDetailAsync("tt0468569", CancellationToken.None);

        var found = Assert.IsType<MovieDetailResult.Found>(result);
        Assert.Null(found.Detail.RuntimeMinutes);
        Assert.Null(found.Detail.Description);
        Assert.Empty(found.Detail.Genres);
    }

    [Fact]
    public async Task ANotFoundStatus_ReturnsNotFound()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        MovieDetailResult result = await client.GetMovieDetailAsync("tt0000000", CancellationToken.None);

        Assert.IsType<MovieDetailResult.NotFound>(result);
    }

    [Fact]
    public async Task ABadRequestStatus_ReturnsInvalid()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        MovieDetailResult result = await client.GetMovieDetailAsync("not-an-imdb-id", CancellationToken.None);

        Assert.IsType<MovieDetailResult.Invalid>(result);
    }

    [Fact]
    public async Task AServerError_ReturnsError()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        MovieDetailResult result = await client.GetMovieDetailAsync("tt0468569", CancellationToken.None);

        Assert.IsType<MovieDetailResult.Error>(result);
    }

    [Fact]
    public async Task ANetworkFailure_ReturnsUnreachable()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        MovieDetailResult result = await client.GetMovieDetailAsync("tt0468569", CancellationToken.None);

        Assert.IsType<MovieDetailResult.Unreachable>(result);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMovieDetailAsync("tt0468569", cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("GetMovieDetailAsync must not use IHttpClientFactory.");
    }
}
