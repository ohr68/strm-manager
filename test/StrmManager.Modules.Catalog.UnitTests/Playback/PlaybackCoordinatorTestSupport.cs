using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Application.Playback;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

/// <summary>
/// A TimeProvider whose timers fire only when the test says so. PlaybackResolutionCoordinator builds its queue-wait and
/// budget deadlines through the injected TimeProvider, so with this the tests decide exactly when a deadline passes -
/// no sleeping, no dependence on how fast the machine is.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Timers that have been created and not yet fired or disposed.</summary>
    public int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.DueAt is not null);
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        List<ManualTimer> due;

        lock (_gate)
        {
            _now += delta;
            due = _timers.Where(timer => timer.DueAt is { } at && at <= _now).ToList();

            foreach (ManualTimer timer in due)
            {
                timer.DueAt = null;
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; } // guarded by owner._gate

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
            {
                DueAt = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Everything a log sink could see at any level: formatted message, every structured property, full exception text.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public string AllText => string.Join('\n', _entries);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var entry = new StringBuilder();
        entry.Append('[').Append(logLevel).Append("] ").Append(formatter(state, exception));

        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach (KeyValuePair<string, object?> property in properties)
            {
                entry.Append(" | ").Append(property.Key).Append('=').Append(property.Value);
            }
        }

        if (exception is not null)
        {
            entry.Append(" | exception=").Append(exception);
        }

        _entries.Enqueue(entry.ToString());
    }
}

/// <summary>
/// The stand-in for IPlaybackResolver behind the coordinator: records every call (with the token it was given), tracks
/// how many run at once, and either blocks until the test completes it or answers immediately. Each instance the
/// container creates is recorded, along with whether its scope has been disposed.
/// </summary>
internal sealed class ResolverProbe
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PlaybackResolutionResult>> _blocked = new();
    private int _calls;
    private int _running;
    private int _maxRunning;
    private int _instances;

    public ResolverProbe()
    {
        Behavior = BlockUntilCompleted;
    }

    public int Calls => Volatile.Read(ref _calls);

    public int Running => Volatile.Read(ref _running);

    public int MaxRunning => Volatile.Read(ref _maxRunning);

    public int InstancesCreated => Volatile.Read(ref _instances);

    public ConcurrentQueue<(Guid MovieId, CancellationToken Token)> Invocations { get; } = new();

    public ConcurrentQueue<ProbeResolver> Instances { get; } = new();

    public Func<Guid, CancellationToken, Task<PlaybackResolutionResult>> Behavior { get; set; }

    public IEnumerable<Guid> MoviesCalled => Invocations.Select(i => i.MovieId);

    public CancellationToken TokenOfCallFor(Guid movieId) => Invocations.Last(i => i.MovieId == movieId).Token;

    /// <summary>Blocks each call until <see cref="Complete"/> is called for its movie (or the call's token is cancelled).</summary>
    public Task<PlaybackResolutionResult> BlockUntilCompleted(Guid movieId, CancellationToken token)
    {
        var gate = new TaskCompletionSource<PlaybackResolutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _blocked[movieId] = gate;
        return gate.Task.WaitAsync(token);
    }

    public void Complete(Guid movieId, PlaybackResolutionResult result) => _blocked[movieId].SetResult(result);

    public ProbeResolver Create(IServiceProvider scopeProvider)
    {
        var resolver = new ProbeResolver(this, scopeProvider, Interlocked.Increment(ref _instances));
        Instances.Enqueue(resolver);
        return resolver;
    }

    internal async Task<PlaybackResolutionResult> RunAsync(Guid movieId, CancellationToken token)
    {
        Interlocked.Increment(ref _calls);
        Invocations.Enqueue((movieId, token));

        int now = Interlocked.Increment(ref _running);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxRunning)) && Interlocked.CompareExchange(ref _maxRunning, now, seen) != seen)
        {
        }

        try
        {
            return await Behavior(movieId, token);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}

internal sealed class ProbeResolver(ResolverProbe probe, IServiceProvider scopeProvider, int id) : IPlaybackResolver, IDisposable
{
    public int Id { get; } = id;

