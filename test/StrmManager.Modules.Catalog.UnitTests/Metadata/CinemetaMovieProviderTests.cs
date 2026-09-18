using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Movie counterpart of CinemetaMetadataProviderTests - same fake-handler approach, same
/// error taxonomy (see ADR-007), against the movie endpoint.
/// </summary>
public class CinemetaMovieProviderTests
{
    private static CinemetaMetadataProvider CreateProvider(
        FakeHttpMessageHandler handler,
        TimeSpan? httpClientTimeout = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cinemeta.example.invalid/"),
            Timeout = httpClientTimeout ?? TimeSpan.FromSeconds(30),
        };

        return new CinemetaMetadataProvider(httpClient, NullLogger<CinemetaMetadataProvider>.Instance);
    }

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fileName));

    [Fact]
    public async Task GetMovieAsync_SuccessfulResponse_ReturnsMappedMetadata()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("movie-released.json")),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0111161");

        Assert.True(result.IsSuccess);
        Assert.Equal("The Shawshank Redemption", result.Value.Title);
        Assert.Equal(1994, result.Value.Year);
        Assert.Equal("tt0111161", result.Value.ExternalIds.ImdbId);
    }

    [Fact]
    public async Task GetMovieAsync_RequestsTheMovieMetaEndpointWithTheEscapedId()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(LoadFixture("movie-released.json")),
            });
        });

        await CreateProvider(handler).GetMovieAsync("tt0111161");

        Assert.NotNull(captured);
        Assert.Equal("https://cinemeta.example.invalid/meta/movie/tt0111161.json", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetMovieAsync_IdWithReservedCharacters_IsEscapedNotInterpretedAsPath()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        await CreateProvider(handler).GetMovieAsync("tt1/../series/tt2");

        Assert.NotNull(captured);
        Assert.DoesNotContain("/../", captured.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.StartsWith("https://cinemeta.example.invalid/meta/movie/", captured.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMovieAsync_NotFoundStatus_ReturnsMovieNotFound()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.MovieNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetMovieAsync_ServerErrorStatus_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0111161");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieAsync_MalformedJson_ReturnsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json"),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0111161");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieAsync_MissingMetaObject_ReturnsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("missing-meta-object.json")),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0111161");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieAsync_Http200IdOnlyStub_ReturnsMovieNotFound()
    {
        // Observed live: what Cinemeta answers for an unknown movie id.
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("movie-id-only-stub.json")),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.MovieNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetMovieAsync_Http200SeriesStyleEmptyObject_IsNotInferredAsNotFound_StaysInvalidResponse()
    {
        // `{}` was observed for unknown SERIES only. It is not assumed to exist for
        // movies, so if it ever shows up here it is an unrecognized response.
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("unknown-series-empty-object.json")),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData("media-object-missing-name.json")] // a real media object (has year/runtime) that just lacks its name
    [InlineData("missing-meta-object.json")] // unrelated payload, no meta
    public async Task GetMovieAsync_MalformedFixtureThatIsNotTheUnknownMediaStub_StaysInvalidResponse(string fixture)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture(fixture)),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt9999003");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData("""{"meta":null}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"movie","poster":"https://example.invalid/p.jpg"}}""")]
    [InlineData("""{"meta":{"name":"No Id","releaseInfo":"2020"}}""")]
    public async Task GetMovieAsync_OtherMalformedInlineResponses_StayInvalidResponse(string json)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        }));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt1");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieAsync_NetworkFailure_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("simulated DNS/connection failure"));

        Result<MovieMetadata> result = await CreateProvider(handler).GetMovieAsync("tt0111161");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieAsync_RequestExceedsHttpClientTimeout_ReturnsTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Result<MovieMetadata> result = await CreateProvider(handler, httpClientTimeout: TimeSpan.FromMilliseconds(50))
            .GetMovieAsync("tt0111161");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.Timeout", result.Error.Code);
    }

    [Fact]
    public async Task GetMovieAsync_CallerCancelsOwnToken_PropagatesOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cancellationTokenSource = new CancellationTokenSource();
        Task<Result<MovieMetadata>> task = CreateProvider(handler).GetMovieAsync("tt0111161", cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
