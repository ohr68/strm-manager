using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Catalog;

/// <summary>
/// GET api/catalog/series/popular - endpoint-level behavior only.
/// Cinemeta request, mapping, and provider failure behavior are covered by
/// CinemetaCatalogProviderTests; this test replaces ICatalogProvider with a fake.
/// </summary>
public sealed class GetPopularSeriesEndpointTests
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeCatalogProvider _catalogProvider = new();

    public GetPopularSeriesEndpointTests(
        ApiWebApplicationFactory factory)
    {
        WebApplicationFactory<Program> isolated =
            factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<ICatalogProvider>(
                        _catalogProvider)));

        _client = isolated.CreateClient();
    }

    [Fact]
    public async Task PopularSeries_ReturnsTheMappedSeries()
    {
        _catalogProvider.NextResult =
            Result.Success<IReadOnlyList<CatalogSeries>>(
            [
                new CatalogSeries(
                    "tt0903747",
                    "Breaking Bad",
                    2008,
                    "https://images.example.invalid/breaking-bad.jpg",
                    ["Crime", "Drama"])
            ]);

        HttpResponseMessage response =
            await _client.GetAsync(
                "/api/catalog/series/popular");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        PopularSeriesResponseBody? body =
            await response.Content
                .ReadFromJsonAsync<PopularSeriesResponseBody>();

        Series series = Assert.Single(body!.Series);

        Assert.Equal("tt0903747", series.ExternalId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal(2008, series.Year);
        Assert.Equal(
            "https://images.example.invalid/breaking-bad.jpg",
            series.PosterUrl);

        Assert.Equal(1, _catalogProvider.CallCount);
        Assert.Equal(20, _catalogProvider.LastLimit);
    }

    [Fact]
    public async Task NoPopularSeries_Returns200WithAnEmptySeriesArray()
    {
        _catalogProvider.NextResult =
            Result.Success<IReadOnlyList<CatalogSeries>>([]);

        HttpResponseMessage response =
            await _client.GetAsync(
                "/api/catalog/series/popular");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        PopularSeriesResponseBody? body =
            await response.Content
                .ReadFromJsonAsync<PopularSeriesResponseBody>();

        Assert.NotNull(body);
        Assert.Empty(body.Series);

        Assert.Equal(1, _catalogProvider.CallCount);
        Assert.Equal(20, _catalogProvider.LastLimit);
    }

    private sealed class FakeCatalogProvider
        : ICatalogProvider
    {
        public Result<IReadOnlyList<CatalogSeries>> NextResult
        {
            get;
            set;
        } = Result.Success<IReadOnlyList<CatalogSeries>>([]);

        public int CallCount { get; private set; }

        public int? LastLimit { get; private set; }

        public Task<Result<IReadOnlyList<CatalogMovie>>>
            GetPopularMoviesAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The popular series endpoint must not call GetPopularMoviesAsync.");

        public Task<Result<IReadOnlyList<CatalogSeries>>>
            GetPopularSeriesAsync(
                int limit,
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastLimit = limit;

            return Task.FromResult(NextResult);
        }

        public Task<Result<IReadOnlyList<CatalogMovie>>>
            SearchMoviesAsync(
                string query,
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The popular series endpoint must not call SearchMoviesAsync.");

        public Task<Result<IReadOnlyList<CatalogSeries>>> SearchSeriesAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "This endpoint must not call SearchSeriesAsync.");
    }

    private sealed record PopularSeriesResponseBody(
        List<Series> Series);

    private sealed record Series(
        string ExternalId,
        string Title,
        int? Year,
        string? PosterUrl);
}
