using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// Single-flight, globally bounded orchestration around
/// <see cref="IEpisodePlaybackResolver"/>.
///
/// Concurrent callers for the same episode share one resolution. Once that
/// resolution finishes, nothing is cached and a later request resolves again.
///
/// Execution capacity is shared with movie playback through
/// <see cref="PlaybackResolutionCapacity"/>.
/// </summary>
public sealed partial class EpisodePlaybackResolutionCoordinator
    : IEpisodePlaybackResolutionCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlaybackResolutionOptions _options;
    private readonly PlaybackResolutionCapacity _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EpisodePlaybackResolutionCoordinator> _logger;
    private readonly CancellationToken _shutdown;

    private readonly SingleFlightGroup<Guid, PlaybackCoordinationResult> _flights =
        new();

    public EpisodePlaybackResolutionCoordinator(
        IServiceScopeFactory scopeFactory,
        IOptions<PlaybackResolutionOptions> options,
        PlaybackResolutionCapacity capacity,
        TimeProvider timeProvider,
        ILogger<EpisodePlaybackResolutionCoordinator> logger,
        CancellationToken shutdownToken)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _capacity = capacity;
        _timeProvider = timeProvider;
        _logger = logger;
        _shutdown = shutdownToken;
    }

    public int InFlightCount => _flights.Count;

    public override string ToString() =>
        nameof(EpisodePlaybackResolutionCoordinator);

    public async Task<PlaybackCoordinationResult> ResolveAsync(
        Guid episodeId,
        CancellationToken waiterToken)
    {
        waiterToken.ThrowIfCancellationRequested();

        SingleFlightGroup<Guid, PlaybackCoordinationResult>.Flight flight =
            _flights.JoinOrStart(episodeId, out bool isOwner);

        if (isOwner)
        {
            LogStarted(_logger, episodeId);
            StartDetached(flight);
        }
        else
        {
            LogJoined(_logger, episodeId);
        }

        return await flight.Result.WaitAsync(waiterToken);
    }

    private void StartDetached(
        SingleFlightGroup<Guid, PlaybackCoordinationResult>.Flight flight)
    {
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(
                () => RunFlightAsync(flight),
                CancellationToken.None);
        }
    }

    private async Task RunFlightAsync(
        SingleFlightGroup<Guid, PlaybackCoordinationResult>.Flight flight)
    {
        PlaybackCoordinationResult result =
            new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.Faulted);

        try
        {
            result = await ExecuteAsync(flight.Key);
        }
        catch (Exception exception)
        {
            LogFaulted(
                _logger,
                flight.Key,
                exception.GetType().Name);
        }
        finally
        {
            _flights.Complete(flight, result);

            LogCompleted(
                _logger,
                flight.Key,
                result.GetType().Name);
        }
    }

    private async Task<PlaybackCoordinationResult> ExecuteAsync(
        Guid episodeId)
    {
        try
        {
            if (!await TryAcquireSlotAsync())
            {
                LogBusy(_logger, episodeId);
                return new PlaybackCoordinationResult.Busy();
            }
        }
        catch (OperationCanceledException)
            when (_shutdown.IsCancellationRequested)
        {
            LogShuttingDown(_logger, episodeId);

            return new PlaybackCoordinationResult.Failed(
                PlaybackCoordinationFailure.ShuttingDown);
        }

        try
        {
            using var budget = new CancellationTokenSource(
                _options.ResolutionBudget,
                _timeProvider);

            using var work =
                CancellationTokenSource.CreateLinkedTokenSource(
                    budget.Token,
                    _shutdown);

            try
            {
                await using AsyncServiceScope scope =
                    _scopeFactory.CreateAsyncScope();

                IEpisodePlaybackResolver resolver =
                    scope.ServiceProvider
                        .GetRequiredService<IEpisodePlaybackResolver>();

                PlaybackResolutionResult resolution =
                    await resolver.ResolveAsync(
                        episodeId,
                        work.Token);

                return new PlaybackCoordinationResult.Completed(
                    resolution);
            }
            catch (OperationCanceledException)
                when (_shutdown.IsCancellationRequested)
            {
                LogShuttingDown(_logger, episodeId);

                return new PlaybackCoordinationResult.Failed(
                    PlaybackCoordinationFailure.ShuttingDown);
            }
            catch (OperationCanceledException)
                when (budget.IsCancellationRequested)
            {
                LogBudgetExceeded(_logger, episodeId);

                return new PlaybackCoordinationResult.Failed(
                    PlaybackCoordinationFailure.BudgetExceeded);
            }
            catch (Exception exception)
            {
                LogFaulted(
                    _logger,
                    episodeId,
                    exception.GetType().Name);

                return new PlaybackCoordinationResult.Failed(
                    PlaybackCoordinationFailure.Faulted);
            }
        }
        finally
        {
            try
            {
                _capacity.Release();
            }
            catch (ObjectDisposedException)
            {
                // Host disposal may race with work unwinding after shutdown.
            }
        }
    }

    private async Task<bool> TryAcquireSlotAsync()
    {
        _shutdown.ThrowIfCancellationRequested();

        if (_capacity.TryAcquireImmediately())
        {
            return true;
        }

        using var queueWait =
            new CancellationTokenSource(
                _options.QueueWait,
                _timeProvider);

        using var admission =
            CancellationTokenSource.CreateLinkedTokenSource(
                queueWait.Token,
                _shutdown);

        try
        {
            await _capacity.AcquireAsync(
                admission.Token);

            return true;
        }
        catch (OperationCanceledException)
            when (!_shutdown.IsCancellationRequested)
        {
            return false;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Playback resolution for episode {EpisodeId}: started a shared resolution")]
    private static partial void LogStarted(
        ILogger logger,
        Guid episodeId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Playback resolution for episode {EpisodeId}: joined the resolution already in progress")]
    private static partial void LogJoined(
        ILogger logger,
        Guid episodeId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Playback resolution for episode {EpisodeId}: finished ({Outcome})")]
    private static partial void LogCompleted(
        ILogger logger,
        Guid episodeId,
        string outcome);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Playback resolution for episode {EpisodeId}: no execution slot within the queue wait - busy")]
    private static partial void LogBusy(
        ILogger logger,
        Guid episodeId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Playback resolution for episode {EpisodeId}: exceeded the execution budget and was cancelled")]
    private static partial void LogBudgetExceeded(
        ILogger logger,
        Guid episodeId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Playback resolution for episode {EpisodeId}: cancelled by application shutdown")]
    private static partial void LogShuttingDown(
        ILogger logger,
        Guid episodeId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Playback resolution for episode {EpisodeId} failed unexpectedly ({ExceptionType})")]
    private static partial void LogFaulted(
        ILogger logger,
        Guid episodeId,
        string exceptionType);
}
