using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Application.Movies.GetMovie;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Movies;

/// <summary>
/// GET /api/movies/by-imdb/{imdbId} (Jellyfin plugin G1): find a movie's id and state from its IMDb id. It is purely observational, so these tests
/// prove three things beyond the happy path: it answers whatever state the movie is in, it returns exactly the representation GET
/// /api/movies/{id} returns, and it has no effect - no Cinemeta, stream-provider or ffprobe call, and no row created or changed.
/// Every provider here is a counting fake; nothing touches the network.
/// </summary>
public class MovieByImdbEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private readonly FakeMetadataProvider _metadata = new();
    private int _metadataCalls;
    private int _streamCalls;
    private int _ffprobeCalls;

    public MovieByImdbEndpointsTests(ApiWebApplicationFactory factory)
    {
        _metadata.MovieHandler = id =>
        {
            Interlocked.Increment(ref _metadataCalls);
            return Result.Failure<MovieMetadata>(MetadataProviderErrors.MovieNotFound("Fake", id));
        };

        var streams = new FakeStreamProvider
        {
            MovieHandler = _ =>
            {
                Interlocked.Increment(ref _streamCalls);
                return Result.Success<IReadOnlyList<StreamCandidate>>(Array.Empty<StreamCandidate>());
            },
        };

        var validator = new FakeMediaValidator
        {
            Handler = (_, _) =>
            {
                Interlocked.Increment(ref _ffprobeCalls);
                return MediaValidationResult.ForRejection(StrmManager.Modules.Catalog.Domain.SourceAttempts.SourceAttemptResult.Rejected, "not expected");
            },
        };

        WebApplicationFactory<Program> isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(UtcNow));
                services.AddSingleton<IMetadataProvider>(_metadata);
                services.AddSingleton<IStreamProvider>(streams);
                services.AddSingleton<IMediaValidator>(validator);
            }));

        _client = isolated.CreateClient();
        _services = isolated.Services;
    }

    public static TheoryData<MediaStatus> EveryStatus() =>
        new()
        {
            MediaStatus.Scheduled,
            MediaStatus.Pending,
            MediaStatus.Searching,
            MediaStatus.Validating,
            MediaStatus.Completed,
            MediaStatus.Unavailable,
            MediaStatus.Error,
        };

    private async Task<int> MovieCountAsync()
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<Movie>().CountAsync();
    }

    private void AssertNothingWasCalledOut()
    {
        Assert.Equal(0, _metadataCalls);
        Assert.Equal(0, _streamCalls);
        Assert.Equal(0, _ffprobeCalls);
    }

    private static object Snapshot(Movie m) =>
        (m.Id, m.ExternalIds, m.Title, m.Year, m.Runtime, m.ReleaseAtUtc, m.Status, m.LastAttemptAtUtc, m.NextAttemptAtUtc, m.AttemptCount, m.LastError, m.CreatedAtUtc, m.UpdatedAtUtc);

    // ---- A / C: found, in every state ----

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public async Task AMovie_IsFoundByItsImdbId_WhateverStateItIsIn(MediaStatus status)
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_services, status, imdbId: imdbId);

        HttpResponseMessage response = await _client.GetAsync($"/api/movies/by-imdb/{imdbId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MovieResponse? movie = await response.Content.ReadFromJsonAsync<MovieResponse>();
        Assert.NotNull(movie);
        Assert.Equal(movieId, movie.Id);
        Assert.Equal(imdbId, movie.ImdbId);
        Assert.Equal(status.ToString(), movie.Status);
        AssertNothingWasCalledOut();
    }

    // ---- B: the same representation as GET by id ----

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public async Task BothLookups_ReturnTheIdenticalRepresentation_OfTheSameMovie(MediaStatus status)
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_services, status, title: "Equivalence Movie", year: 2019, imdbId: imdbId, tmdbId: "12345");

        string byId = await (await _client.GetAsync($"/api/movies/{movieId}")).Content.ReadAsStringAsync();
        string byImdb = await (await _client.GetAsync($"/api/movies/by-imdb/{imdbId}")).Content.ReadAsStringAsync();

        Assert.Equal(byId, byImdb); // byte-for-byte: same fields, same names, same formatting
    }

    [Fact]
    public async Task ARepresentation_HasExactlyTheExistingMovieResponseFields_AndNothingMore()
    {
        string imdbId = NextImdbId();
        await SeedMovieAsync(_services, MediaStatus.Unavailable, imdbId: imdbId); // a movie that HAS attempt/error data internally

        using JsonDocument body = JsonDocument.Parse(await (await _client.GetAsync($"/api/movies/by-imdb/{imdbId}")).Content.ReadAsStringAsync());

        // G3 (attempt count, last error, next attempt, sources) is deliberately not part of this contract.
        Assert.Equal(
            ["createdAtUtc", "id", "imdbId", "releaseAtUtc", "runtime", "status", "title", "tmdbId", "tvdbId", "updatedAtUtc", "year"],
            body.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task AMovieAddedThroughPostMovie_IsEquivalentThroughBothLookups()
    {
        _metadata.MovieHandler = id => new MovieMetadata(new ExternalIds(id, "550", null), "Added Movie", 1999, TimeSpan.FromMinutes(139), UtcNow.AddYears(-1));
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/movies", new { ImdbId = "tt0137523" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Guid id = (await created.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;

        string byId = await (await _client.GetAsync($"/api/movies/{id}")).Content.ReadAsStringAsync();
        string byImdb = await (await _client.GetAsync("/api/movies/by-imdb/tt0137523")).Content.ReadAsStringAsync();

        Assert.Equal(byId, byImdb);
        Assert.Equal("Pending", (await _client.GetFromJsonAsync<MovieResponse>("/api/movies/by-imdb/tt0137523"))!.Status);
    }

    // ---- D: unknown ----

    [Fact]
    public async Task AnUnknownImdbId_Is404_WithTheMoviesNotFoundCode_AndCreatesNothing()
    {
        int before = await MovieCountAsync();

        HttpResponseMessage response = await _client.GetAsync("/api/movies/by-imdb/tt7000001");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Movies.NotFound", problem?.Title); // the same code GET by id uses
        Assert.Contains("tt7000001", problem?.Detail, StringComparison.Ordinal); // it may name the requested id...
        Assert.DoesNotContain("provider", problem?.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase); // ...but nothing about providers
        Assert.DoesNotContain("Cinemeta", problem?.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        AssertNothingWasCalledOut(); // in particular: an unknown id is NOT looked up at Cinemeta, unlike POST /api/movies
        Assert.Equal(before, await MovieCountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/movies/by-imdb/tt7000001")).StatusCode); // still absent: the lookup created nothing
    }

    // ---- E: malformed ids ----

    [Theory]
    [InlineData("tt")]                                    // no digits
    [InlineData("TT0111161")]                             // the prefix is lowercase only
    [InlineData("Tt0111161")]
    [InlineData("ttabc")]
    [InlineData("0111161")]                               // no prefix
    [InlineData("tt12345678901234567")]                   // 17 digits: over the 15-digit rule
    [InlineData("tt1234567890123456")]                    // 16 digits: just over
    [InlineData("tt0111161tt0111161tt0111161tt0111161")]  // longer than the 32-character column
    [InlineData("%20tt0111161")]                          // a leading space (URL-encoded)
    [InlineData("tt0111161%20")]                          // a trailing space
    [InlineData("tt1%2F..%2Fseries%2Ftt2")]               // path-like: an ENCODED slash reaches the endpoint as one value
    [InlineData("..%2F..%2Fapi%2Fstatus")]
    [InlineData("tt0111161'%20OR%201=1--")]
    [InlineData("b37173c0-e45f-45da-bd1e-84bb8f2976d7")]  // a movie id is not an IMDb id
    public async Task AMalformedImdbId_Is400_AndHasNoEffect(string imdbId)
    {
        string existing = NextImdbId();
        await SeedMovieAsync(_services, MediaStatus.Pending, imdbId: existing);
        int before = await MovieCountAsync();

        HttpResponseMessage response = await _client.GetAsync($"/api/movies/by-imdb/{imdbId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNothingWasCalledOut();
        Assert.Equal(before, await MovieCountAsync());
    }

    [Theory]
    [InlineData("/api/movies/by-imdb/")]                   // an empty id: routing finds no endpoint, so this is NOT validated (404, empty body)
    [InlineData("/api/movies/by-imdb")]                    // no id segment at all
    [InlineData("/api/movies/by-imdb/tt0111161/extra")]    // an extra segment
    [InlineData("/api/movies/by-imdb/tt1/../series/tt2")]  // an unencoded path-like value: the client collapses the dot-segments, so it never reaches this endpoint
    public async Task ARequestThatRoutingDoesNotSendToTheEndpoint_Is404WithNoBody_AndHasNoEffect(string path)
    {
        int before = await MovieCountAsync();

        HttpResponseMessage response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync()); // the framework's bare 404, not this endpoint's problem details
        AssertNothingWasCalledOut();
        Assert.Equal(before, await MovieCountAsync());
    }

    // ---- F: read-only ----

    [Fact]
    public async Task ALookup_DoesNotChangeTheMovie()
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Unavailable, nextAttemptAtUtc: SeededAtUtc.AddHours(6), imdbId: imdbId);
        object before = Snapshot(await LoadMovieAsync(_services, movieId));
        int countBefore = await MovieCountAsync();

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/movies/by-imdb/{imdbId}")).StatusCode);
        }

        Assert.Equal(before, Snapshot(await LoadMovieAsync(_services, movieId))); // state, timestamps (incl. the concurrency token), attempts, error - all identical
        Assert.Equal(countBefore, await MovieCountAsync());
        AssertNothingWasCalledOut();
    }

    // ---- G: a legacy Completed movie ----

    [Fact]
    public async Task ALegacyCompletedMovie_WithAnOldStrmRecord_IsDiscoverableByImdbId_AndItsStrmRecordIsUntouched()
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Completed, imdbId: imdbId, title: "Legacy Movie", year: 1994);
        const string legacyPath = "/stream/movies/Legacy Movie (1994) [imdbid-tt0000000]/Legacy Movie (1994).strm";
        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            context.Set<StrmFile>().Add(StrmFile.ForMovie(movieId, legacyPath, SeededAtUtc));
            await context.SaveChangesAsync();
        }

        MovieResponse? movie = await _client.GetFromJsonAsync<MovieResponse>($"/api/movies/by-imdb/{imdbId}");

        Assert.NotNull(movie);
        Assert.Equal(movieId, movie.Id);
        Assert.Equal("Completed", movie.Status);

        await using AsyncServiceScope check = _services.CreateAsyncScope();
        StrmFile strm = await check.ServiceProvider.GetRequiredService<CatalogDbContext>().Set<StrmFile>().AsNoTracking().SingleAsync(f => f.MovieId == movieId);
        Assert.Equal(legacyPath, strm.Path);
        AssertNothingWasCalledOut();
    }

    // ---- H: the literal route coexists with {id:guid} ----

    [Fact]
    public async Task TheByImdbRoute_AndTheGuidRoute_CoexistWithoutInterfering()
    {
        string imdbId = NextImdbId();
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending, imdbId: imdbId);

        HttpResponseMessage byId = await _client.GetAsync($"/api/movies/{movieId}");
        HttpResponseMessage byImdb = await _client.GetAsync($"/api/movies/by-imdb/{imdbId}");
        HttpResponseMessage unknownGuid = await _client.GetAsync($"/api/movies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byImdb.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownGuid.StatusCode); // GET by id keeps its own 404 contract
        Assert.Equal("Movies.NotFound", (await unknownGuid.Content.ReadFromJsonAsync<ProblemBody>())?.Title);

        // "by-imdb" is not a Guid, so it can never be taken for the id route: with no id it is simply a routing 404.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/movies/by-imdb")).StatusCode);
    }

    [Fact]
    public async Task OnlyGet_IsMappedOnTheByImdbRoute()
    {
        string imdbId = NextImdbId();
        await SeedMovieAsync(_services, MediaStatus.Pending, imdbId: imdbId);

        foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            using var request = new HttpRequestMessage(method, $"/api/movies/by-imdb/{imdbId}");
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.SendAsync(request)).StatusCode);
        }

        AssertNothingWasCalledOut();
    }

    // ---- I: validation parity with AddMovie, over HTTP ----

    [Theory]
    [InlineData("tt0111161", true)]
    [InlineData("tt1", true)]
    [InlineData("tt123456789012345", true)]   // 15 digits: the longest accepted
    [InlineData("tt", false)]
    [InlineData("TT0111161", false)]
    [InlineData("ttabc", false)]
    [InlineData("tt1234567890123456", false)] // 16 digits
    [InlineData("0111161", false)]
    [InlineData("tt0111161%20", false)]
    [InlineData("tt1%2F..%2Fseries%2Ftt2", false)]
    public async Task ByImdbAndAddMovie_AcceptAndRejectTheSameForms(string imdbId, bool valid)
    {
        HttpResponseMessage lookup = await _client.GetAsync($"/api/movies/by-imdb/{imdbId}");
        HttpResponseMessage add = await _client.PostAsJsonAsync("/api/movies", new { ImdbId = Uri.UnescapeDataString(imdbId) });

        // A form the rule accepts is never a 400 (here: 404, since no such movie exists and the fake metadata provider knows none).
        Assert.Equal(valid, lookup.StatusCode != HttpStatusCode.BadRequest);
        Assert.Equal(valid, add.StatusCode != HttpStatusCode.BadRequest);
    }

    private sealed record CreatedResponse(Guid Id);

    private sealed record ProblemBody(string? Title, string? Detail);
}
