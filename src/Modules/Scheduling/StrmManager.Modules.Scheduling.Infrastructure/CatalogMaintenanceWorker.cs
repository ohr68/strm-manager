using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Maintenance;

namespace StrmManager.Modules.Scheduling.Infrastructure;

/// <summary>
/// Thin timer around RunCatalogMaintenanceCommand - all four housekeeping steps
/// (release, retry, stale-processing recovery, metadata-refresh fan-out) are Catalog
/// Application-layer logic; this worker only owns the tick cadence and the
/// IServiceScopeFactory-per-tick scope, per ADR-003. Never touches CatalogDbContext,
/// repositories, or Domain entities directly.
/// </summary>
internal sealed partial class CatalogMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulingOptions> options,
    ILogger<CatalogMaintenanceWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, options.Value.MaintenanceInterval);

        using var timer = new PeriodicTimer(options.Value.MaintenanceInterval);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

            var handler = scope.ServiceProvider
                .GetRequiredService<ICommandHandler<RunCatalogMaintenanceCommand, CatalogMaintenanceResult>>();

            Result<CatalogMaintenanceResult> result = await handler.Handle(new RunCatalogMaintenanceCommand(), stoppingToken);

            if (result.IsFailure)
            {
                LogTickFailed(logger, result.Error.Code);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down mid-tick - not a fault, just stop.
        }
        catch (Exception exception)
        {
            // A single bad tick must not kill this BackgroundService permanently - the
            // next tick tries again. Host cancellation is handled separately above.
            LogTickCrashed(logger, exception);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "CatalogMaintenanceWorker disabled (Scheduling:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "CatalogMaintenanceWorker started, ticking every {Interval}")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog maintenance tick failed: {ErrorCode}")]
    private static partial void LogTickFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog maintenance tick crashed")]
    private static partial void LogTickCrashed(ILogger logger, Exception exception);
}
