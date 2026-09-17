using System.Net;
using System.Net.Http.Json;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Series;

public class SeriesEndpointsTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private sealed record AddSeriesRequest(string ImdbId, string? TmdbId, string? TvdbId, string Title, string? OriginalTitle, int Year);

    private sealed record CreatedResponse(Guid Id);

    private sealed record SeriesResponse(Guid Id, string? ImdbId, string Title, int Year, string Status);

    [Fact]
    public async Task AddSeries_ThenGetSeries_ReturnsThePersistedSeries()
    {
        HttpClient client = factory.CreateClient();
        var request = new AddSeriesRequest("tt27497393", "12345", null, "Paradise", null, 2025);

        HttpResponseMessage postResponse = await client.PostAsJsonAsync("/api/series", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        CreatedResponse? created = await postResponse.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(created);

        HttpResponseMessage getResponse = await client.GetAsync($"/api/series/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        SeriesResponse? series = await getResponse.Content.ReadFromJsonAsync<SeriesResponse>();
        Assert.NotNull(series);
        Assert.Equal("Paradise", series.Title);
        Assert.Equal("tt27497393", series.ImdbId);
        Assert.Equal("Active", series.Status);
    }

    [Fact]
    public async Task AddSeries_CalledTwiceWithSameImdbId_DoesNotCreateADuplicate()
    {
        HttpClient client = factory.CreateClient();
        var request = new AddSeriesRequest("tt99999999", null, null, "Duplicate Test Series", null, 2026);

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync("/api/series", request);
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync("/api/series", request);

        CreatedResponse? first = await firstResponse.Content.ReadFromJsonAsync<CreatedResponse>();
        CreatedResponse? second = await secondResponse.Content.ReadFromJsonAsync<CreatedResponse>();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task AddSeries_WithoutTitle_ReturnsValidationProblem()
    {
        HttpClient client = factory.CreateClient();
        var request = new AddSeriesRequest("tt00000000", null, null, string.Empty, null, 2026);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/series", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSeries_WhenNotFound_Returns404()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/api/series/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
