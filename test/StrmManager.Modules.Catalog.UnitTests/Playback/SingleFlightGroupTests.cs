using StrmManager.Modules.Catalog.Application.Playback;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

/// <summary>
/// The single-flight registry on its own: who owns a flight, who removes it, and why a finished flight can never be
/// mistaken for a cache entry. The registry holds no work and no clock, so every case here is deterministic.
/// </summary>
public class SingleFlightGroupTests
{
    [Fact]
    public void FirstCallerOwnsTheFlight_LaterCallersJoinTheSameOne()
    {
        var group = new SingleFlightGroup<int, string>();

        SingleFlightGroup<int, string>.Flight first = group.JoinOrStart(1, out bool firstIsOwner);
        SingleFlightGroup<int, string>.Flight second = group.JoinOrStart(1, out bool secondIsOwner);
        SingleFlightGroup<int, string>.Flight third = group.JoinOrStart(1, out bool thirdIsOwner);

        Assert.True(firstIsOwner);
        Assert.False(secondIsOwner);
        Assert.False(thirdIsOwner);
        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Equal(1, group.Count);
    }

    [Fact]
    public void DifferentKeysGetIndependentFlights()
    {
        var group = new SingleFlightGroup<int, string>();

        SingleFlightGroup<int, string>.Flight a = group.JoinOrStart(1, out bool aOwner);
        SingleFlightGroup<int, string>.Flight b = group.JoinOrStart(2, out bool bOwner);

        Assert.True(aOwner);
        Assert.True(bOwner);
        Assert.NotSame(a, b);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public async Task Complete_RemovesTheEntryBeforeTheResultIsObservable()
    {
        var group = new SingleFlightGroup<int, string>();
        SingleFlightGroup<int, string>.Flight flight = group.JoinOrStart(1, out _);

        int countWhenResultObserved = -1;
        Task observer = flight.Result.ContinueWith(_ => countWhenResultObserved = group.Count, TaskScheduler.Default);

        group.Complete(flight, "done");

        Assert.Equal("done", await flight.Result);
        await observer;
        Assert.Equal(0, countWhenResultObserved); // by the time anyone can see the result, the entry is already gone
    }

    [Fact]
    public void AtTheInstantTheResultBecomesVisible_TheFinishedEntryIsAlreadyGone()
    {
        // Waiters run INSIDE the publish here, so this observes what a new caller would find at that exact moment.
        var group = new SingleFlightGroup<int, string>(runContinuationsAsynchronously: false);
        SingleFlightGroup<int, string>.Flight flight = group.JoinOrStart(1, out _);

        bool? newCallerWouldOwn = null;
        _ = flight.Result.ContinueWith(
            _ =>
            {
                group.JoinOrStart(1, out bool isOwner);
                newCallerWouldOwn = isOwner;
            },
            TaskContinuationOptions.ExecuteSynchronously);

        group.Complete(flight, "done");

        Assert.True(newCallerWouldOwn); // it found no finished flight to reuse: it must start its own
    }

    [Fact]
    public async Task ACallerArrivingAfterCompletion_StartsAFreshFlight_AndNeverSeesTheOldResult()
    {
        var group = new SingleFlightGroup<int, string>();
        SingleFlightGroup<int, string>.Flight old = group.JoinOrStart(1, out _);
        group.Complete(old, "old-result");
        await old.Result;

        SingleFlightGroup<int, string>.Flight fresh = group.JoinOrStart(1, out bool isOwner);

        Assert.True(isOwner); // it must run its own work
        Assert.NotSame(old, fresh);
        Assert.False(fresh.Result.IsCompleted); // and there is no completed result waiting for it
    }

    [Fact]
    public async Task AStaleCompletion_CannotRemoveANewerFlightForTheSameKey()
    {
        var group = new SingleFlightGroup<int, string>();
        SingleFlightGroup<int, string>.Flight older = group.JoinOrStart(1, out _);
        group.Complete(older, "older-result");

        SingleFlightGroup<int, string>.Flight newer = group.JoinOrStart(1, out bool newerIsOwner);
        Assert.True(newerIsOwner);

        group.Complete(older, "stale-completion"); // the older flight completing again, late

        Assert.Equal(1, group.Count); // the newer entry survived
        Assert.Same(newer, group.JoinOrStart(1, out bool joinedIsOwner)); // and new callers still join it
        Assert.False(joinedIsOwner);
        Assert.False(newer.Result.IsCompleted);
        Assert.Equal("older-result", await older.Result); // the first published result is final
    }

    [Fact]
    public async Task ResultIsPublishedToEveryWaiterOfThatFlight()
    {
        var group = new SingleFlightGroup<int, string>();
        SingleFlightGroup<int, string>.Flight owner = group.JoinOrStart(1, out _);
        SingleFlightGroup<int, string>.Flight waiter1 = group.JoinOrStart(1, out _);
        SingleFlightGroup<int, string>.Flight waiter2 = group.JoinOrStart(1, out _);

        group.Complete(owner, "shared");

        Assert.Equal(["shared", "shared", "shared"], await Task.WhenAll(owner.Result, waiter1.Result, waiter2.Result));
    }

    [Fact]
    public async Task ConcurrentJoinsAndCompletions_NeverProduceTwoOwnersOfOneFlight_OrAReusedResult()
    {
        var group = new SingleFlightGroup<int, int>();
        int generation = 0;

        for (int round = 0; round < 500; round++)
        {
            int highestSeen = 0;

            // A burst of callers; each that becomes an owner "works" a moment, then completes its own flight.
            Task[] burst = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                SingleFlightGroup<int, int>.Flight flight = group.JoinOrStart(7, out bool isOwner);

                if (isOwner)
                {
                    int mine = Interlocked.Increment(ref generation);
                    await Task.Yield();
                    group.Complete(flight, mine);
                }

                int seen = await flight.Result;
                int current;
                while (seen > (current = Volatile.Read(ref highestSeen)) && Interlocked.CompareExchange(ref highestSeen, seen, current) != current)
                {
                }
            })).ToArray();

            await Task.WhenAll(burst);

            // Everything has finished, so a new caller must start new work and get a newer result.
            SingleFlightGroup<int, int>.Flight after = group.JoinOrStart(7, out bool afterIsOwner);
            Assert.True(afterIsOwner);
            group.Complete(after, Interlocked.Increment(ref generation));
            Assert.True(await after.Result > highestSeen);
            Assert.Equal(0, group.Count);
        }
    }
}
