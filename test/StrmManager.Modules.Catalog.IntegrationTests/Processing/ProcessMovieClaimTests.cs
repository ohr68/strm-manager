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
/// The critical invariant (Phase 6.2b, unchanged by 6.2c): a Movie is claimed, and the claim
/// is PERSISTED through the concurrency-aware save, before ANY external provider call - and
/// a request that loses the claim race ends cleanly without ever calling the provider.
/// Races are forced deterministically (a hook between "movie loaded" and "claim saved"), and
/// a spying IUnitOfWork plus the fakes' own "external:*" events give one ordered log of what
/// happened, in what order.
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
            MovieHandler = _ =>
            {
                _events.Enqueue("external:stream-provider");
                return Result.Success<IReadOnlyList<StreamCandidate>>([]); // "no candidates" - the run ends Unavailable
            },
        };
        var mediaValidator = new FakeMediaValidator
        {
            Handler = (_, _) =>
            {
                _events.Enqueue("external:media-validator");
                return MediaValidationResult.ForRejection(SourceAttemptResult.Rejected, "unused: the provider returns no candidates");
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

    // --- The claim is persisted first, then (and only then) the provider is called ---

    [Fact]
    public async Task ProcessMovie_Claim_IsSavedOnceThroughTheConcurrencyAwareSave_BeforeTheFirstExternalCall()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // SAVE #1 is the concurrency-aware claim: at that moment the aggregate was already
        // Searching in memory while the database still said Pending, and it succeeded.
        // Only then does the provider run, and the outcome (no candidates -> Unavailable)
        // is persisted by a separate, later plain save - the claim and the outcome are never
        // one transaction. The validator is never reached (nothing to validate).
        Assert.Equal(
            [
                "claim-save:in-memory=Searching",
                "claim-save:persisted-before=Pending",
                "claim-save:result=True",
                "external:stream-provider",
                "plain-save",
            ],
            Events);

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Unavailable, persisted.Status);
    }

    // --- The race loser ends cleanly, with ZERO provider calls ---

    [Fact]
    public async Task ProcessMovie_ClaimLostToAConcurrentClaimant_Returns409AlreadyBeingProcessed_AndNeverCallsTheProvider()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);
        DateTime competitorClaimedAtUtc = Now.AddSeconds(5);

        // Between this request loading the Pending movie and saving its claim, another
        // request claims and commits it.
        _loadHook.AfterLoadAsync = async _ =>
        {
            await using AsyncServiceScope competitorScope = _services.CreateAsyncScope();
            Movie competitorsCopy = await competitorScope.ServiceProvider
                .GetRequiredService<CatalogDbContext>().Set<Movie>().SingleAsync(m => m.Id == movieId);
            Assert.True(competitorsCopy.StartSearching(competitorClaimedAtUtc).IsSuccess);
            await competitorScope.ServiceProvider.GetRequiredService<CatalogDbContext>().SaveChangesAsync();
        };

        HttpResponseMessage response = await ProcessAsync(movieId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemResponse? problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.AlreadyBeingProcessed", problem?.Title);

        // The loser's claim save failed and was NOT retried (one attempt, no plain save), and
        // neither the provider nor the validator was ever reached.
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

    // --- Two concurrent attempts cannot both acquire the movie ---

    [Fact]
    public async Task ProcessMovie_TwoRequestsBothHoldingTheSamePendingMovie_ExactlyOneClaimsItAndOnlyItCallsTheProvider()
    {
        Guid movieId = await SeedMovieAsync(_services, MediaStatus.Pending);

        // Both requests must have loaded the Pending movie before either saves, so this is
        // a genuine race for one claim - not two sequential requests.
        _loadHook.AfterLoadAsync = MovieLoadHook.Barrier(participants: 2);

        HttpResponseMessage[] responses = await Task.WhenAll(ProcessAsync(movieId), ProcessAsync(movieId));

        HttpResponseMessage winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        HttpResponseMessage loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        ProblemResponse? problem = await loser.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("Movies.AlreadyBeingProcessed", problem?.Title);

        ProcessMovieResponse? winnerBody = await winner.Content.ReadFromJsonAsync<ProcessMovieResponse>();
        Assert.Equal("Unavailable", winnerBody?.Status); // the winner ran the whole (provider -> no candidates) flow

        // Two claim saves were attempted; exactly one succeeded - and the provider was
        // called exactly once, by the winner, after its claim save.
        string[] events = Events;
        Assert.Equal(1, events.Count(e => e == "claim-save:result=True"));
        Assert.Equal(1, events.Count(e => e == "claim-save:result=False"));
        Assert.Equal(1, events.Count(e => e == "external:stream-provider"));
        Assert.True(
            Array.IndexOf(events, "claim-save:result=True") < Array.IndexOf(events, "external:stream-provider"),
            "the provider must only be called after the claim save succeeded");

        Movie persisted = await LoadMovieAsync(_services, movieId);
        Assert.Equal(MediaStatus.Unavailable, persisted.Status);
        Assert.Equal(1, persisted.AttemptCount); // one outcome, recorded once
        await AssertNothingProducedAsync(movieId);
    }

    private async Task AssertNothingProducedAsync(Guid movieId)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        Assert.Empty(await context.Set<SourceAttempt>().Where(a => a.MovieId == movieId).ToListAsync());
        Assert.Empty(await context.Set<StrmFile>().Where(f => f.MovieId == movieId).ToListAsync());
    }
}
