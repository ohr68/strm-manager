using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Application.Playback;
using static StrmManager.Modules.Catalog.UnitTests.Playback.CoordinatorHarness;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

public class EpisodePlaybackResolutionCoordinatorTests
{
    [Fact(Timeout = 10000)]
    public async Task ConcurrentCallersForOneEpisode_ShareExactlyOneResolution()
    {
        using var h = new EpisodeCoordinatorHarness();
        Guid episode = Guid.NewGuid();

        Task<PlaybackCoordinationResult>[] callers =
            Enumerable.Range(0, 8)
                .Select(_ => h.Coordinator.ResolveAsync(episode, default))
                .ToArray();

        await EventuallyAsync(
            () => h.Probe.Calls == 1,
            "the shared episode resolution to start");

        Assert.Equal(1, h.Coordinator.InFlightCount);

        PlaybackResolutionResult.Resolved resolved = Resolved("episode-shared");
        h.Probe.Complete(episode, resolved);

        PlaybackCoordinationResult[] results =
            await Task.WhenAll(callers);

        Assert.Equal(1, h.Probe.Calls);
        Assert.All(results, result => Assert.Same(results[0], result));

        Assert.Same(
            resolved,
            Assert.IsType<PlaybackCoordinationResult.Completed>(
                results[0]).Resolution);

        Assert.Equal(0, h.Coordinator.InFlightCount);
    }

    [Fact(Timeout = 10000)]
    public async Task ALaterCall_StartsAFreshEpisodeResolution()
    {
        using var h = new EpisodeCoordinatorHarness();
        Guid episode = Guid.NewGuid();
        int generation = 0;

        h.Probe.Behavior = (_, _) =>
            Task.FromResult<PlaybackResolutionResult>(
                Resolved(
                    $"episode-{Interlocked.Increment(ref generation)}"));

        PlaybackCoordinationResult first =
            await h.Coordinator.ResolveAsync(episode, default);

        PlaybackCoordinationResult second =
            await h.Coordinator.ResolveAsync(episode, default);

        Assert.Equal(2, h.Probe.Calls);
        Assert.Equal(2, h.Probe.InstancesCreated);
        Assert.NotEqual(first, second);

        var resolved =
            Assert.IsType<PlaybackResolutionResult.Resolved>(
                Assert.IsType<PlaybackCoordinationResult.Completed>(
                    second).Resolution);

        Assert.Equal(
            UrlFor("episode-2"),
            resolved.Location.Reveal());
    }

    [Fact(Timeout = 10000)]
    public async Task CancellingOneWaiter_DoesNotCancelTheSharedEpisodeResolution()
    {
        using var h = new EpisodeCoordinatorHarness();
        Guid episode = Guid.NewGuid();

        using var cancelled = new CancellationTokenSource();

        Task<PlaybackCoordinationResult> first =
            h.Coordinator.ResolveAsync(
                episode,
                cancelled.Token);

        Task<PlaybackCoordinationResult> second =
            h.Coordinator.ResolveAsync(
                episode,
                default);

        await EventuallyAsync(
            () => h.Probe.Calls == 1,
            "the shared episode resolution to start");

        CancellationToken resolverToken =
            h.Probe.TokenOfCallFor(episode);

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => first);

