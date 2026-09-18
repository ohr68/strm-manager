using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Movies;

/// <summary>
/// POST /api/movies through the REAL CinemetaMetadataProvider, mapper and API - only the
/// HTTP transport is faked, so what Cinemeta's wire responses turn into at the API
/// boundary is proven end to end (never live Cinemeta). The default factory swaps in
/// FakeMetadataProvider; these tests put the real typed-client registration back.
/// </summary>
public class MovieCinemetaEndToEndTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string IdOnlyStub =
        """{"meta":{"id":"tt0060900","type":"movie","behaviorHints":{"hasScheduledVideos":false}}}""";

    private HttpClient CreateClient(string cinemetaResponseBody, List<string> requestedPaths) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // ApiWebApplicationFactory registered FakeMetadataProvider last; removing just
            // that descriptor restores the real Cinemeta typed-client registration.
            ServiceDescriptor fake = services.Last(d => d.ServiceType == typeof(IMetadataProvider));
            services.Remove(fake);
            Assert.DoesNotContain(services, d => d.ServiceType == typeof(IMetadataProvider) && d.ImplementationType == typeof(FakeMetadataProvider));

            var handler = new StubCinemetaHandler(cinemetaResponseBody, requestedPaths);
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = handler));
        })).CreateClient();

    [Fact]
    public async Task PostMovie_CinemetaHttp200UnknownMovieStub_Returns404MovieNotFound()
    {
        // Observed live: what Cinemeta answers for an unknown movie id.
        var requestedPaths = new List<string>();
        HttpClient client = CreateClient(IdOnlyStub, requestedPaths);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/movies", new { ImdbId = "tt0060900" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Metadata.MovieNotFound", problem?.Title);
        Assert.Equal("/meta/movie/tt0060900.json", Assert.Single(requestedPaths));
    }

    [Fact]
    public async Task PostMovie_CinemetaHttp200SeriesStyleEmptyObject_IsNotInferredAsNotFound_Returns500InvalidResponse()
    {
        // `{}` was observed for unknown SERIES ids only - not assumed for movies.
        var requestedPaths = new List<string>();
        HttpClient client = CreateClient("{}", requestedPaths);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/movies", new { ImdbId = "tt0060903" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Metadata.InvalidResponse", problem?.Title);
        Assert.Single(requestedPaths);
    }

    [Fact]
    public async Task PostMovie_CinemetaHttp200MalformedMediaObject_StillReturns500InvalidResponse()
    {
        var requestedPaths = new List<string>();
        // A real media object (has a year and runtime) that is missing its name - not the stub.
        HttpClient client = CreateClient(
            """{"meta":{"id":"tt0060901","type":"movie","releaseInfo":"2020","runtime":"90 min"}}""",
            requestedPaths);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/movies", new { ImdbId = "tt0060901" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Metadata.InvalidResponse", problem?.Title);
        Assert.Single(requestedPaths);
    }

    [Fact]
    public async Task PostMovie_RealisticCinemetaMovieResponse_CreatesTheMovieEndToEnd()
    {
        var requestedPaths = new List<string>();
        HttpClient client = CreateClient(
            """
            {"meta":{"id":"tt0060902","imdb_id":"tt0060902","type":"movie","name":"Wire Format Movie",
             "year":"1994","releaseInfo":"1994","released":"1994-10-14T00:00:00.000Z","runtime":"142 min",
             "genres":["Drama"],"videos":[]}}
            """,
            requestedPaths);

        HttpResponseMessage post = await client.PostAsJsonAsync("/api/movies", new { ImdbId = "tt0060902" });

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        CreatedResponse? created = await post.Content.ReadFromJsonAsync<CreatedResponse>();
        MovieBody? movie = await (await client.GetAsync($"/api/movies/{created!.Id}")).Content.ReadFromJsonAsync<MovieBody>();
        Assert.Equal("Wire Format Movie", movie!.Title);
        Assert.Equal(1994, movie.Year);
        Assert.Equal("Pending", movie.Status);
        Assert.Equal(new DateTime(1994, 10, 14, 0, 0, 0, DateTimeKind.Utc), movie.ReleaseAtUtc);
    }

    private sealed class StubCinemetaHandler(string body, List<string> requestedPaths) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }

    private sealed record CreatedResponse(Guid Id);

    private sealed record ProblemBody(string? Title, string? Detail);

    private sealed record MovieBody(string Title, int Year, string Status, DateTime ReleaseAtUtc);
}
