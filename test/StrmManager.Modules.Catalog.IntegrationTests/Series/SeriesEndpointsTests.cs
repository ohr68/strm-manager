using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Series;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Series;

public class SeriesEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeMetadataProvider _metadataProvider;

    public SeriesEndpointsTests(ApiWebApplicationFactory factory)
    {
        _metadataProvider = new FakeMetadataProvider();

        WebApplicationFactory<Program> isolatedFactory =
            factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IMetadataProvider>(_metadataProvider)));

        _client = isolatedFactory.CreateClient();
    }

    private sealed record AddSeriesRequest(string ImdbId);

    private sealed record CreatedResponse(Guid Id);

    private sealed record SeriesResponse(
        Guid Id,
        string? ImdbId,
        string Title,
        int Year,
        string Status);

    [Fact]
    public async Task AddSeries_ThenGetSeries_ReturnsProviderMetadata()
    {
        const string imdbId = "tt27497393";

        _metadataProvider.Handler = _ =>
            BuildMetadata(
                imdbId,
                title: "Paradise",
                year: 2025);

        var request = new AddSeriesRequest(imdbId);

        HttpResponseMessage postResponse =
            await _client.PostAsJsonAsync("/api/series", request);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        CreatedResponse? created =
            await postResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        Assert.NotNull(created);

        HttpResponseMessage getResponse =
            await _client.GetAsync($"/api/series/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        SeriesResponse? series =
            await getResponse.Content.ReadFromJsonAsync<SeriesResponse>();

        Assert.NotNull(series);
        Assert.Equal(imdbId, series.ImdbId);
        Assert.Equal("Paradise", series.Title);
        Assert.Equal(2025, series.Year);
        Assert.Equal("Active", series.Status);
    }

    [Fact]
    public async Task AddSeries_CalledTwiceWithSameImdbId_DoesNotCreateADuplicate()
    {
        const string imdbId = "tt99999999";

        _metadataProvider.Handler = _ =>
            BuildMetadata(
                imdbId,
                title: "Duplicate Test Series",
                year: 2026);

        var request = new AddSeriesRequest(imdbId);

        HttpResponseMessage firstResponse =
            await _client.PostAsJsonAsync("/api/series", request);

        HttpResponseMessage secondResponse =
            await _client.PostAsJsonAsync("/api/series", request);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        CreatedResponse? first =
            await firstResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        CreatedResponse? second =
            await secondResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task AddSeries_WithoutImdbId_ReturnsValidationProblem()
    {
        var request = new AddSeriesRequest(string.Empty);

        HttpResponseMessage response =
            await _client.PostAsJsonAsync("/api/series", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddSeries_WhenMetadataDoesNotExist_Returns404()
    {
        const string imdbId = "tt00000000";

        _metadataProvider.Handler = externalId =>
            Result.Failure<SeriesMetadata>(
                MetadataProviderErrors.SeriesNotFound(
                    "Fake",
                    externalId));

        var request = new AddSeriesRequest(imdbId);

        HttpResponseMessage response =
            await _client.PostAsJsonAsync("/api/series", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSeries_WhenNotFound_Returns404()
    {
        HttpResponseMessage response =
            await _client.GetAsync($"/api/series/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Result<SeriesMetadata> BuildMetadata(
        string imdbId,
        string title,
        int year)
    {
        return new SeriesMetadata(
            new ExternalIds(
                imdbId,
                TmdbId: null,
                TvdbId: null),
            title,
            OriginalTitle: null,
            year,
            SeriesStatus.Active,
            Episodes: []);
    }
}