        Assert.False(resolverToken.IsCancellationRequested);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, h.Probe.Calls);

        h.Probe.Complete(
            episode,
            Resolved("episode-waiter"));

        Assert.IsType<PlaybackCoordinationResult.Completed>(
            await second);
    }

    [Fact(Timeout = 10000)]
    public async Task QueueTimeout_ReturnsBusyWithoutCallingEpisodeResolver()
    {
        using var h =
            new EpisodeCoordinatorHarness(
                maxConcurrent: 1);

        Guid running = Guid.NewGuid();
        Guid queued = Guid.NewGuid();

        Task<PlaybackCoordinationResult> holder =
            h.Coordinator.ResolveAsync(
                running,
                default);

        await EventuallyAsync(
            () => h.Probe.Calls == 1,
            "the episode capacity slot to be occupied");

        Task<PlaybackCoordinationResult> waiting =
            h.Coordinator.ResolveAsync(
                queued,
                default);

        await EventuallyAsync(
            () => h.Time.PendingTimers >= 2,
            "the episode queue timer to be armed");

        h.Time.Advance(h.QueueWait);

        Assert.IsType<PlaybackCoordinationResult.Busy>(
            await waiting);

        Assert.DoesNotContain(
            queued,
            h.Probe.EpisodesCalled);

        h.Probe.Complete(
            running,
            Resolved("running"));

        await holder;
    }

    [Fact(Timeout = 10000)]
    public async Task BudgetExpiry_CancelsEpisodeResolver_AndReturnsStableFailure()
    {
        using var h = new EpisodeCoordinatorHarness();
        Guid episode = Guid.NewGuid();

        Task<PlaybackCoordinationResult> call =
            h.Coordinator.ResolveAsync(
                episode,
                default);

        await EventuallyAsync(
            () => h.Probe.Calls == 1,
            "the episode resolution to start");

        CancellationToken resolverToken =
            h.Probe.TokenOfCallFor(episode);

        h.Time.Advance(h.Budget);

        PlaybackCoordinationResult result =
            await call;

        Assert.True(
            resolverToken.IsCancellationRequested);

        Assert.Equal(
            new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.BudgetExceeded),
            result);

        Assert.Equal(
            0,
            h.Coordinator.InFlightCount);
    }

    [Fact(Timeout = 10000)]
    public async Task Shutdown_CancelsEpisodeResolution()
    {
        using var h = new EpisodeCoordinatorHarness();
        Guid episode = Guid.NewGuid();

        Task<PlaybackCoordinationResult> call =
            h.Coordinator.ResolveAsync(
                episode,
                default);

        await EventuallyAsync(
            () => h.Probe.Calls == 1,
            "the episode resolution to start");

        CancellationToken resolverToken =
            h.Probe.TokenOfCallFor(episode);

        await h.Shutdown.CancelAsync();

        PlaybackCoordinationResult result =
            await call;

        Assert.True(
            resolverToken.IsCancellationRequested);

        Assert.Equal(
            new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.ShuttingDown),
            result);

        Assert.Equal(
            0,
            h.Coordinator.InFlightCount);
    }

    [Fact(Timeout = 10000)]
    public async Task SharedEpisodeResolution_OwnsOneScope_WhichIsDisposedAtCompletion()
    {
        using var h = new EpisodeCoordinatorHarness();
        Guid episode = Guid.NewGuid();

        Task<PlaybackCoordinationResult>[] callers =
            Enumerable.Range(0, 5)
                .Select(_ => h.Coordinator.ResolveAsync(
                    episode,
                    default))
                .ToArray();

        await EventuallyAsync(
            () => h.Probe.Calls == 1,
            "the episode resolver to start");

        EpisodeProbeResolver resolver =
            Assert.Single(h.Probe.Instances);

        Assert.NotSame(
            h.Root,
            resolver.ScopeProvider);

        Assert.False(
            resolver.Disposed);

        h.Probe.Complete(
            episode,
            Resolved("scope"));

        await Task.WhenAll(callers);

        Assert.Equal(
            1,
            h.Probe.InstancesCreated);

        Assert.True(
            resolver.Disposed);
    }

    [Fact(Timeout = 10000)]
    public async Task MovieAndEpisodeCoordinators_ShareTheSameGlobalCapacity()
    {
        var options =
            Options.Create(
                new PlaybackResolutionOptions
                {
                    MaxConcurrentResolutions = 1,
                    QueueWait = TimeSpan.FromSeconds(5),
                    ResolutionBudget = TimeSpan.FromSeconds(60),
                });

        using var capacity =
            new PlaybackResolutionCapacity(options);

        using var shutdown =
            new CancellationTokenSource();

        var time =
            new ManualTimeProvider();

        var movieProbe =
            new ResolverProbe();

        var episodeProbe =
            new EpisodeResolverProbe();

        using ServiceProvider root =
            new ServiceCollection()
                .AddScoped<IPlaybackResolver>(
                    scope => movieProbe.Create(scope))
                .AddScoped<IEpisodePlaybackResolver>(
                    scope => episodeProbe.Create(scope))
                .BuildServiceProvider(
                    new ServiceProviderOptions
                    {
                        ValidateScopes = true,
                        ValidateOnBuild = true,
                    });

        var movie =
            new PlaybackResolutionCoordinator(
                root.GetRequiredService<IServiceScopeFactory>(),
                options,
                capacity,
                time,
                new CapturingLogger<PlaybackResolutionCoordinator>(),
                shutdown.Token);

        var episode =
            new EpisodePlaybackResolutionCoordinator(
                root.GetRequiredService<IServiceScopeFactory>(),
                options,
                capacity,
                time,
                new CapturingLogger<EpisodePlaybackResolutionCoordinator>(),
                shutdown.Token);

        Guid movieId = Guid.NewGuid();
        Guid episodeId = Guid.NewGuid();

        Task<PlaybackCoordinationResult> movieCall =
            movie.ResolveAsync(
                movieId,
                default);

        await EventuallyAsync(
            () => movieProbe.Calls == 1,
            "the movie to occupy the shared capacity");

        Task<PlaybackCoordinationResult> episodeCall =
            episode.ResolveAsync(
                episodeId,
                default);

        await EventuallyAsync(
            () => time.PendingTimers >= 2,
            "the episode to queue behind the movie");

        Assert.Equal(
            1,
            movieProbe.Calls);

        Assert.Equal(
            0,
            episodeProbe.Calls);

        movieProbe.Complete(
            movieId,
            Resolved("movie"));

        await EventuallyAsync(
            () => episodeProbe.Calls == 1,
            "the episode to acquire the movie's released slot");

        episodeProbe.Complete(
            episodeId,
            Resolved("episode"));

        Assert.IsType<PlaybackCoordinationResult.Completed>(
            await movieCall);

        Assert.IsType<PlaybackCoordinationResult.Completed>(
            await episodeCall);

        Assert.Equal(
            1,
            movieProbe.MaxRunning);

        Assert.Equal(
            1,
            episodeProbe.MaxRunning);
    }
}

