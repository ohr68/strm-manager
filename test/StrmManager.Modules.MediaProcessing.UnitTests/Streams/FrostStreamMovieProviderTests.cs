using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Infrastructure.Streams.FrostStream;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Streams;

/// <summary>
/// Movie counterpart of FrostStreamProviderTests - same fake-handler approach and same
/// error taxonomy, against the movie endpoint (GET /stream/movie/{imdbId}.json, the
/// Stremio convention the existing series lookup already follows). No identity filtering
/// here either: the provider maps whatever FrostStream returned, and never depends on the
/// live service. Behavior of the live movie endpoint for unknown ids / no streams is NOT
/// observed - anything non-2xx is treated as ProviderUnavailable, exactly as for series.
/// </summary>
public class FrostStreamMovieProviderTests
{
    private static readonly MovieStreamReference Reference =
        new("tt0111161", "The Shawshank Redemption", 1994, TimeSpan.FromMinutes(142));

    private static FrostStreamProvider CreateProvider(FakeHttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://froststream.example.invalid/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };

        return new FrostStreamProvider(httpClient, NullLogger<FrostStreamProvider>.Instance);
    }

    private static FakeHttpMessageHandler Respond(HttpStatusCode status, string? body = null) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = body is null ? null : new StringContent(body),
        }));

    [Fact]
    public async Task GetMovieStreamsAsync_RequestsTheMovieStreamEndpointWithTheImdbId()
    {
        FakeHttpMessageHandler handler = Respond(HttpStatusCode.OK, """{ "streams": [] }""");

        await CreateProvider(handler).GetMovieStreamsAsync(Reference);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("https://froststream.example.invalid/stream/movie/tt0111161.json", handler.LastRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_IdWithReservedCharacters_IsEscapedNotInterpretedAsPath()
    {
        FakeHttpMessageHandler handler = Respond(HttpStatusCode.OK, """{ "streams": [] }""");

        await CreateProvider(handler).GetMovieStreamsAsync(Reference with { ImdbId = "tt1/../series/tt2" });

        string requested = handler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("/../", requested, StringComparison.Ordinal);
        Assert.StartsWith("https://froststream.example.invalid/stream/movie/", requested, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_EmptyStreamsArray_ReturnsSuccessWithNoCandidates()
    {
        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(
            Respond(HttpStatusCode.OK, """{ "streams": [] }""")).GetMovieStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_OneCandidate_ReturnsMappedCandidate()
    {
        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(Respond(
            HttpStatusCode.OK,
            """{ "streams": [ { "name": "FrostStream 1080p", "title": "The Shawshank Redemption (1994)", "url": "https://media.example.test/movie" } ] }"""))
            .GetMovieStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        StreamCandidate candidate = Assert.Single(result.Value);
        Assert.Equal("FrostStream", candidate.Provider);
        Assert.Equal("FrostStream 1080p", candidate.Name);
        Assert.Equal("https://media.example.test/movie", candidate.Url);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_MultipleCandidates_ReturnsAllInResponseOrder()
    {
        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(Respond(
            HttpStatusCode.OK,
            """
            {
                "streams": [
                    { "name": "Source A", "title": "720p", "url": "https://media.example.test/a" },
                    { "name": "Source B", "title": "1080p", "url": "https://media.example.test/b" }
                ]
            }
            """)).GetMovieStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Source A", "Source B"], result.Value.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task GetMovieStreamsAsync_NonHttpUrl_IsFilteredOut()
    {
        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(Respond(
            HttpStatusCode.OK, """{ "streams": [ { "name": "Bad", "url": "file:///etc/passwd" } ] }"""))
            .GetMovieStreamsAsync(Reference);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)] // live behavior for movies is unobserved: parity with series (any non-2xx)
    public async Task GetMovieStreamsAsync_NonSuccessStatus_ReturnsProviderUnavailable(HttpStatusCode status)
    {
        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(Respond(status)).GetMovieStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.ProviderUnavailable", result.Error.Code);
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("null")]
    public async Task GetMovieStreamsAsync_MalformedOrEmptyBody_ReturnsInvalidResponse(string body)
    {
        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(
            Respond(HttpStatusCode.OK, body)).GetMovieStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_NetworkFailure_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("simulated connection failure"));

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler).GetMovieStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_RequestExceedsTimeout_ReturnsTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Result<IReadOnlyList<StreamCandidate>> result = await CreateProvider(handler, timeout: TimeSpan.FromMilliseconds(50))
            .GetMovieStreamsAsync(Reference);

        Assert.True(result.IsFailure);
        Assert.Equal("Streams.Timeout", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieStreamsAsync_CallerCancelsOwnToken_PropagatesOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cancellationTokenSource = new CancellationTokenSource();
        Task<Result<IReadOnlyList<StreamCandidate>>> task = CreateProvider(handler).GetMovieStreamsAsync(Reference, cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
