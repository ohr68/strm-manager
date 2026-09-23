using System.Net;
using System.Text;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for StrmManagerClient.AddMovieAsync - no real network, no running plugin/Jellyfin host. Each
/// test drives a fake HttpMessageHandler directly, matching the six scenarios the P2 report committed to. P1's
/// GetByImdbIdAsync tests (StrmManagerClientTests.cs) are untouched and not re-run here.
/// </summary>
public sealed class AddMovieAsyncTests
{
    private const string ImdbId = "tt0137523";

    private static StrmManagerClient.StrmManagerClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new FakeHandler(handle)) { BaseAddress = new Uri("http://strm-manager.test/") };
        return new StrmManagerClient.StrmManagerClient(httpClient, new NeverCalledHttpClientFactory(), NullLogger<StrmManagerClient.StrmManagerClient>.Instance);
    }

    /// <summary>
    /// AddMovieAsync never uses IHttpClientFactory (only ProcessMovieAsync does - see ProcessMovieAsyncTests.cs)
    /// - a throwing fake makes any accidental future use of it here fail loudly instead of silently.
    /// </summary>
    private sealed class NeverCalledHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException("AddMovieAsync must not use IHttpClientFactory.");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task AValidRequest_Returns201AsCreated_WithTheMovieId()
    {
        const string body = """{"id":"1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6"}""";
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.Created, body)));

        AddMovieResult result = await client.AddMovieAsync(ImdbId, CancellationToken.None);

        var created = Assert.IsType<AddMovieResult.Created>(result);
        Assert.Equal(Guid.Parse("1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6"), created.MovieId);
    }

    [Fact]
    public async Task ADuplicateImdbId_Returns409AsAlreadyExists()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));

        AddMovieResult result = await client.AddMovieAsync(ImdbId, CancellationToken.None);

        Assert.IsType<AddMovieResult.AlreadyExists>(result);
    }

    [Fact]
    public async Task ABackendValidationFailure_Returns400AsInvalid()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        AddMovieResult result = await client.AddMovieAsync("not-an-imdb-id", CancellationToken.None);

        Assert.IsType<AddMovieResult.Invalid>(result);
    }

    [Fact]
    public async Task ANetworkFailure_ReturnsUnreachable()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        AddMovieResult result = await client.AddMovieAsync(ImdbId, CancellationToken.None);

        Assert.IsType<AddMovieResult.Unreachable>(result);
    }

    [Fact]
    public async Task AMalformed201Response_ReturnsError()
    {
        StrmManagerClient.StrmManagerClient client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.Created, "{ this is not valid json")));

        AddMovieResult result = await client.AddMovieAsync(ImdbId, CancellationToken.None);

        Assert.IsType<AddMovieResult.Error>(result);
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

        // Same reasoning as P1's equivalent test: HttpClient wraps a cancellation as TaskCanceledException (a
        // subclass of OperationCanceledException), so this checks the base type, not the exact concrete type.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AddMovieAsync(ImdbId, cts.Token));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }
}
