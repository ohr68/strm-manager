using Jellyfin.Plugin.StrmManager.Processing;
using Jellyfin.Plugin.StrmManager.StrmManagerClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for ProcessMovieWorker - drives the real BackgroundService lifecycle (StartAsync/StopAsync)
/// against a fake IStrmManagerClient resolved through a fake IServiceScopeFactory, no running plugin/Jellyfin host.
/// Matches the P5 report's committed scenarios.
/// </summary>
public sealed class ProcessMovieWorkerTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    public static IEnumerable<object[]> AllProcessMovieResultCases()
    {
        yield return [new ProcessMovieResult.Completed(Guid.NewGuid(), 1, "FrostStream", "1080p", "/data/strm/x.strm")];
        yield return [new ProcessMovieResult.Unavailable(2, "No candidate passed validation.")];
        yield return [new ProcessMovieResult.ProcessingFailed(1, "Stream provider failed.")];
        yield return [new ProcessMovieResult.AlreadyBeingProcessed()];
        yield return [new ProcessMovieResult.InvalidTransition()];
        yield return [new ProcessMovieResult.NotFound()];
        yield return [new ProcessMovieResult.Unreachable("Connection refused")];
        yield return [new ProcessMovieResult.Error("Unexpected HTTP status 500.")];
    }

    [Theory]
    [MemberData(nameof(AllProcessMovieResultCases))]
    public async Task EveryProcessMovieResultCase_IsHandledWithoutCrashing_AndReleasesTheDedupReservation(ProcessMovieResult result)
    {
        var queue = new ProcessMovieQueue();
        Guid movieId = Guid.NewGuid();
        Assert.Equal(ProcessMovieEnqueueResult.Accepted, queue.TryEnqueue(movieId));

        var handled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeStrmManagerClient((_, _) =>
        {
            handled.TrySetResult(true);
            return Task.FromResult(result);
        });
        var worker = new ProcessMovieWorker(queue, new FakeServiceScopeFactory(client), NullLogger<ProcessMovieWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await handled.Task.WaitAsync(WaitTimeout);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // Release happens in a finally AFTER the result is handled - poll briefly rather than assume it has
        // already run at the instant the fake client returned.
        ProcessMovieEnqueueResult reEnqueue = await WaitForAsync(() => queue.TryEnqueue(movieId), r => r == ProcessMovieEnqueueResult.Accepted);
        Assert.Equal(ProcessMovieEnqueueResult.Accepted, reEnqueue);
    }

    [Fact]
    public async Task HostShutdown_StopsTheActiveItemCleanly_AndNeverStartsAFurtherBufferedItem()
    {
        var queue = new ProcessMovieQueue();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Assert.Equal(ProcessMovieEnqueueResult.Accepted, queue.TryEnqueue(firstId));
        Assert.Equal(ProcessMovieEnqueueResult.Accepted, queue.TryEnqueue(secondId));

        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int secondCallCount = 0;

        var client = new FakeStrmManagerClient(async (movieId, cancellationToken) =>
        {
            if (movieId == secondId)
            {
                Interlocked.Increment(ref secondCallCount);
                return new ProcessMovieResult.NotFound();
            }

            firstStarted.TrySetResult(true);

            // Hangs until the worker's own stopping token (host shutdown) cancels it - never a request token, since
            // none exists in this architecture.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ProcessMovieResult.NotFound();
        });
        var worker = new ProcessMovieWorker(queue, new FakeServiceScopeFactory(client), NullLogger<ProcessMovieWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await firstStarted.Task.WaitAsync(WaitTimeout);

        await worker.StopAsync(CancellationToken.None).WaitAsync(WaitTimeout);

        Assert.Equal(0, secondCallCount);
    }

    private static async Task<T> WaitForAsync<T>(Func<T> poll, Func<T, bool> isDone)
    {
        using var cts = new CancellationTokenSource(WaitTimeout);
        while (true)
        {
            T value = poll();
            if (isDone(value))
            {
                return value;
            }

            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, CancellationToken.None);
        }
    }

    private sealed class FakeStrmManagerClient(Func<Guid, CancellationToken, Task<ProcessMovieResult>> handle) : IStrmManagerClient
    {
        public Task<MovieLookupResult> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovieWorker does not call GetByImdbIdAsync.");

        public Task<AddMovieResult> AddMovieAsync(string imdbId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovieWorker does not call AddMovieAsync.");

        public Task<ProcessMovieResult> ProcessMovieAsync(Guid movieId, CancellationToken cancellationToken) =>
            handle(movieId, cancellationToken);

        public Task<CatalogMoviesResult> GetPopularMoviesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("ProcessMovieWorker does not call GetPopularMoviesAsync.");
    }

    private sealed class FakeServiceScopeFactory(IStrmManagerClient client) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeServiceScope(client);
    }

    private sealed class FakeServiceScope(IStrmManagerClient client) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider(client);

        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider(IStrmManagerClient client) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IStrmManagerClient) ? client : null;
    }
}
