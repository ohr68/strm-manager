using Jellyfin.Plugin.StrmManager.Movies;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for EnsureMovieService - composition only, driven by a small hand-written fake
/// IStrmManagerClient (canned results per call, in order; no real HTTP, no mocking framework). P1/P2's own HTTP
/// status mappings are not retested here - StrmManagerClientTests.cs/AddMovieAsyncTests.cs already cover those.
/// </summary>
public sealed class EnsureMovieServiceTests
{
    private const string ImdbId = "tt0137523";
    private static readonly Guid MovieId = Guid.Parse("1393e862-e5f1-4fe6-b4a0-b3bb8ee0daa6");

    private static EnsureMovieService CreateService(FakeStrmManagerClient client) =>
        new(client, NullLogger<EnsureMovieService>.Instance);

    [Fact]
    public async Task ALookupThatFindsTheMovie_ReturnsExisting_AndNeverCallsAddMovie()
    {
        var client = new FakeStrmManagerClient(lookups: [new MovieLookupResult.Found(MovieId, ImdbId, "Fight Club", 1999, "Completed")]);
        EnsureMovieService service = CreateService(client);

        EnsureMovieResult result = await service.EnsureMovieAsync(ImdbId, CancellationToken.None);

        var existing = Assert.IsType<EnsureMovieResult.Existing>(result);
        Assert.Equal(MovieId, existing.MovieId);
        Assert.Equal("Completed", existing.Status);
        Assert.Equal(0, client.AddMovieCallCount);
    }

    [Fact]
    public async Task ALookupThatFindsNothing_ThenAnAddThatCreates_ReturnsCreated()
    {
        var client = new FakeStrmManagerClient(
            lookups: [new MovieLookupResult.NotFound()],
            adds: [new AddMovieResult.Created(MovieId)]);
        EnsureMovieService service = CreateService(client);

        EnsureMovieResult result = await service.EnsureMovieAsync(ImdbId, CancellationToken.None);

        var created = Assert.IsType<EnsureMovieResult.Created>(result);
        Assert.Equal(MovieId, created.MovieId);
        Assert.Equal(1, client.GetByImdbIdCallCount);
        Assert.Equal(1, client.AddMovieCallCount);
    }

    [Fact]
    public async Task TheDocumentedRace_NotFound_ThenAddConflict_ThenLookupFinds_ReturnsExisting()
    {
        var client = new FakeStrmManagerClient(
            lookups: [new MovieLookupResult.NotFound(), new MovieLookupResult.Found(MovieId, ImdbId, "Fight Club", 1999, "Pending")],
            adds: [new AddMovieResult.AlreadyExists()]);
        EnsureMovieService service = CreateService(client);

        EnsureMovieResult result = await service.EnsureMovieAsync(ImdbId, CancellationToken.None);

        var existing = Assert.IsType<EnsureMovieResult.Existing>(result);
        Assert.Equal(MovieId, existing.MovieId);
        Assert.Equal("Pending", existing.Status);
        Assert.Equal(2, client.GetByImdbIdCallCount);   // exactly one race-recovery lookup - not a loop
        Assert.Equal(1, client.AddMovieCallCount);
    }

    [Fact]
    public async Task ALookupFailure_IsMapped_AndAddMovieIsNeverCalled()
    {
        var client = new FakeStrmManagerClient(lookups: [new MovieLookupResult.Unreachable("Connection refused")]);
        EnsureMovieService service = CreateService(client);

        EnsureMovieResult result = await service.EnsureMovieAsync(ImdbId, CancellationToken.None);

        var unreachable = Assert.IsType<EnsureMovieResult.Unreachable>(result);
        Assert.Equal("Connection refused", unreachable.Reason);
        Assert.Equal(0, client.AddMovieCallCount);
    }

    [Fact]
    public async Task AnAddFailure_IsMapped()
    {
        var client = new FakeStrmManagerClient(
            lookups: [new MovieLookupResult.NotFound()],
            adds: [new AddMovieResult.Invalid()]);
        EnsureMovieService service = CreateService(client);

        EnsureMovieResult result = await service.EnsureMovieAsync(ImdbId, CancellationToken.None);

        Assert.IsType<EnsureMovieResult.Invalid>(result);
    }

    [Fact]
    public async Task ACallerCancellation_PropagatesAsOperationCanceledException_NotAsAResult()
    {
        var client = new FakeStrmManagerClient(lookups: [new MovieLookupResult.Found(MovieId, ImdbId, "Fight Club", 1999, "Completed")], throwOnCancellation: true);
        EnsureMovieService service = CreateService(client);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnsureMovieAsync(ImdbId, cts.Token));
    }

    /// <summary>
    /// Canned results per call, in order - not a mocking framework. Optionally honors an already-cancelled token the
    /// way the real StrmManagerClient does, so the cancellation test can prove EnsureMovieService does not swallow it.
    /// </summary>
    private sealed class FakeStrmManagerClient(
        IEnumerable<MovieLookupResult> lookups,
        IEnumerable<AddMovieResult>? adds = null,
        bool throwOnCancellation = false) : IStrmManagerClient
    {
        private readonly Queue<MovieLookupResult> _lookups = new(lookups);
        private readonly Queue<AddMovieResult> _adds = new(adds ?? []);

        public int GetByImdbIdCallCount { get; private set; }

        public int AddMovieCallCount { get; private set; }

        public Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken)
        {
            if (throwOnCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            GetByImdbIdCallCount++;
            return Task.FromResult(_lookups.Dequeue());
        }

        public Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken)
        {
            if (throwOnCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            AddMovieCallCount++;
            return Task.FromResult(_adds.Dequeue());
        }
    }
}
