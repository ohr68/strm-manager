using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Catalog;

/// <summary>
/// GET api/catalog/movies/{imdbId}/detail (UI-6b) - endpoint-level behavior only (validation, not-found, provider
/// failure mapping, and - most importantly - proof that selecting/browsing a movie's detail never creates a Movie
/// or calls the AddMovie/GetMovieAsync metadata path). CinemetaMetadataMapper/CinemetaMetadataProvider's own mapping
/// and HTTP behavior are covered by CinemetaMovieDetailMapperTests/CinemetaMovieDetailProviderTests; this never
/// calls Cinemeta - IMetadataProvider is the shared FakeMetadataProvider, isolated per test class instance.
/// </summary>
public sealed class GetMovieDetailEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private readonly FakeMetadataProvider _metadata = new();

    public GetMovieDetailEndpointTests(ApiWebApplicationFactory factory)
    {
        WebApplicationFactory<Program> isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IMetadataProvider>(_metadata)));

        _client = isolated.CreateClient();
        _services = isolated.Services;
    }

    private async Task<int> MovieCountAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<Movie>().CountAsync();
    }

    [Fact]
    public async Task AValidImdbId_WithFullDetail_Returns200WithTheMappedFields()
    {
        _metadata.MovieDetailHandler = _ => new MovieDetail(
            "tt0468569",
            "The Dark Knight",
            2008,
            TimeSpan.FromMinutes(152),
            "When a menace known as the Joker...",
            ["Action", "Crime", "Drama"],
            "9.1",
            "https://images.metahub.space/poster/small/tt0468569/img",
            "https://images.metahub.space/background/medium/tt0468569/img");

        HttpResponseMessage response = await _client.GetAsync("/api/catalog/movies/tt0468569/detail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DetailBody? body = await response.Content.ReadFromJsonAsync<DetailBody>();
        Assert.NotNull(body);
        Assert.Equal("tt0468569", body.ExternalId);
        Assert.Equal("The Dark Knight", body.Title);
        Assert.Equal(2008, body.Year);
        Assert.Equal(152, body.RuntimeMinutes);
        Assert.Equal("When a menace known as the Joker...", body.Description);
        Assert.Equal(["Action", "Crime", "Drama"], body.Genres);
        Assert.Equal("9.1", body.ImdbRating);
        Assert.Equal("https://images.metahub.space/poster/small/tt0468569/img", body.PosterUrl);
        Assert.Equal("https://images.metahub.space/background/medium/tt0468569/img", body.BackdropUrl);
    }

    [Fact]
    public async Task AnUnknownImdbId_Is404_AndCreatesNoMovie_AndNeverCallsTheAddMoviePath()
    {
        int before = await MovieCountAsync();

        HttpResponseMessage response = await _client.GetAsync("/api/catalog/movies/tt7000001/detail");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await MovieCountAsync());
        Assert.Equal(1, _metadata.MovieDetailCallCount); // the detail path was reached, not short-circuited
    }

    [Theory]
    [InlineData("tt")]
    [InlineData("TT0111161")]
    [InlineData("0111161")]
    [InlineData("tt0111161%20")]
    public async Task AMalformedImdbId_Is400_AndNeverCallsTheProvider(string imdbId)
    {
        int before = await MovieCountAsync();

        HttpResponseMessage response = await _client.GetAsync($"/api/catalog/movies/{imdbId}/detail");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _metadata.MovieDetailCallCount);
        Assert.Equal(before, await MovieCountAsync());
    }

    [Fact]
    public async Task AProviderUnavailable_Returns500_ConsistentWithPopularRowsSearch()
    {
        _metadata.MovieDetailHandler = externalId => Result.Failure<MovieDetail>(MetadataProviderErrors.ProviderUnavailable("Fake"));

        HttpResponseMessage response = await _client.GetAsync("/api/catalog/movies/tt0468569/detail");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SelectingAMovieDetail_NeverCreatesAMovie_RegardlessOfOutcome()
    {
        int before = await MovieCountAsync();

        _metadata.MovieDetailHandler = _ => new MovieDetail("tt0468569", "The Dark Knight", 2008, null, null, [], null, null, null);
        await _client.GetAsync("/api/catalog/movies/tt0468569/detail");

        _metadata.MovieDetailHandler = _ => Result.Failure<MovieDetail>(MetadataProviderErrors.MovieNotFound("Fake", "tt0000000"));
        await _client.GetAsync("/api/catalog/movies/tt0000000/detail");

        Assert.Equal(before, await MovieCountAsync());

        // The ordinary AddMovie/GetMovieAsync metadata path (MovieHandler) must never be touched by this endpoint.
        Assert.Equal(2, _metadata.MovieDetailCallCount);
    }

    private sealed record DetailBody(
        string ExternalId,
        string Title,
        int Year,
        int? RuntimeMinutes,
        string? Description,
        List<string> Genres,
        string? ImdbRating,
        string? PosterUrl,
        string? BackdropUrl);
}
