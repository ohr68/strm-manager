using System.Diagnostics;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StrmManager.Processing;

/// <summary>
/// Drains <see cref="ProcessMovieQueue"/> one item at a time under the HOST's own stopping token - never a
/// browser/controller request token, which never reaches this class or IStrmManagerClient.ProcessMovieAsync at all
/// in this architecture. <see cref="IStrmManagerClient"/> is resolved fresh from a DI scope per item rather than
/// held as a singleton-captured field, so the typed HttpClient's BaseUrl-refresh behavior (see
/// PluginServiceRegistrator) is preserved - a singleton worker holding a transient typed client for its whole
/// lifetime would defeat that.
///
/// P5 has no producer wired anywhere in product code (see the P5 report) - this worker simply waits on an empty
/// queue until a future slice adds one. No processing budget/CancelAfter is applied here: an arbitrary worker
/// timeout would itself become another cancellation source capable of interrupting a backend run after its durable
/// Pending -&gt; Searching claim, which is exactly the still-open risk (no backend Movie stale-processing recovery)
/// that keeps real execution unreachable from product code in this slice.
/// </summary>
public sealed partial class ProcessMovieWorker(
    ProcessMovieQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessMovieWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (Guid movieId in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                // ReadAllAsync keeps yielding items that are already buffered even after the token is cancelled, so
                // without this check every remaining queued item would be "started" and instantly cancelled during
                // shutdown - see the P0 spike's SpikeWorker, which established this exact check.
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await ProcessOneAsync(movieId, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopping(logger);
        }
    }

    private async Task ProcessOneAsync(Guid movieId, CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<IStrmManagerClient>();

            ProcessMovieResult result = await client.ProcessMovieAsync(movieId, stoppingToken).ConfigureAwait(false);

            LogResult(movieId, result, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown interrupted an active run - not a processing result, never logged/translated as one.
            // Let it propagate so the outer loop's own handler performs the clean-shutdown log exactly once.
            throw;
        }
        catch (Exception exception)
        {
            // A single bad item must never take the worker down.
            LogUnhandledFailure(logger, movieId, exception);
        }
        finally
        {
            queue.Release(movieId);
        }
    }

    private void LogResult(Guid movieId, ProcessMovieResult result, double elapsedMs)
    {
        switch (result)
        {
            case ProcessMovieResult.Completed completed:
                LogCompleted(movieId, completed.Attempts, elapsedMs);
                break;
            case ProcessMovieResult.Unavailable unavailable:
                LogUnavailable(movieId, unavailable.Attempts, elapsedMs);
                break;
            case ProcessMovieResult.AlreadyBeingProcessed:
                LogAlreadyBeingProcessed(movieId, elapsedMs);
                break;
            case ProcessMovieResult.ProcessingFailed processingFailed:
                LogProcessingFailed(movieId, processingFailed.Attempts, elapsedMs);
                break;
            case ProcessMovieResult.InvalidTransition:
                LogInvalidTransition(movieId, elapsedMs);
                break;
            case ProcessMovieResult.NotFound:
                LogNotFound(movieId, elapsedMs);
                break;
            case ProcessMovieResult.Unreachable:
                LogUnreachable(movieId, elapsedMs);
                break;
            case ProcessMovieResult.Error:
                LogError(movieId, elapsedMs);
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessMovieWorker stopping: host shutdown requested")]
    private static partial void LogWorkerStopping(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "ProcessMovieWorker: unhandled failure processing movie {MovieId}; worker continues")]
    private static partial void LogUnhandledFailure(ILogger logger, Guid movieId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessMovie {MovieId} completed after {Attempts} attempt(s) in {ElapsedMs} ms")]
    private partial void LogCompleted(Guid movieId, int attempts, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessMovie {MovieId} reported Unavailable after {Attempts} attempt(s) in {ElapsedMs} ms")]
    private partial void LogUnavailable(Guid movieId, int attempts, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "ProcessMovie {MovieId} was already being processed by another request ({ElapsedMs} ms)")]
    private partial void LogAlreadyBeingProcessed(Guid movieId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ProcessMovie {MovieId} reported a processing failure after {Attempts} attempt(s) in {ElapsedMs} ms")]
    private partial void LogProcessingFailed(Guid movieId, int attempts, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ProcessMovie {MovieId} was rejected as an invalid state transition ({ElapsedMs} ms)")]
    private partial void LogInvalidTransition(Guid movieId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ProcessMovie {MovieId} was not found ({ElapsedMs} ms)")]
    private partial void LogNotFound(Guid movieId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ProcessMovie {MovieId} could not reach STRM Manager ({ElapsedMs} ms)")]
    private partial void LogUnreachable(Guid movieId, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ProcessMovie {MovieId} returned an unexpected client-side error ({ElapsedMs} ms)")]
    private partial void LogError(Guid movieId, double elapsedMs);
}
