using System.Collections.Concurrent;
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
/// Phase 6.2b's critical invariant: a Movie is claimed, and the claim is PERSISTED through
/// the concurrency-aware save, before any external work can happen - and a request that
/// loses the claim race ends cleanly without going on. Races are forced deterministically
/// (a hook between "movie loaded" and "claim saved"), and a spying IUnitOfWork records
/// exactly what the handler saved and what the database held at that moment.
/// </summary>
public class ProcessMovieClaimTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly IServiceProvider _services;
    private readonly MovieLoadHook _loadHook = new();

    /// <summary>Ordered, thread-safe log of persistence and external-call events.</summary>
    private readonly ConcurrentQueue<string> _events = new();

    public ProcessMovieClaimTests(ApiWebApplicationFactory factory)
    {
        var streamProvider = new FakeStreamProvider
        {
            Handler = _ =>
            {
                _events.Enqueue("external:stream-provider");
                return Result.Success<IReadOnlyList<StreamCandidate>>([]);
            },
        };
        var mediaValidator = new FakeMediaValidator
        {
            Handler = (_, _) =>
            {
                _events.Enqueue("external:media-validator");
                return MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "must not be called in 6.2b");
            },
        };

        WebApplicationFactory<Program> isolatedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
                services.AddSingleton<IStreamProvider>(streamProvider);
                services.AddSingleton<IMediaValidator>(mediaValidator);

                ServiceDescriptor originalRepository = services.Single(d => d.ServiceType == typeof(IMovieRepository));
                services.Remove(originalRepository);
                services.AddScoped<IMovieRepository>(sp => new InterceptingMovieRepository(
                    (IMovieRepository)ActivatorUtilities.CreateInstance(sp, originalRepository.ImplementationType!),
                    _loadHook));

                services.AddScoped<IUnitOfWork>(sp => new SpyUnitOfWork(sp.GetRequiredService<CatalogDbContext>(), _events));
            }));

        _client = isolatedFactory.CreateClient();
        _services = isolatedFactory.Services;
    }

    private Task<HttpResponseMessage> ProcessAsync(Guid movieId) =>
        _client.PostAsync($"/api/movies/{movieId}/process", content: null);

    private string[] Events => _events.ToArray();

    // --- B. The claim is persisted first, and nothing else is ---

    [Fact]
    public async Task ProcessMovie_Claim_IsSavedOnceThroughTheConcurrencyAwareSave_BeforeAnythingElseHappens()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Exactly one save, and it is the concurrency-aware claim save: at that moment the
        // aggregate was already Searching in memory while the database still said Pending -
        // so persisting it is what makes the claim real, and it succeeded. No plain
        // SaveChanges, and (6.2b) no provider or validator call at any point.
        Assert.Equal(
            ["claim-save:in-memory=Searching", "claim-save:persisted-before=Pending", "claim-save:result=True"],
            Events);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Searching, persisted.Status);
    }

    // --- D. The race loser ends cleanly ---

    [Fact]
    public async Task ProcessMovie_ClaimLostToAConcurrentClaimant_Returns409AlreadyBeingProcessed_AndDoesNotProceed()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        DateTime competitorClaimedAtUtc = Now.AddSeconds(5);

        // Between this request loading the Pending movie and saving its claim, another
        // request claims and commits it.
        _loadHook.AfterLoadAsync = async _ =>
        {
            await using AsyncServiceScope competitorScope = _services.CreateAsyncScope();
            Movie competitorsCopy = (await competitorScope.ServiceProvider
                .GetRequiredService<CatalogDbContext>().Set<Movie>().SingleAsync(m => m.Id == movieId));
            Assert.True(competitorsCopy.StartSearching(competitorClaimedAtUtc).IsSuccess);
            await competitorScope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
        };

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.AlreadyBeingProcessed", problem?.Title);

        // The loser's claim save failed, was NOT retried (one attempt, no plain save),
        // and nothing external ran afterwards.
        Assert.Equal(
            ["claim-save:in-memory=Searching", "claim-save:persisted-before=Searching", "claim-save:result=False"],
            Events);

        // The database holds the competitor's claim untouched, not the loser's.
        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Searching, persisted.Status);
        Assert.Equal(competitorClaimedAtUtc, persisted.UpdatedAtUtc);
        Assert.Equal(0, persisted.AttemptCount);
        await AssertNothingProducedAsync(movieId);
    }

    // --- C. Two concurrent attempts cannot both acquire the movie ---

    [Fact]
    public async Task ProcessMovie_TwoRequestsBothHoldingTheSamePendingMovie_ExactlyOneClaimsIt()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);

        // Both requests must have loaded the Pending movie before either saves, so this is
        // a genuine race for one claim - not two sequential requests.
        _loadHook.AfterLoadAsync = MovieLoadHook.Barrier(participants: 2);

        HttpResponseMessage[] responses = await Task.WhenAll(ProcessAsync(movieId), ProcessAsync(movieId));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        ProblemResponse? problem = await loser.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.AlreadyBeingProcessed", problem?.Title);

        HttpResponseMessage winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        ProcessMovieResponse? winnerBody = await winner.Content.ReadFromJsonAsync<ProcessMovieResponse>();
        Assert.Equal("Searching", winnerBody?.Status);

        // Two claim saves were attempted; exactly one succeeded. Nothing external ran.
        Assert.Equal(1, Events.Count(e => e == "claim-save:result=True"));
        Assert.Equal(1, Events.Count(e => e == "claim-save:result=False"));
        Assert.DoesNotContain(Events, e => e.StartsWith("external:", StringComparison.Ordinal));

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Searching, persisted.Status);
        Assert.Equal(Now, persisted.UpdatedAtUtc);
        Assert.Equal(0, persisted.AttemptCount);
        await AssertNothingProducedAsync(movieId);
    }

    private async Task AssertNothingProducedAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Assert.Empty(await context.Set<SourceAttempt>().Where(a => a.MovieId == movieId).ToListAsync());
        Assert.Empty(await context.Set<StrmFile>().Where(f => f.MovieId == movieId).ToListAsync());
    }

    /// <summary>
    /// Records what the handler does to persistence. On the concurrency-aware save it also
    /// captures the tracked Movie's in-memory status and - via a database read taken before
    /// the save is issued - the status the database still held at that instant.
    /// </summary>
    private sealed class SpyUnitOfWork(CatalogDbContext context, ConcurrentQueue<string> events) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            events.Enqueue("plain-save");
            return context.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entry = context.ChangeTracker.Entries<Movie>().Single();
            events.Enqueue($"claim-save:in-memory={entry.Entity.Status}");

            var persisted = await entry.GetDatabaseValuesAsync(cancellationToken);
            events.Enqueue($"claim-save:persisted-before={persisted!.GetValue<MediaStatus>(nameof(Movie.Status))}");

            bool saved = await context.TrySaveChangesAsync(cancellationToken);
            events.Enqueue($"claim-save:result={saved}");

            return saved;
        }
    }
}