internal sealed class EpisodeResolverProbe
{
    private readonly ConcurrentDictionary<
        Guid,
        TaskCompletionSource<PlaybackResolutionResult>> _blocked =
        new();

    private int _calls;
    private int _running;
    private int _maxRunning;
    private int _instances;

    public EpisodeResolverProbe()
    {
        Behavior = BlockUntilCompleted;
    }

    public int Calls =>
        Volatile.Read(ref _calls);

    public int Running =>
        Volatile.Read(ref _running);

    public int MaxRunning =>
        Volatile.Read(ref _maxRunning);

    public int InstancesCreated =>
        Volatile.Read(ref _instances);

    public ConcurrentQueue<(
        Guid EpisodeId,
        CancellationToken Token)> Invocations { get; } =
        new();

    public ConcurrentQueue<EpisodeProbeResolver> Instances { get; } =
        new();

    public Func<
        Guid,
        CancellationToken,
        Task<PlaybackResolutionResult>> Behavior { get; set; }

    public IEnumerable<Guid> EpisodesCalled =>
        Invocations.Select(invocation => invocation.EpisodeId);

    public CancellationToken TokenOfCallFor(
        Guid episodeId) =>
        Invocations.Last(
            invocation =>
                invocation.EpisodeId == episodeId).Token;

