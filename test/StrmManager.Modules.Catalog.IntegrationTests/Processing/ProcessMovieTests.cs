using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Abstractions.Data;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Domain.SourceAttempts;
using StrmManager.Modules.Catalog.Domain.StrmFiles;
using StrmManager.Modules.Catalog.Infrastructure.Database;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Application.Validation;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// Phase 6.2b: POST /api/movies/{id}/process claims an eligible Movie (Pending ->
/// Searching, persisted through the concurrency-aware save) and stops there - provider
/// discovery, validation and STRM generation are later phases. Every test also proves the
/// stream provider and media validator were never touched. "Now" is a FakeTimeProvider and
/// every movie is seeded before it, so claims visibly move the concurrency token.
/// </summary>
public class ProcessMovieTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private int _externalCalls;

    public ProcessMovieTests(ApiWebApplicationFactory factory)
    {
        var streamProvider = new FakeStreamProvider
        {
            Handler = _ =>
            {
                Interlocked.Increment(ref _externalCalls);
                return Result.Success<IReadOnlyList<StreamCandidate>>([]);
            },
        };
        var mediaValidator = new FakeMediaValidator
        {
            Handler = (_, _) =>
            {
                Interlocked.Increment(ref _externalCalls);
                return MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "must not be called in 6.2b");
            },
        };

        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
                services.AddSingleton<IStreamProvider>(streamProvider);
                services.AddSingleton<IMediaValidator>(mediaValidator);
            }));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    private Task<HttpResponseMessage> ProcessAsync(Guid movieId) =>
        _client.PostAsync($"/api/movies/{movieId}/process", content: null);

    private void AssertNoExternalCalls() => Assert.Equal(0, Volatile.Read(ref _externalCalls));

    private async Task AssertNothingProducedAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Assert.Empty(await context.Set<SourceAttempt>().Where(a => a.MovieId == movieId).ToListAsync());
        Assert.Empty(await context.Set<StrmFile>().Where(f => f.MovieId == movieId).ToListAsync());
    }

    // --- A. Claim ---

    [Fact]
    public async Task ProcessMovie_PendingMovie_IsClaimedAndPersistedAsSearching()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse? body = await response.Content.ReadFromJsonAsync<ProcessMovieResponse>();
        Assert.NotNull(body);
        Assert.Equal(movieId, body.MovieId);
        Assert.Equal("Searching", body.Status);
        Assert.Equal(0, body.Attempts); // a claim is not an attempt - nothing has been tried yet
        Assert.Null(body.StrmPath);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Searching, persisted.Status);
        Assert.Equal(Now, persisted.UpdatedAtUtc); // the concurrency token moved with the claim
        Assert.Equal(0, persisted.AttemptCount);
        Assert.Null(persisted.LastAttemptAtUtc);

        AssertNoExternalCalls();
        await AssertNothingProducedAsync(movieId);
    }

    [Fact]
    public async Task ProcessMovie_UnknownMovie_Returns404()
    {
        HttpResponseMessage response = await ProcessAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.NotFound", problem?.Title);
        AssertNoExternalCalls();
    }

    [Fact]
    public async Task ProcessMovie_ClaimedMovieProcessedAgain_IsRejectedAndNotClaimedTwice()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        Assert.Equal(HttpStatusCode.OK, (await ProcessAsync(movieId)).StatusCode);

        HttpResponseMessage second = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        ProblemResponse? problem = await second.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.AlreadyBeingProcessed", problem?.Title);
        AssertNoExternalCalls();
    }

    // --- E. Already processing ---

    [Theory]
    [InlineData(MediaStatus.Searching)]
    [InlineData(MediaStatus.Validating)]
    public async Task ProcessMovie_MovieAlreadyBeingProcessed_Returns409AlreadyBeingProcessedAndChangesNothing(MediaStatus status)
    {
        Guid movieId = await SeedMovieAsync(_services, status);

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.AlreadyBeingProcessed", problem?.Title);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(status, persisted.Status);
        Assert.Equal(SeededAtUtc, persisted.UpdatedAtUtc);
        AssertNoExternalCalls();
    }

    // --- F. Eligibility / retry scheduling ---

    [Fact]
    public async Task ProcessMovie_ScheduledMovieNotYetReleased_Returns409AndStaysScheduled()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Scheduled);

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movie.InvalidTransition", problem?.Title);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Scheduled, persisted.Status);
        Assert.Equal(SeededAtUtc, persisted.UpdatedAtUtc);
        AssertNoExternalCalls();
    }

    [Theory]
    [InlineData(MediaStatus.Unavailable, true)] // retry not due yet
    [InlineData(MediaStatus.Unavailable, false)] // retry already due - still not a manual "process"; Retry/the sweep owns it
    [InlineData(MediaStatus.Error, true)]
    [InlineData(MediaStatus.Error, false)]
    public async Task ProcessMovie_UnavailableOrErrorMovie_Returns409AndKeepsItsRetrySchedule(MediaStatus status, bool retryStillInTheFuture)
    {
        DateTime nextAttemptAtUtc = retryStillInTheFuture ? Now.AddHours(3) : Now.AddHours(-3);
        Guid movieId = await SeedMovieAsync(_services, status, nextAttemptAtUtc);

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movie.InvalidTransition", problem?.Title);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(status, persisted.Status);
        Assert.Equal(nextAttemptAtUtc, persisted.NextAttemptAtUtc); // the schedule is left exactly as it was
        Assert.Equal(1, persisted.AttemptCount);
        Assert.NotNull(persisted.LastError);
        Assert.Equal(SeededAtUtc, persisted.UpdatedAtUtc);
        AssertNoExternalCalls();
    }

    [Fact]
    public async Task ProcessMovie_UnavailableMovieAfterDomainRetry_IsClaimableAndKeepsItsAttemptHistory()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Unavailable, Now.AddHours(-1));

        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            Movie movie = (await scope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;
            Assert.True(movie.Retry(Now.AddMinutes(-5)).IsSuccess);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Searching, persisted.Status);
        Assert.Equal(1, persisted.AttemptCount); // the earlier attempt is history, not reset
        Assert.Null(persisted.NextAttemptAtUtc); // Retry cleared it; claiming does not resurrect it
        AssertNoExternalCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessMovie_CompletedMovie_IsIdempotentAndNotReclaimed(bool hasStrmFile)
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Completed);
        const string strmPath = "/data/movies/Process Movie Test (2025).strm";

        if (hasStrmFile)
        {
            await using AsyncServiceScope scope = _services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IStrmFileRepository>().Insert(StrmFile.ForMovie(movieId, strmPath, SeededAtUtc));
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
        }

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessMovieResponse? body = await response.Content.ReadFromJsonAsync<ProcessMovieResponse>();
        Assert.NotNull(body);
        Assert.Equal("Completed", body.Status);
        Assert.Equal(1, body.Attempts); // the movie's recorded attempt count, as Episode reports it
        Assert.Equal(hasStrmFile ? strmPath : null, body.StrmPath);
        Assert.NotNull(body.Reason);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Completed, persisted.Status);
        Assert.Equal(SeededAtUtc, persisted.UpdatedAtUtc);
        AssertNoExternalCalls();
    }

    // --- G. Interrupted processing ---

    [Theory]
    [InlineData(false)] // Searching
    [InlineData(true)] // Validating
    public async Task ProcessMovie_InterruptedMovieStaysBlockedUntilRecovered_ThenIsClaimableAgain(bool interruptedWhileValidating)
    {
        // A crash after the persisted claim leaves the row Searching/Validating. The
        // endpoint must refuse it (no silent re-claim of possibly live work), and only the
        // domain's explicit recovery - what 6.3's stale sweep will call - reopens it.
        Guid movieId = await SeedMovieAsync(_services, interruptedWhileValidating ? MediaStatus.Validating : MediaStatus.Searching);

        Assert.Equal(HttpStatusCode.Conflict, (await ProcessAsync(movieId)).StatusCode);

        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            Movie movie = (await scope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId))!;
            Assert.True(movie.RecoverInterruptedProcessing(Now.AddMinutes(-1)).IsSuccess);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        Movie recovered = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Pending, recovered.Status);
        Assert.Null(recovered.NextAttemptAtUtc);
        Assert.Equal(0, recovered.AttemptCount); // an interrupted run reached no outcome

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Movie reclaimed = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Searching, reclaimed.Status);
        Assert.Equal(Now, reclaimed.UpdatedAtUtc);
        AssertNoExternalCalls();
    }
}
