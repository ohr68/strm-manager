using Jellyfin.Plugin.StrmManager.Processing;

namespace Jellyfin.Plugin.StrmManager.Tests;

/// <summary>
/// Focused unit tests for ProcessMovieQueue - enqueue/dedup/capacity behavior only, no worker involved. Matches the
/// P5 report's committed scenarios.
/// </summary>
public sealed class ProcessMovieQueueTests
{
    [Fact]
    public void AnEmptyQueue_AcceptsANewMovieId()
    {
        var queue = new ProcessMovieQueue();

        ProcessMovieEnqueueResult result = queue.TryEnqueue(Guid.NewGuid());

        Assert.Equal(ProcessMovieEnqueueResult.Accepted, result);
    }

    [Fact]
    public void TheSameMovieId_EnqueuedTwice_IsAcceptedThenAlreadyQueued()
    {
        var queue = new ProcessMovieQueue();
        Guid movieId = Guid.NewGuid();

        ProcessMovieEnqueueResult first = queue.TryEnqueue(movieId);
        ProcessMovieEnqueueResult second = queue.TryEnqueue(movieId);

        Assert.Equal(ProcessMovieEnqueueResult.Accepted, first);
        Assert.Equal(ProcessMovieEnqueueResult.AlreadyQueued, second);
    }

    [Fact]
    public void AFullQueue_RejectsAFurtherUniqueId_WithNoDanglingReservationLeftBehind()
    {
        var queue = new ProcessMovieQueue();

        // Capacity is 8 (ProcessMovieQueue's own constant) - fill it with distinct ids. No worker runs in this
        // test, so nothing dequeues them.
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(ProcessMovieEnqueueResult.Accepted, queue.TryEnqueue(Guid.NewGuid()));
        }

        Guid overflowId = Guid.NewGuid();
        Assert.Equal(ProcessMovieEnqueueResult.Full, queue.TryEnqueue(overflowId));

        // If the failed attempt above had left a dangling dedup reservation for overflowId, this retry would
        // incorrectly report AlreadyQueued instead of Full - proving the rollback in TryEnqueue actually ran.
        Assert.Equal(ProcessMovieEnqueueResult.Full, queue.TryEnqueue(overflowId));
    }
}
