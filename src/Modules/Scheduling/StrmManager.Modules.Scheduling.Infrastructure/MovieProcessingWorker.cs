using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StrmManager.Common.Application.Messaging;
using StrmManager.Common.Domain.Abstractions;
using StrmManager.Modules.Catalog.Application.Movies.ProcessMovie;
using StrmManager.Modules.Catalog.Domain.Movies;

namespace StrmManager.Modules.Scheduling.Infrastructure;

internal sealed partial class MovieProcessingWorker(
    IServiceScopeFactory scopeFactory,
    MediaProcessingConcurrencyLimiter concurrencyLimiter,
    IOptions<SchedulingOptions> options,
    ILogger<MovieProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(
            logger,
            options.Value.ProcessingInterval,
            options.Value.MaxConcurrentMediaProcessing);

        using var timer = new PeriodicTimer(options.Value.ProcessingInterval);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        Guid[] pendingMovieIds;

        try
        {
            await using AsyncServiceScope discoveryScope =
                scopeFactory.CreateAsyncScope();

            IMovieRepository movieRepository =
                discoveryScope.ServiceProvider.GetRequiredService<IMovieRepository>();

            IReadOnlyList<Movie> pending =
                await movieRepository.GetPendingForProcessingAsync(
                    options.Value.BatchSize,
                    stoppingToken);

            pendingMovieIds = pending.Select(movie => movie.Id).ToArray();
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            LogDiscoveryFailed(logger, exception);
            return;
        }

        if (pendingMovieIds.Length == 0)
        {
            return;
        }

        await Task.WhenAll(
            pendingMovieIds.Select(
                movieId => ProcessOneAsync(movieId, stoppingToken)));
    }

    private async Task ProcessOneAsync(
        Guid movieId,
        CancellationToken stoppingToken)
    {
        try
        {
            using IDisposable lease =
                await concurrencyLimiter.AcquireAsync(stoppingToken);

            await using AsyncServiceScope scope =
                scopeFactory.CreateAsyncScope();

            var handler = scope.ServiceProvider
                .GetRequiredService<
                    ICommandHandler<ProcessMovieCommand, ProcessMovieResult>>();

            Result<ProcessMovieResult> result =
                await handler.Handle(
                    new ProcessMovieCommand(movieId),
                    stoppingToken);

            if (result.IsFailure)
            {
                LogMovieNotProcessed(
                    logger,
                    movieId,
                    result.Error.Code);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Maintenance recovers a persisted in-flight claim if shutdown
            // interrupts processing.
        }
        catch (Exception exception)
        {
            LogMovieProcessingCrashed(
                logger,
                movieId,
                exception);
        }
    }

    private static async Task<bool> WaitForNextTickAsync(
        PeriodicTimer timer,
        CancellationToken stoppingToken)
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "MovieProcessingWorker disabled (Scheduling:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "MovieProcessingWorker started, ticking every {Interval}, shared max {MaxConcurrent} concurrent")]
    private static partial void LogStarted(
        ILogger logger,
        TimeSpan interval,
        int maxConcurrent);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to discover Pending movies")]
    private static partial void LogDiscoveryFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Movie {MovieId} not processed this tick: {ErrorCode}")]
    private static partial void LogMovieNotProcessed(
        ILogger logger,
        Guid movieId,
        string errorCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Processing movie {MovieId} crashed")]
    private static partial void LogMovieProcessingCrashed(
        ILogger logger,
        Guid movieId,
        Exception exception);
}
