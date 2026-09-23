using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta;

namespace StrmManager.Modules.Catalog.UnitTests.Metadata;

/// <summary>
/// Focused unit tests for CinemetaCatalogProvider - same fake-handler approach as CinemetaMovieProviderTests, live
/// endpoint/shape verified before implementation (see the UI-2 report), not assumed.
/// </summary>
public class CinemetaCatalogProviderTests
{
    private static CinemetaCatalogProvider CreateProvider(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cinemeta.example.invalid/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        return new CinemetaCatalogProvider(httpClient, NullLogger<CinemetaCatalogProvider>.Instance);
    }

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Metadata", "Fixtures", fileName));

    [Fact]
    public async Task GetPopularMoviesAsync_SuccessfulResponse_ReturnsMappedMovies()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("catalog-movies-top.json")),
        }));

        Result<IReadOnlyList<CatalogMovie>> result = await CreateProvider(handler).GetPopularMoviesAsync(20);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(new CatalogMovie("tt27165187", "The End of Oak Street", 2026, "https://images.metahub.space/poster/small/tt27165187/img"), result.Value[0]);
        Assert.Equal(new CatalogMovie("tt28014327", "Mayday", 2026, "https://images.metahub.space/poster/small/tt28014327/img"), result.Value[1]);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_RequestsTheCatalogTopEndpoint()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            captured = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(LoadFixture("catalog-movies-top.json")),
            });
        });

        await CreateProvider(handler).GetPopularMoviesAsync(20);

        Assert.NotNull(captured);
        Assert.Equal("https://cinemeta.example.invalid/catalog/movie/top.json", captured.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_MoreItemsThanLimit_ReturnsOnlyLimitCount()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LoadFixture("catalog-movies-top.json")),
        }));

        Result<IReadOnlyList<CatalogMovie>> result = await CreateProvider(handler).GetPopularMoviesAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("tt27165187", result.Value[0].ExternalId);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_ItemsMissingIdOrName_AreSkippedNotFailed()
    {
        const string body = """{"metas":[{"name":"No Id","releaseInfo":"2020"},{"id":"tt1","releaseInfo":"2020"},{"id":"tt2","name":"Has Both","releaseInfo":"2020"}]}""";
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        }));

        Result<IReadOnlyList<CatalogMovie>> result = await CreateProvider(handler).GetPopularMoviesAsync(20);

        Assert.True(result.IsSuccess);
        var movie = Assert.Single(result.Value);
        Assert.Equal("tt2", movie.ExternalId);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_MissingMetasArray_ReturnsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        }));

        Result<IReadOnlyList<CatalogMovie>> result = await CreateProvider(handler).GetPopularMoviesAsync(20);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_ServerErrorStatus_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Result<IReadOnlyList<CatalogMovie>> result = await CreateProvider(handler).GetPopularMoviesAsync(20);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_NetworkFailure_ReturnsProviderUnavailable()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("simulated DNS/connection failure"));

        Result<IReadOnlyList<CatalogMovie>> result = await CreateProvider(handler).GetPopularMoviesAsync(20);

        Assert.True(result.IsFailure);
        Assert.Equal("Metadata.ProviderUnavailable", result.Error.Code);
    }

    [Fact]
    public async Task GetPopularMoviesAsync_CallerCancelsOwnToken_PropagatesOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var cancellationTokenSource = new CancellationTokenSource();
        Task<Result<IReadOnlyList<CatalogMovie>>> task = CreateProvider(handler).GetPopularMoviesAsync(20, cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
