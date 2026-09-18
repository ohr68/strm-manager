using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Tests CinemetaMetadataProvider's own HTTP-status/exception -> Result&lt;Error&gt;
/// mapping directly against a fake handler - no live Cinemeta, no DI, no resilience
/// pipeline (that's configured separately in CatalogModule and is not this class'
/// responsibility to retry/time out on its own - see ADR-007).
/// </summary>
public class CinemetaMetadataProviderTests
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
    public async Task GetSeriesAsync_SuccessfulResponse_ReturnsMappedMetadata()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("stuart-continuing.json")),
        }));

        CinemetaMetadataProvider provider = CreateProvider(handler);

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt27497393");

        Assert.True(result.IsSuccess);
        Assert.Equal("Stuart Fails to Save the Universe", result.Value.Title);
        Assert.Equal(10, result.Value.Episodes.Count);
    }

    [Fact]
    public async Task GetSeriesAsync_NotFoundStatus_ReturnsSeriesNotFound()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        CinemetaMetadataProvider provider = CreateProvider(handler);

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt00000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.SeriesNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetSeriesAsync_ServerErrorStatus_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        CinemetaMetadataProvider provider = CreateProvider(handler);

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt27497393");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetSeriesAsync_MalformedJson_ReturnsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ this is not valid json"),
        }));

        CinemetaMetadataProvider provider = CreateProvider(handler);

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt27497393");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetSeriesAsync_MissingMetaObject_ReturnsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("missing-meta-object.json")),
        }));

        CinemetaMetadataProvider provider = CreateProvider(handler);

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt00000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetSeriesAsync_Http200EmptyObject_ReturnsSeriesNotFound()
    {
        // Observed live: what Cinemeta answers for an unknown series id.
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("unknown-series-empty-object.json")),
        }));

        Result<SeriesMetadata> result = await CreateProvider(handler).GetSeriesAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.SeriesNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetSeriesAsync_Http200MovieStyleIdOnlyStub_IsNotInferredAsNotFound_StaysInvalidResponse()
    {
        // The id-only stub was observed for MOVIES only. It is not assumed to exist for
        // series, so if it ever shows up here it is an unrecognized response.
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("movie-id-only-stub.json")),
        }));

        Result<SeriesMetadata> result = await CreateProvider(handler).GetSeriesAsync("tt0000000");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Theory]
    [InlineData("""{"meta":null}""")]
    [InlineData("""{"error":"unexpected upstream payload"}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"series","poster":"https://example.invalid/p.jpg"}}""")]
    [InlineData("""{"meta":{"id":"tt1","type":"series","releaseInfo":"2020-","status":"Continuing"}}""")]
    public async Task GetSeriesAsync_MalformedResponseThatIsNotTheUnknownMediaStub_StaysInvalidResponse(string json)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        }));

        Result<SeriesMetadata> result = await CreateProvider(handler).GetSeriesAsync("tt1");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetSeriesAsync_NetworkFailure_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("simulated DNS/connection failure"));

        CinemetaMetadataProvider provider = CreateProvider(handler);

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt27497393");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetSeriesAsync_RequestExceedsHttpClientTimeout_ReturnsTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        CinemetaMetadataProvider provider = CreateProvider(handler, httpClientTimeout: TimeSpan.FromMilliseconds(50));

        Result<SeriesMetadata> result = await provider.GetSeriesAsync("tt27497393");

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.Timeout", result.Error.Code);
    }

    [Fact]
    public async Task GetSeriesAsync_CallerCancelsOwnToken_PropagatesOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        CinemetaMetadataProvider provider = CreateProvider(handler);

        using var cancellationTokenSource = new CancellationTokenSource();
        Task<Result<SeriesMetadata>> task = provider.GetSeriesAsync("tt27497393", cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
