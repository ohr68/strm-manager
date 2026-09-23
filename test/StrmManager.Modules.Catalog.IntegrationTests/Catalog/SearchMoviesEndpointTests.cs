using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Catalog.SearchMovies;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Catalog;

/// <summary>
/// GET api/catalog/movies/search (UI-5b) - endpoint-level behavior only (trimming, validation, no-match shape).
/// SearchMoviesAsync's own Cinemeta request/mapping/failure behavior is covered by CinemetaCatalogProviderTests;
/// this never calls Cinemeta - ICatalogProvider is a counting fake.
/// </summary>
public sealed class SearchMoviesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeCatalogProvider _catalogProvider = new();

    public SearchMoviesEndpointTests(ApiWebApplicationFactory factory)
    {
        WebApplicationFactory<Program> isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ICatalogProvider>(_catalogProvider)));

        _client = isolated.CreateClient();
    }

    [Fact]
    public async Task AValidQuery_ReturnsTheMappedMovies()
    {
        _catalogProvider.NextResult = Result.Success<IReadOnlyList<CatalogMovie>>(
            [new CatalogMovie("tt0468569", "The Dark Knight", 2008, "https://images.metahub.space/poster/small/tt0468569/img", [])]);

        HttpResponseMessage response = await _client.GetAsync("/api/catalog/movies/search?query=The%20Dark%20Knight");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchMoviesResponseBody? body = await response.Content.ReadFromJsonAsync<SearchMoviesResponseBody>();
        Movie movie = Assert.Single(body!.Movies);
        Assert.Equal("tt0468569", movie.ExternalId);
        Assert.Equal("The Dark Knight", movie.Title);
        Assert.Equal(2008, movie.Year);
        Assert.Equal("The Dark Knight", _catalogProvider.LastQuery);
    }

    [Fact]
    public async Task ANoMatchesResult_Returns200WithAnEmptyMoviesArray_NotAFailure()
    {
        _catalogProvider.NextResult = Result.Success<IReadOnlyList<CatalogMovie>>([]);

        HttpResponseMessage response = await _client.GetAsync("/api/catalog/movies/search?query=zzzzstrmmanagerthismoviedoesnotexist99999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchMoviesResponseBody? body = await response.Content.ReadFromJsonAsync<SearchMoviesResponseBody>();
        Assert.Empty(body!.Movies);
    }

    [Theory]
    [InlineData("  The Dark Knight  ", "The Dark Knight")]
    [InlineData("\tThe Dark Knight\t", "The Dark Knight")]
    public async Task LeadingAndTrailingWhitespace_IsTrimmed_BeforeReachingTheProvider(string rawQuery, string expectedTrimmedQuery)
    {
        _catalogProvider.NextResult = Result.Success<IReadOnlyList<CatalogMovie>>([]);

        HttpResponseMessage response = await _client.GetAsync($"/api/catalog/movies/search?query={Uri.EscapeDataString(rawQuery)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedTrimmedQuery, _catalogProvider.LastQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    public async Task AnEmptyOrWhitespaceOnlyQuery_Is400_AndNeverCallsTheProvider(string query)
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/catalog/movies/search?query={Uri.EscapeDataString(query)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task AMissingQueryParameter_Is400_AndNeverCallsTheProvider()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/catalog/movies/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task AQueryOver100CharactersAfterTrimming_Is400_AndNeverCallsTheProvider()
    {
        string tooLong = new string('a', 101);

        HttpResponseMessage response = await _client.GetAsync($"/api/catalog/movies/search?query={tooLong}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _catalogProvider.CallCount);
    }

    [Fact]
    public async Task AQueryOfExactly100CharactersAfterTrimming_IsAccepted()
    {
        _catalogProvider.NextResult = Result.Success<IReadOnlyList<CatalogMovie>>([]);
        string exactly100 = new string('a', 100);

        HttpResponseMessage response = await _client.GetAsync($"/api/catalog/movies/search?query={exactly100}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _catalogProvider.CallCount);
    }

    private sealed class FakeCatalogProvider : ICatalogProvider
    {
        public Result<IReadOnlyList<CatalogMovie>> NextResult { get; set; } = Result.Success<IReadOnlyList<CatalogMovie>>([]);

        public string? LastQuery { get; private set; }

        public int CallCount { get; private set; }

        public Task<Result<IReadOnlyList<CatalogMovie>>> GetPopularMoviesAsync(int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The search endpoint must not call GetPopularMoviesAsync.");

        public Task<Result<IReadOnlyList<CatalogMovie>>> SearchMoviesAsync(string query, int limit, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult(NextResult);
        }
    }

    private sealed record SearchMoviesResponseBody(List<Movie> Movies);

    private sealed record Movie(string ExternalId, string Title, int? Year, string? PosterUrl);
}
