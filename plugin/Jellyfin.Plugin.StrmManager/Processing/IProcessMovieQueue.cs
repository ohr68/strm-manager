namespace Jellyfin.Plugin.StrmManager.Processing;

/// <summary>
/// The only product-facing surface of the Process Movie queue - enqueue only. The channel reader is deliberately
/// not exposed here (see <see cref="ProcessMovieQueue"/>'s remarks) - only this assembly's own
/// <see cref="ProcessMovieWorker"/> drains it.
///
/// P5 ships this interface with no product code calling it yet - see the P5 report. It exists so a future producer
/// (an admin endpoint, once real ProcessMovie execution is judged safe to expose) can depend on this abstraction
/// rather than the concrete queue.
/// </summary>
#pragma warning disable CA1711 // "Queue" is the correct, established name for this abstraction (matches the P0 spike's own WorkQueue) - not the collection-suffix meaning CA1711 warns about.
public interface IProcessMovieQueue
#pragma warning restore CA1711
{
    /// <summary>Never blocks. Synchronous by design so a future caller (e.g. a controller action) can enqueue and return immediately.</summary>
    ProcessMovieEnqueueResult TryEnqueue(Guid movieId);
}