    /// <summary>The provider this instance was created from: the scope's, never the root.</summary>
    public IServiceProvider ScopeProvider { get; } = scopeProvider;

    /// <summary>True once the scope that owns this instance has been disposed.</summary>
    public bool Disposed { get; private set; }

    public Task<PlaybackResolutionResult> ResolveAsync(Guid movieId, CancellationToken cancellationToken) => probe.RunAsync(movieId, cancellationToken);

    public void Dispose() => Disposed = true;
}

/// <summary>A coordinator over a real container (scope validation on), a manual clock, a shutdown token and a capturing logger.</summary>
internal sealed class CoordinatorHarness : IDisposable
{
    public const string CanaryHost = "canary-host-4e7a19.invalid";
    public const string PathSecret = "PATHSECRET-2c68";
    public const string QuerySecret = "QUERYSECRET-95f1";
    public const string RawExceptionText = "RAWEXCEPTIONTEXT-7d30";

    public static readonly string[] AllSecrets = [CanaryHost, PathSecret, QuerySecret, RawExceptionText];

    public CoordinatorHarness(int maxConcurrent = 2, TimeSpan? queueWait = null, TimeSpan? budget = null)
    {
        QueueWait = queueWait ?? TimeSpan.FromSeconds(5);
        Budget = budget ?? TimeSpan.FromSeconds(60);

        Root = new ServiceCollection()
            .AddScoped<IPlaybackResolver>(scope => Probe.Create(scope))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Coordinator = new PlaybackResolutionCoordinator(
            Root.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PlaybackResolutionOptions { MaxConcurrentResolutions = maxConcurrent, QueueWait = QueueWait, ResolutionBudget = Budget }),
            Time,
            Logger,
            Shutdown.Token);
    }

    public TimeSpan QueueWait { get; }

    public TimeSpan Budget { get; }

    public ManualTimeProvider Time { get; } = new();

    public ResolverProbe Probe { get; } = new();

    public CapturingLogger<PlaybackResolutionCoordinator> Logger { get; } = new();

    public CancellationTokenSource Shutdown { get; } = new();

    public ServiceProvider Root { get; }

    public PlaybackResolutionCoordinator Coordinator { get; }

    public static string UrlFor(string label) => $"https://{CanaryHost}/hls/{PathSecret}/{label}.m3u8?sig={QuerySecret}-{label}";

    public static PlaybackResolutionResult.Resolved Resolved(string label) =>
        new("FrostStream", label, new PlaybackLocation(UrlFor(label)));

    public static PlaybackResolutionResult NotFound() => new PlaybackResolutionResult.NotFound();

    public static PlaybackResolutionResult Unavailable() => new PlaybackResolutionResult.Unavailable(PlaybackUnavailableReason.NoApprovedCandidate);

    /// <summary>Starts N callers one after another on this thread; each has joined (or started) the flight by the time its call returns.</summary>
    public Task<PlaybackCoordinationResult>[] StartCallers(Guid movieId, int count, CancellationToken token = default) =>
        Enumerable.Range(0, count).Select(_ => Coordinator.ResolveAsync(movieId, token)).ToArray();

    public static async Task EventuallyAsync(Func<bool> condition, string what)
    {
        DateTime giveUp = DateTime.UtcNow.AddSeconds(10); // a failure guard only - never part of what a test asserts

        while (!condition())
        {
            if (DateTime.UtcNow > giveUp)
            {
                Assert.Fail($"Timed out waiting for: {what}");
            }

            await Task.Delay(1);
        }
    }

    /// <summary>Every place a secret could be observed: the result's text and JSON, the coordinator's text, and every log entry.</summary>
    public string EverythingObservable(params PlaybackCoordinationResult[] results) =>
        string.Join('\n', results.Select(r => r.ToString()).Concat(results.Select(r => System.Text.Json.JsonSerializer.Serialize(r, r.GetType())))
            .Append(Coordinator.ToString()).Append(Logger.AllText));

    public static void AssertNoSecrets(string text)
    {
        foreach (string secret in AllSecrets)
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        Shutdown.Dispose();
        Coordinator.Dispose();
        Root.Dispose();
    }
}
