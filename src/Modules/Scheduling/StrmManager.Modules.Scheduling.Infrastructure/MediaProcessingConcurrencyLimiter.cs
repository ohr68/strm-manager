namespace StrmManager.Modules.Scheduling.Infrastructure;

/// <summary>
/// Bounds the combined amount of automatic Episode and Movie processing work.
/// A single limiter prevents the two workers from independently consuming the
/// full ffprobe/provider concurrency budget.
/// </summary>
internal sealed class MediaProcessingConcurrencyLimiter(SchedulingOptions options) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(options.MaxConcurrentMediaProcessing);

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose()
        {
            SemaphoreSlim? semaphore = Interlocked.Exchange(ref _semaphore, null);
            semaphore?.Release();
        }
    }
}
