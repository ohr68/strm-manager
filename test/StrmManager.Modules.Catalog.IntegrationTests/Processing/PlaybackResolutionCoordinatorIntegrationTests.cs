using Microsoft.Extensions.DependencyInjection;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Playback;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.MovieCandidates;
using static StrmManager.Modules.Catalog.IntegrationTests.Processing.ProcessMovieTestSupport;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

/// <summary>
/// The coordinator as the app composes it: the singleton registered by CatalogModule, running the real scoped
/// PlaybackResolver in scopes of its own over the real SQLite database, with the real .strm writer present and the
/// spying unit of work registered. What it shows that the unit tests cannot: the registration's lifetimes actually
/// work (a singleton driving scoped services), and one shared resolution still persists nothing.
/// </summary>
public class PlaybackResolutionCoordinatorIntegrationTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ProcessMovieHost _host = new(factory);

    private static Result<IReadOnlyList<StreamCandidate>> Found(params StreamCandidate[] candidates) =>
        Result.Success<IReadOnlyList<StreamCandidate>>(candidates);

    private static object Snapshot(Movie m) =>
        (m.Id, m.ExternalIds, m.Title, m.Year, m.Runtime, m.ReleaseAtUtc, m.Status, m.LastAttemptAtUtc, m.NextAttemptAtUtc, m.AttemptCount, m.LastError, m.CreatedAtUtc, m.UpdatedAtUtc);

    private int Count(string @event) => _host.Events.Count(e => e == @event);

    private void AssertNothingPersisted() =>
        Assert.DoesNotContain(_host.Events, e => e is "plain-save" || e.StartsWith("claim-save", StringComparison.Ordinal) || e == "external:strm-writer");

    private void AssertNoUrlInLogs()
    {
        foreach (string line in _host.Logs.Lines)
        {
            Assert.DoesNotContain("media.example.test", line, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretMarker, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCoordinator_IsOneSingleton_ForEveryRequestScope()
    {
        IPlaybackResolutionCoordinator fromRoot = _host.Services.GetRequiredService<IPlaybackResolutionCoordinator>();

        using IServiceScope requestA = _host.Services.CreateScope();
        using IServiceScope requestB = _host.Services.CreateScope();

        Assert.Same(fromRoot, requestA.ServiceProvider.GetRequiredService<IPlaybackResolutionCoordinator>());
        Assert.Same(fromRoot, requestB.ServiceProvider.GetRequiredService<IPlaybackResolutionCoordinator>());
    }

    [Fact]
    public async Task ConcurrentCallers_ShareOneRealResolution_AndPersistNothing()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Completed);
        object before = Snapshot(await LoadMovieAsync(_host.Services, movieId));
        IPlaybackResolutionCoordinator coordinator = _host.Services.GetRequiredService<IPlaybackResolutionCoordinator>();

        // Hold the (real) resolver inside the provider call until every caller has joined.
        using var release = new ManualResetEventSlim(false);
        _host.Provider = _ =>
        {
            release.Wait(TimeSpan.FromSeconds(10));
            return Found(Candidate("Source A"));
        };
        _host.Validate = _ => Approval;

        Task<PlaybackCoordinationResult>[] callers = Enumerable.Range(0, 6).Select(_ => coordinator.ResolveAsync(movieId, CancellationToken.None)).ToArray();
        release.Set();
        PlaybackCoordinationResult[] results = await Task.WhenAll(callers);

        Assert.Equal(1, Count("external:stream-provider")); // six callers, one provider lookup
        Assert.Equal(1, Count("external:media-validator")); // and one ffprobe
        Assert.All(results, r => Assert.Same(results[0], r));
        var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(Assert.IsType<PlaybackCoordinationResult.Completed>(results[0]).Resolution);
        Assert.Equal(UrlFor("Source A"), resolved.Location.Reveal());

        AssertNothingPersisted();
        Assert.Equal(before, Snapshot(await LoadMovieAsync(_host.Services, movieId)));
        Assert.Empty(await _host.AttemptsAsync(movieId));
        AssertNoUrlInLogs();
    }

    [Fact]
    public async Task ALaterCall_StartsAFreshRealResolution()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Completed);
        IPlaybackResolutionCoordinator coordinator = _host.Services.GetRequiredService<IPlaybackResolutionCoordinator>();
        _host.Provider = _ => Found(Candidate("Source A"));
        _host.Validate = _ => Approval;

        await coordinator.ResolveAsync(movieId, CancellationToken.None);
        await coordinator.ResolveAsync(movieId, CancellationToken.None);

        Assert.Equal(2, Count("external:stream-provider")); // nothing was cached between the two
        Assert.Equal(2, Count("external:media-validator"));
        AssertNothingPersisted();
    }

    [Fact]
    public async Task ANotCompletedMovie_IsSharedAsNotFound_WithoutTouchingTheProvider()
    {
        Guid movieId = await SeedMovieAsync(_host.Services, MediaStatus.Pending);
        IPlaybackResolutionCoordinator coordinator = _host.Services.GetRequiredService<IPlaybackResolutionCoordinator>();
        _host.Provider = _ => Found(Candidate("Source A"));

        PlaybackCoordinationResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => coordinator.ResolveAsync(movieId, CancellationToken.None)));

        Assert.All(results, r => Assert.IsType<PlaybackResolutionResult.NotFound>(Assert.IsType<PlaybackCoordinationResult.Completed>(r).Resolution));
        Assert.Equal(0, Count("external:stream-provider"));
    }
}
