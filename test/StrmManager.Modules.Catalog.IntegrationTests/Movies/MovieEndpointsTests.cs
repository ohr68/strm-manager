using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Metadata;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

namespace StrmManager.Modules.Catalog.IntegrationTests.Movies;

/// <summary>
/// Phase 6.1: adding and querying a Movie by a known IMDb id through the real API.
/// Metadata always comes from FakeMetadataProvider (never live Cinemeta) and "now" from
/// FakeTimeProvider (never the wall clock), so Scheduled/Pending is deterministic.
/// Every test uses its own imdb id - the class shares one database.
/// </summary>
public class MovieEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly DateTime UtcNow = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly HttpClient _client;
    private readonly FakeMetadataProvider _fakeMetadataProvider = new();
    private readonly IServiceProvider _services;

    public MovieEndpointsTests(ApiWebApplicationFactory factory)
    {
        var fakeTimeProvider = new FakeTimeProvider(UtcNow);

        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(fakeTimeProvider);
                services.AddSingleton<IMetadataProvider>(_fakeMetadataProvider);
            }));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    private static MovieMetadata Metadata(
        string imdbId,
        string title = "Canonical Title",
        int year = 2025,
        TimeSpan? runtime = null,
        DateTime? releaseAtUtc = null) =>
        new(new ExternalIds(imdbId, null, null), title, year, runtime ?? TimeSpan.FromMinutes(101), releaseAtUtc);

    private Task<HttpResponseMessage> PostMovieAsync(string imdbId) =>
        _client.PostAsJsonAsync("/api/movies", new { ImdbId = imdbId });

    private async Task<Guid> AddMovieAsync(string imdbId)
    {
        HttpResponseMessage response = await PostMovieAsync(imdbId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CreatedResponse? created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(created);
        return created.Id;
    }

    // --- API contract ---

    [Fact]
    public async Task PostMovie_WithKnownImdbId_ReturnsCreatedWithLocationAndId()
    {
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, releaseAtUtc: UtcNow.AddYears(-1));

        HttpResponseMessage response = await PostMovieAsync("tt0060001");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CreatedResponse? created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal($"/api/movies/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetMovie_AfterAdd_ReturnsTheCanonicalPersistedFields()
    {
        DateTime releaseAtUtc = new(2024, 6, 14, 0, 0, 0, DateTimeKind.Utc);
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, "Canonical Movie", 2024, TimeSpan.FromMinutes(142), releaseAtUtc);
        Guid id = await AddMovieAsync("tt0060002");

        HttpResponseMessage response = await _client.GetAsync($"/api/movies/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MovieResponse? movie = await response.Content.ReadFromJsonAsync<MovieResponse>();
        Assert.NotNull(movie);
        Assert.Equal(id, movie.Id);
        Assert.Equal("tt0060002", movie.ImdbId);
        Assert.Equal("Canonical Movie", movie.Title);
        Assert.Equal(2024, movie.Year);
        Assert.Equal(TimeSpan.FromMinutes(142), movie.Runtime);
        Assert.Equal(releaseAtUtc, movie.ReleaseAtUtc);
        Assert.Equal("Pending", movie.Status);
        Assert.Equal(UtcNow, movie.CreatedAtUtc);
    }

    [Fact]
    public async Task GetMovie_WhenNotFound_Returns404()
    {
        HttpResponseMessage response = await _client.GetAsync($"/api/movies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Movies.NotFound", problem?.Title);
    }

    // --- Canonical metadata vs caller input ---

    [Fact]
    public async Task PostMovie_CallerSuppliedTitleAndYear_AreIgnoredInFavorOfProviderMetadata()
    {
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, "Provider Title", 2019, releaseAtUtc: UtcNow.AddYears(-6));

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/api/movies", new { ImdbId = "tt0060003", Title = "Caller Title", Year = 1999 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CreatedResponse? created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        MovieResponse? movie = await (await _client.GetAsync($"/api/movies/{created!.Id}")).Content.ReadFromJsonAsync<MovieResponse>();
        Assert.Equal("Provider Title", movie!.Title);
        Assert.Equal(2019, movie.Year);
    }

    [Fact]
    public async Task PostMovie_ProviderReturnsADifferentCanonicalId_PersistsTheCanonicalId()
    {
        _fakeMetadataProvider.MovieHandler = _ => Metadata("tt0060104", releaseAtUtc: UtcNow.AddYears(-1));

        Guid id = await AddMovieAsync("tt0060004");

        MovieResponse? movie = await (await _client.GetAsync($"/api/movies/{id}")).Content.ReadFromJsonAsync<MovieResponse>();
        Assert.Equal("tt0060104", movie!.ImdbId);
    }

    // --- Duplicates ---

    [Fact]
    public async Task PostMovie_DuplicateImdbId_IsRejectedWithConflict_WithoutCallingTheProviderAgain()
    {
        int providerCalls = 0;
        _fakeMetadataProvider.MovieHandler = id =>
        {
            providerCalls++;
            return Metadata(id, releaseAtUtc: UtcNow.AddYears(-1));
        };
        await AddMovieAsync("tt0060005");

        HttpResponseMessage second = await PostMovieAsync("tt0060005");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        ProblemBody? problem = await second.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Movies.AlreadyExists", problem?.Title);
        Assert.Equal(1, providerCalls);
        Assert.Equal(1, await CountMoviesAsync("tt0060005"));
    }

    [Fact]
    public async Task PostMovie_DifferentRequestedIdResolvingToAnExistingCanonicalId_IsRejectedWithConflict()
    {
        _fakeMetadataProvider.MovieHandler = _ => Metadata("tt0060006", releaseAtUtc: UtcNow.AddYears(-1));
        await AddMovieAsync("tt0060006");

        // A different id that the provider canonicalizes to the movie we already have.
        HttpResponseMessage second = await PostMovieAsync("tt0060106");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountMoviesAsync("tt0060006"));
    }

    // --- Release date / scheduling ---

    [Fact]
    public async Task PostMovie_MissingReleaseDate_IsRejectedAndNothingIsPersisted()
    {
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, releaseAtUtc: null);

        HttpResponseMessage response = await PostMovieAsync("tt0060007");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Movies.ReleaseDateUnavailable", problem?.Title);
        Assert.Contains("release date", problem?.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountMoviesAsync("tt0060007"));
    }

    [Fact]
    public async Task PostMovie_AlreadyReleased_BecomesPending()
    {
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, releaseAtUtc: UtcNow.AddDays(-1));

        Guid id = await AddMovieAsync("tt0060008");

        Assert.Equal(MediaStatus.Pending, (await LoadMovieAsync(id)).Status);
    }

    [Fact]
    public async Task PostMovie_ReleasedExactlyNow_BecomesPending()
    {
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, releaseAtUtc: UtcNow);

        Guid id = await AddMovieAsync("tt0060009");

        Assert.Equal(MediaStatus.Pending, (await LoadMovieAsync(id)).Status);
    }

    [Fact]
    public async Task PostMovie_FutureRelease_RemainsScheduled()
    {
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, releaseAtUtc: UtcNow.AddSeconds(1));

        Guid id = await AddMovieAsync("tt0060010");

        Assert.Equal(MediaStatus.Scheduled, (await LoadMovieAsync(id)).Status);
    }

    // --- Persistence ---

    [Fact]
    public async Task AddedMovie_IsPersistedAndReadableFromAFreshDbContext()
    {
        DateTime releaseAtUtc = new(2023, 3, 3, 3, 3, 3, DateTimeKind.Utc);
        _fakeMetadataProvider.MovieHandler = id => Metadata(id, "Persisted", 2023, TimeSpan.FromMinutes(88), releaseAtUtc);
        Guid id = await AddMovieAsync("tt0060011");

        Movie movie = await LoadMovieAsync(id); // brand-new scope => brand-new DbContext

        Assert.Equal("tt0060011", movie.ExternalIds.ImdbId);
        Assert.Equal("Persisted", movie.Title);
        Assert.Equal(2023, movie.Year);
        Assert.Equal(TimeSpan.FromMinutes(88), movie.Runtime);
        Assert.Equal(releaseAtUtc, DateTime.SpecifyKind(movie.ReleaseAtUtc, DateTimeKind.Utc));
        Assert.Equal(MediaStatus.Pending, movie.Status);
        Assert.Equal(UtcNow, DateTime.SpecifyKind(movie.CreatedAtUtc, DateTimeKind.Utc));
        Assert.Equal(0, movie.AttemptCount);
    }

    // --- Error mapping ---

    [Fact]
    public async Task PostMovie_MetadataNotFound_Returns404WithProviderErrorCode()
    {
        _fakeMetadataProvider.MovieHandler = id => Result.Failure<MovieMetadata>(MetadataProviderErrors.MovieNotFound("Fake", id));

        HttpResponseMessage response = await PostMovieAsync("tt0060012");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("Metadata.MovieNotFound", problem?.Title);
        Assert.Equal(0, await CountMoviesAsync("tt0060012"));
    }

    [Theory]
    [InlineData("Metadata.InvalidResponse")]
    [InlineData("Metadata.ProviderUnavailable")]
    [InlineData("Metadata.Timeout")]
    public async Task PostMovie_ProviderFailure_IsNotSwallowed_AndNothingIsPersisted(string errorCode)
    {
        _fakeMetadataProvider.MovieHandler = _ => Result.Failure<MovieMetadata>(Error.Failure(errorCode, "simulated"));

        HttpResponseMessage response = await PostMovieAsync("tt0060013");

        // Unlike AddSeries (which can fall back to caller-supplied data), a movie has
        // nothing to be created from without metadata - so the failure is surfaced.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal(errorCode, problem?.Title);
        Assert.Equal(0, await CountMoviesAsync("tt0060013"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-imdb-id")]
    [InlineData("tt")]
    [InlineData("tt1/../series/tt2")]
    public async Task PostMovie_InvalidImdbId_ReturnsValidationProblem_WithoutCallingTheProvider(string imdbId)
    {
        int providerCalls = 0;
        _fakeMetadataProvider.MovieHandler = id =>
        {
            providerCalls++;
            return Metadata(id, releaseAtUtc: UtcNow.AddYears(-1));
        };

        HttpResponseMessage response = await PostMovieAsync(imdbId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, providerCalls);
    }

    private async Task<Movie> LoadMovieAsync(Guid id)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await context.Set<Movie>().AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private async Task<int> CountMoviesAsync(string imdbId)
    {
        using IServiceScope scope = _services.CreateScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await context.Set<Movie>().CountAsync(m => m.ExternalIds.ImdbId == imdbId);
    }

    private sealed record CreatedResponse(Guid Id);

    private sealed record ProblemBody(string? Title, string? Detail, int? Status);

    private sealed record MovieResponse(
        Guid Id,
        string? ImdbId,
        string? TmdbId,
        string? TvdbId,
        string Title,
        int Year,
        TimeSpan? Runtime,
        DateTime ReleaseAtUtc,
        string Status,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
