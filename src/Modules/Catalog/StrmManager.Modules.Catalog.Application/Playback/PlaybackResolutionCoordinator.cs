using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Single-flight, bounded orchestration around <see cref="IPlaybackResolver"/> (ADR-015).
///
/// Per movie: the first caller starts the shared resolution, every concurrent caller for the same movie joins it, all
/// non-cancelled waiters get the same result, and the entry is removed the moment the work finishes (see
/// <see cref="SingleFlightGroup{TKey, TResult}"/>). Nothing is kept afterwards - this is single-flight, NOT a cache: a
/// later request always starts a new resolution, and Resolved, NotFound, Unavailable, Busy and failures are all treated alike.
///
/// Across movies: at most MaxConcurrentResolutions underlying resolutions execute at once. A new resolution waits up to
/// QueueWait for a slot and otherwise reports Busy without ever calling the resolver; waiters never occupy a slot.
///
/// Once admitted, the resolution runs under a ResolutionBudget. The shared work is cancelled ONLY by that budget or by
/// application shutdown - never by any caller: a caller's token controls only its own wait.
///
/// Lifetime: this is a SINGLETON (the single-flight state must outlive any request) and therefore holds no scoped
/// dependency. It receives only the root <see cref="IServiceScopeFactory"/>; each shared resolution creates and owns ONE
/// scope, resolves IPlaybackResolver from it, and disposes it when the work ends. It never captures a caller's
/// IServiceProvider, scoped repository or DbContext, resolver instance, or CancellationToken, and starts the work with
/// the caller's ambient execution context suppressed.
///
/// Confidentiality: a successful result (holding a PlaybackLocation) lives here only while in flight and is passed on
/// unchanged. Nothing here logs, formats or serializes it; the registry key is the movie Guid alone; an unexpected
/// exception is reduced to its TYPE NAME (its message may contain a URL) and reported as a generic Faulted outcome.
/// </summary>
public sealed partial class PlaybackResolutionCoordinator : IPlaybackResolutionCoordinator, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaybackResolutionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlaybackResolutionCoordinator> _logger;
    private readonly CancellationToken _shutdown;
    private readonly SemaphoreSlim _slots;
    private readonly SingleFlightGroup<Guid, PlaybackCoordinationResult> _flights = new();

    /// <param name="shutdownToken">Cancelled when the application begins to stop (the host's ApplicationStopping).</param>
    public PlaybackResolutionCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<PlaybackResolutionOptions> options,
        TimeProvider timeProvider,
        ILogger<PlaybackResolutionCoordinator> logger,
        CancellationToken shutdownToken)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _shutdown = shutdownToken;
        _slots = new SemaphoreSlim(_options.MaxConcurrentResolutions, _options.MaxConcurrentResolutions);
    }

    /// <summary>How many resolutions are in flight (queued for a slot or executing). A count only - no keys, no results.</summary>
    public int InFlightCount => _flights.Count;

    public override string ToString() => nameof(PlaybackResolutionCoordinator);

    /// <summary>Releases the slot semaphore when the container disposes this singleton at host shutdown.</summary>
    public void Dispose() => _slots.Dispose();

    public async Task<PlaybackCoordinationResult> ResolveAsync(Guid movieId, CancellationToken waiterToken)
    {
        // A caller that has already gone away must not start (or join) any work.
        waiterToken.ThrowIfCancellationRequested();

        SingleFlightGroup<Guid, PlaybackCoordinationResult>.Flight flight = _flights.JoinOrStart(movieId, out bool isOwner);

        if (isOwner)
        {
            LogStarted(_logger, movieId);
            StartDetached(flight);
        }
        else
        {
            LogJoined(_logger, movieId);
        }

        // Only THIS caller's wait is cancellable; the shared flight is untouched by it.
        return await flight.Result.WaitAsync(waiterToken);
    }

    /// <summary>
    /// Runs the flight on the thread pool, detached from the caller: no caller token, and the caller's ambient
    /// ExecutionContext (activity, logging scopes, ...) is not flowed into work that outlives its request.
    /// </summary>
    private void StartDetached(SingleFlightGroup<Guid, PlaybackCoordinationResult>.Flight flight)
    {
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => RunFlightAsync(flight), CancellationToken.None);
        }
    }

    private async Task RunFlightAsync(SingleFlightGroup<Guid, PlaybackCoordinationResult>.Flight flight)
    {
        // Whatever happens below, the flight is completed - removed, then published - exactly once. The default is the
        // safe outcome if something escapes everything that is handled explicitly.
        PlaybackCoordinationResult result = new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.Faulted);

        try
        {
            result = await ExecuteAsync(flight.Key);
        }
        catch (Exception exception)
        {
            LogFaulted(_logger, flight.Key, exception.GetType().Name);
        }
        finally
        {
            _flights.Complete(flight, result);
            LogCompleted(_logger, flight.Key, result.GetType().Name);
        }
    }

    private async Task<PlaybackCoordinationResult> ExecuteAsync(Guid movieId)
    {
        // 1) Admission: a slot within the queue wait, or Busy (the resolver is never called).
        try
        {
            if (!await TryAcquireSlotAsync())
            {
                LogBusy(_logger, movieId);
                return new PlaybackCoordinationResult.Busy();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            LogShuttingDown(_logger, movieId);
            return new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown);
        }

        try
        {
            // 2) Execution under the budget, cancelled only by the budget or shutdown.
            using var budget = new CancellationTokenSource(_options.ResolutionBudget, _timeProvider);
            using var work = CancellationTokenSource.CreateLinkedTokenSource(budget.Token, _shutdown);

            try
            {
                // ONE scope per shared resolution, owned by this work and disposed when it ends.
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                IPlaybackResolver resolver = scope.ServiceProvider.GetRequiredService<IPlaybackResolver>();

                PlaybackResolutionResult resolution = await resolver.ResolveAsync(movieId, work.Token);

                return new PlaybackCoordinationResult.Completed(resolution);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                LogShuttingDown(_logger, movieId);
                return new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown);
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                LogBudgetExceeded(_logger, movieId);
                return new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.BudgetExceeded);
            }
            catch (Exception exception)
            {
                // Type name only: the message (or an inner exception) may contain a provider URL.
                LogFaulted(_logger, movieId, exception.GetType().Name);
                return new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.Faulted);
            }
        }
        finally
        {
            try
            {
                _slots.Release();
            }
            catch (ObjectDisposedException)
            {
                // The host disposed this singleton while work was still unwinding after shutdown; nothing left to release.
            }
        }
    }

    /// <summary>True once a slot is held; false if the queue wait elapsed first. Throws OperationCanceledException only for shutdown.</summary>
    private async Task<bool> TryAcquireSlotAsync()
    {
        _shutdown.ThrowIfCancellationRequested();

        // A free slot is taken at once, so a zero queue wait means "never queue" rather than "always refuse".
        if (_slots.Wait(0))
        {
            return true;
        }

        using var queueWait = new CancellationTokenSource(_options.QueueWait, _timeProvider);
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(queueWait.Token, _shutdown);

        try
        {
            await _slots.WaitAsync(admission.Token);
            return true;
        }
        catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
        {
            return false;
        }
    }

    // Ids, counts and enum-like names only - never a result, a URL, an exception message or a candidate.

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback resolution for movie {MovieId}: started a shared resolution")]
    private static partial void LogStarted(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback resolution for movie {MovieId}: joined the resolution already in progress")]
    private static partial void LogJoined(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playback resolution for movie {MovieId}: finished ({Outcome})")]
    private static partial void LogCompleted(ILogger logger, Guid movieId, string outcome);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playback resolution for movie {MovieId}: no execution slot within the queue wait - busy")]
    private static partial void LogBusy(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Playback resolution for movie {MovieId}: exceeded the execution budget and was cancelled")]
    private static partial void LogBudgetExceeded(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playback resolution for movie {MovieId}: cancelled by application shutdown")]
    private static partial void LogShuttingDown(ILogger logger, Guid movieId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Playback resolution for movie {MovieId} failed unexpectedly ({ExceptionType})")]
    private static partial void LogFaulted(ILogger logger, Guid movieId, string exceptionType);
}
