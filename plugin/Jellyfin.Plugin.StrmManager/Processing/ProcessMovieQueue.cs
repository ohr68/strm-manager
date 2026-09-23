using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Jellyfin.Plugin.StrmManager.Processing;

/// <summary>
/// Singleton bounded queue of movie ids awaiting a future ProcessMovieAsync run. <see cref="TryEnqueue"/> (via
/// <see cref="IProcessMovieQueue"/>) is the only product-facing surface - the <see cref="Reader"/> and
/// <see cref="Release"/> members are internal to this assembly, used only by <see cref="ProcessMovieWorker"/>, so a
/// controller or other product code can never drain the channel directly or manipulate the dedup set.
///
/// FullMode is Wait, not DropWrite: TryWrite itself never blocks regardless of FullMode, so Wait costs nothing here
/// and avoids any channel-level semantics that could silently discard an accepted item - a caller only ever learns
/// "accepted" or "full" from TryEnqueue itself, never a silent drop.
///
/// P5 is infrastructure only - nothing in the product plugin calls TryEnqueue yet (see the P5 report).
/// ProcessMovieWorker will simply wait on an empty queue until a future slice wires a real producer, and real
/// ProcessMovie execution is not yet judged safe to expose (backend Movie stale-processing recovery does not
/// exist).
/// </summary>
#pragma warning disable CA1711 // "Queue" is the correct, established name for this abstraction (matches the P0 spike's own WorkQueue) - not the collection-suffix meaning CA1711 warns about.
public sealed class ProcessMovieQueue : IProcessMovieQueue
#pragma warning restore CA1711
{
    private const int Capacity = 8;

    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });

    /// <summary>
    /// Movie ids currently queued or running - queued and running are deliberately not distinguished. A successful
    /// <see cref="TryEnqueue"/> reserves the id here first; the worker removes it only once it has finished
    /// handling that item, whatever the outcome. This is a duplicate-suppression aid only (the backend's own 409
    /// already rejects genuinely concurrent processing) - not a job-status model.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _queuedOrRunning = new();

    internal ChannelReader<Guid> Reader => _channel.Reader;

    public ProcessMovieEnqueueResult TryEnqueue(Guid movieId)
    {
        // ConcurrentDictionary.TryAdd is atomic, so of any number of concurrent callers for the SAME id, exactly
        // one wins this reservation - every other caller gets AlreadyQueued immediately, before ever touching the
        // channel.
        if (!_queuedOrRunning.TryAdd(movieId, 0))
        {
            return ProcessMovieEnqueueResult.AlreadyQueued;
        }

        if (_channel.Writer.TryWrite(movieId))
        {
            return ProcessMovieEnqueueResult.Accepted;
        }

        // The channel was full - roll back the reservation so a later enqueue for this id can succeed. TryWrite
        // never blocks, so the reservation is held for a negligible window before this rollback.
        _queuedOrRunning.TryRemove(movieId, out _);
        return ProcessMovieEnqueueResult.Full;
    }

    /// <summary>Called by the worker once it has finished handling a dequeued item, regardless of outcome.</summary>
    internal void Release(Guid movieId) => _queuedOrRunning.TryRemove(movieId, out _);
}
