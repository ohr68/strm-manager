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
/// GET api/catalog/series/search - endpoint-level behavior only.
/// Provider request/mapping/failure behavior is covered by
/// CinemetaCatalogProviderTests; this never calls Cinemeta.
/// </summary>
public sealed class SearchSeriesEndpointTests
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeCatalogProvider _catalogProvider = new();

    public SearchSeriesEndpointTests(ApiWebApplicationFactory factory)
    {
        WebApplicationFactory<Program> isolated =
            factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<ICatalogProvider>(
                        _catalogProvider)));

        _client = isolated.CreateClient();
    }

    [Fact]
    public async Task AValidQuery_ReturnsTheMappedSeries()
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
                "/api/catalog/series/search?query=Breaking%20Bad");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SearchSeriesResponseBody? body =
            await response.Content
                .ReadFromJsonAsync<SearchSeriesResponseBody>();

        Series series = Assert.Single(body!.Series);

        Assert.Equal("tt0903747", series.ExternalId);
        Assert.Equal("Breaking Bad", series.Title);
        Assert.Equal(2008, series.Year);
        Assert.Equal(
            "https://images.example.invalid/breaking-bad.jpg",
            series.PosterUrl);

        Assert.Equal("Breaking Bad", _catalogProvider.LastQuery);
        Assert.Equal(20, _catalogProvider.LastLimit);
        Assert.Equal(1, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task ANoMatchesResult_Returns200WithAnEmptySeriesArray()
    {
        _catalogProvider.NextResult =
            Result.Success<IReadOnlyList<CatalogSeries>>([]);

        HttpResponseMessage response =
            await _client.GetAsync(
                "/api/catalog/series/search?query=zzzzstrmmanagernoseries99999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SearchSeriesResponseBody? body =
            await response.Content
                .ReadFromJsonAsync<SearchSeriesResponseBody>();

        Assert.NotNull(body);
        Assert.Empty(body.Series);
    }

    [Theory]
    [InlineData("  Breaking Bad  ", "Breaking Bad")]
    [InlineData("\tBreaking Bad\t", "Breaking Bad")]
    public async Task LeadingAndTrailingWhitespace_IsTrimmed_BeforeReachingTheProvider(
        string rawQuery,
        string expectedTrimmedQuery)
    {
        _catalogProvider.NextResult =
            Result.Success<IReadOnlyList<CatalogSeries>>([]);

        HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/catalog/series/search?query={Uri.EscapeDataString(rawQuery)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            expectedTrimmedQuery,
            _catalogProvider.LastQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    public async Task AnEmptyOrWhitespaceOnlyQuery_Is400_AndNeverCallsTheProvider(
        string query)
    {
        HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/catalog/series/search?query={Uri.EscapeDataString(query)}");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(0, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task AMissingQueryParameter_Is400_AndNeverCallsTheProvider()
    {
        HttpResponseMessage response =
            await _client.GetAsync(
                "/api/catalog/series/search");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(0, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task AQueryOver100CharactersAfterTrimming_Is400_AndNeverCallsTheProvider()
    {
        string tooLong = new('a', 101);

        HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/catalog/series/search?query={tooLong}");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(0, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task AQueryOfExactly100CharactersAfterTrimming_IsAccepted()
    {
        _catalogProvider.NextResult =
            Result.Success<IReadOnlyList<CatalogSeries>>([]);

        string exactly100 = new('a', 100);

        HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/catalog/series/search?query={exactly100}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

        public string? LastQuery { get; private set; }

        public int? LastLimit { get; private set; }

        public int CallCount { get; private set; }

        public Task<Result<IReadOnlyList<CatalogMovie>>>
            GetPopularMoviesAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The series search endpoint must not call GetPopularMoviesAsync.");

        public Task<Result<IReadOnlyList<CatalogSeries>>>
            GetPopularSeriesAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The series search endpoint must not call GetPopularSeriesAsync.");

        public Task<Result<IReadOnlyList<CatalogMovie>>>
            SearchMoviesAsync(
                string query,
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The series search endpoint must not call SearchMoviesAsync.");

        public Task<Result<IReadOnlyList<CatalogSeries>>>
            SearchSeriesAsync(
                string query,
                int limit,
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastQuery = query;
            LastLimit = limit;

            return Task.FromResult(NextResult);
        }
    }

    private sealed record SearchSeriesResponseBody(
        List<Series> Series);

    private sealed record Series(
        string ExternalId,
        string Title,
        int? Year,
        string? PosterUrl);
}