    public Task<PlaybackResolutionResult> BlockUntilCompleted(
        Guid episodeId,
        CancellationToken token)
    {
        var gate =
            new TaskCompletionSource<PlaybackResolutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        _blocked[episodeId] = gate;

        return gate.Task.WaitAsync(token);
    }

    public void Complete(
        Guid episodeId,
        PlaybackResolutionResult result) =>
        _blocked[episodeId].SetResult(result);

    public EpisodeProbeResolver Create(
        IServiceProvider scopeProvider)
    {
        var resolver =
            new EpisodeProbeResolver(
                this,
                scopeProvider,
                Interlocked.Increment(ref _instances));

        Instances.Enqueue(resolver);

        return resolver;
    }

    internal async Task<PlaybackResolutionResult> RunAsync(
        Guid episodeId,
        CancellationToken token)
    {
        Interlocked.Increment(ref _calls);
        Invocations.Enqueue((episodeId, token));

        int now =
            Interlocked.Increment(ref _running);

        int seen;

        while (
            now > (seen = Volatile.Read(ref _maxRunning))
            && Interlocked.CompareExchange(
                ref _maxRunning,
                now,
                seen) != seen)
        {
        }

        try
        {
            return await Behavior(
                episodeId,
                token);
        }
        finally
        {
            Interlocked.Decrement(
                ref _running);
        }
    }
}

internal sealed class EpisodeProbeResolver(
    EpisodeResolverProbe probe,
    IServiceProvider scopeProvider,
    int id)
    : IEpisodePlaybackResolver,
      IDisposable
{
    public int Id { get; } = id;

    public IServiceProvider ScopeProvider { get; } =
        scopeProvider;

    public bool Disposed { get; private set; }

    public Task<PlaybackResolutionResult> ResolveAsync(
        Guid episodeId,
        CancellationToken cancellationToken) =>
        probe.RunAsync(
            episodeId,
            cancellationToken);

    public void Dispose() =>
        Disposed = true;
}

internal sealed class EpisodeCoordinatorHarness : IDisposable
{
    public EpisodeCoordinatorHarness(
        int maxConcurrent = 2,
        TimeSpan? queueWait = null,
        TimeSpan? budget = null)
    {
        QueueWait =
            queueWait ?? TimeSpan.FromSeconds(5);

        Budget =
            budget ?? TimeSpan.FromSeconds(60);

        Root =
            new ServiceCollection()
                .AddScoped<IEpisodePlaybackResolver>(
                    scope => Probe.Create(scope))
                .BuildServiceProvider(
                    new ServiceProviderOptions
                    {
                        ValidateScopes = true,
                        ValidateOnBuild = true,
                    });

        var options =
            Options.Create(
                new PlaybackResolutionOptions
                {
                    MaxConcurrentResolutions = maxConcurrent,
                    QueueWait = QueueWait,
                    ResolutionBudget = Budget,
                });

        Capacity =
            new PlaybackResolutionCapacity(
                options);

        Coordinator =
            new EpisodePlaybackResolutionCoordinator(
                Root.GetRequiredService<IServiceScopeFactory>(),
                options,
                Capacity,
                Time,
                Logger,
                Shutdown.Token);
    }

    public TimeSpan QueueWait { get; }

    public TimeSpan Budget { get; }

    public ManualTimeProvider Time { get; } =
        new();

    public EpisodeResolverProbe Probe { get; } =
        new();

    public CapturingLogger<
        EpisodePlaybackResolutionCoordinator> Logger { get; } =
        new();

    public CancellationTokenSource Shutdown { get; } =
        new();

    public ServiceProvider Root { get; }

    public PlaybackResolutionCapacity Capacity { get; }

    public EpisodePlaybackResolutionCoordinator Coordinator { get; }

    public void Dispose()
    {
        Shutdown.Dispose();
        Capacity.Dispose();
        Root.Dispose();
    }
}
