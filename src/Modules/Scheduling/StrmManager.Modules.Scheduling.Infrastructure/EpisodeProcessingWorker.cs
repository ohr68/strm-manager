using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Episodes.ProcessEpisode;
using StrmManager.Modules.Catalog.Domain.Episodes;

namespace StrmManager.Modules.Scheduling.Infrastructure;

/// <summary>
/// Discovers Pending episodes and processes up to MaxConcurrentEpisodeProcessing of
/// them concurrently, each through the exact same ProcessEpisodeCommandHandler that
/// POST /api/episodes/{id}/process uses - no second pipeline. The handler's own claim
/// (StartSearching persisted with an optimistic-concurrency check before any external
/// work - see ADR-013) is what makes it safe for this worker and manual API calls to
/// race for the same episode: at most one wins.
/// </summary>
internal sealed partial class EpisodeProcessingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulingOptions> options,
    ILogger<EpisodeProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, options.Value.ProcessingInterval, options.Value.MaxConcurrentEpisodeProcessing);

        using var timer = new PeriodicTimer(options.Value.ProcessingInterval);
        using var concurrencyLimiter = new SemaphoreSlim(options.Value.MaxConcurrentEpisodeProcessing);

        do
        {
            await RunOnceAsync(concurrencyLimiter, stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(SemaphoreSlim concurrencyLimiter, CancellationToken stoppingToken)
    {
        Guid[] pendingEpisodeIds;

        try
        {
            await using AsyncServiceScope discoveryScope = scopeFactory.CreateAsyncScope();
            IEpisodeRepository episodeRepository = discoveryScope.ServiceProvider.GetRequiredService<IEpisodeRepository>();

            IReadOnlyList<Episode> pending = await episodeRepository.GetPendingForProcessingAsync(options.Value.BatchSize, stoppingToken);
            pendingEpisodeIds = pending.Select(episode => episode.Id).ToArray();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogDiscoveryFailed(logger, exception);
            return;
        }

        if (pendingEpisodeIds.Length == 0)
        {
            return;
        }

        await Task.WhenAll(pendingEpisodeIds.Select(episodeId => ProcessOneAsync(episodeId, concurrencyLimiter, stoppingToken)));
    }

    private async Task ProcessOneAsync(Guid episodeId, SemaphoreSlim concurrencyLimiter, CancellationToken stoppingToken)
    {
        await concurrencyLimiter.WaitAsync(stoppingToken);

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ProcessEpisodeCommand, ProcessEpisodeResult>>();

            Result<ProcessEpisodeResult> result = await handler.Handle(new ProcessEpisodeCommand(episodeId), stoppingToken);

            if (result.IsFailure)
            {
                // Includes the expected "already claimed by another request" outcome -
                // logged at Debug, not a real failure (see EpisodeErrors.AlreadyBeingProcessed).
                LogEpisodeNotProcessed(logger, episodeId, result.Error.Code);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down mid-item - the episode's claim (if persisted) is
            // recovered by the maintenance worker's stale-processing sweep, not treated
            // as a failure here.
        }
        catch (Exception exception)
        {
            LogEpisodeProcessingCrashed(logger, episodeId, exception);
        }
        finally
        {
            concurrencyLimiter.Release();
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "EpisodeProcessingWorker disabled (Scheduling:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "EpisodeProcessingWorker started, ticking every {Interval}, max {MaxConcurrent} concurrent")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval, int maxConcurrent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to discover Pending episodes")]
    private static partial void LogDiscoveryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Episode {EpisodeId} not processed this tick: {ErrorCode}")]
    private static partial void LogEpisodeNotProcessed(ILogger logger, Guid episodeId, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing episode {EpisodeId} crashed")]
    private static partial void LogEpisodeProcessingCrashed(ILogger logger, Guid episodeId, Exception exception);
}
