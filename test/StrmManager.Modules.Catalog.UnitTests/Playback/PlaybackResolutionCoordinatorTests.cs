using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StrmManager.Modules.Catalog.Application.Playback;
using static StrmManager.Modules.Catalog.UnitTests.Playback.CoordinatorHarness;

namespace StrmManager.Modules.Catalog.UnitTests.Playback;

/// <summary>
/// Single-flight and bounded concurrency, proven without sleeping: resolutions block on TaskCompletionSources the test
/// completes, and queue-wait / budget deadlines pass only when the test advances a manual clock. Where a test must wait
/// for something asynchronous to be reached it polls for a state (with a failure guard) rather than assuming a delay.
/// </summary>
public class PlaybackResolutionCoordinatorTests
{
    private static Guid NewMovie() => Guid.NewGuid();

    // ================= SAME MOVIE =================

    [Fact(Timeout = 10000)]
    public async Task ConcurrentCallersForOneMovie_ShareExactlyOneResolution_AndTheSameResult()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();

        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 10);

        await EventuallyAsync(() => h.Probe.Calls == 1, "the shared resolution to start");
        Assert.Equal(1, h.Coordinator.InFlightCount);

        PlaybackResolutionResult.Resolved resolved = Resolved("shared");
        h.Probe.Complete(movie, resolved);
        PlaybackCoordinationResult[] results = await Task.WhenAll(callers);

        Assert.Equal(1, h.Probe.Calls); // exactly one underlying ResolveAsync
        Assert.All(results, r => Assert.Same(results[0], r)); // every waiter got the very same outcome
        Assert.Same(resolved, Assert.IsType<PlaybackCoordinationResult.Completed>(results[0]).Resolution); // passed through unchanged
    }

    [Fact(Timeout = 10000)]
    public async Task ConcurrentResolved_IsShared()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 4);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        h.Probe.Complete(movie, Resolved("a"));

        foreach (PlaybackCoordinationResult r in await Task.WhenAll(callers))
        {
            var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(Assert.IsType<PlaybackCoordinationResult.Completed>(r).Resolution);
            Assert.Equal(UrlFor("a"), resolved.Location.Reveal());
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ConcurrentNotFound_IsShared()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 4);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        h.Probe.Complete(movie, NotFound());

        Assert.All(await Task.WhenAll(callers), r => Assert.IsType<PlaybackResolutionResult.NotFound>(Assert.IsType<PlaybackCoordinationResult.Completed>(r).Resolution));
        Assert.Equal(1, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task ConcurrentUnavailable_IsShared()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 4);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        h.Probe.Complete(movie, Unavailable());

        Assert.All(await Task.WhenAll(callers), r => Assert.Equal(new PlaybackCoordinationResult.Completed(Unavailable()), r));
        Assert.Equal(1, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task UnexpectedException_BecomesTheSameStableFailureForEveryWaiter_WithoutItsText()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        h.Probe.Behavior = (_, _) => throw new InvalidOperationException($"{RawExceptionText} {UrlFor("in-message")}");

        // Callers join before the resolver has had a chance to throw.
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 5);
        PlaybackCoordinationResult[] results = await Task.WhenAll(callers); // none of them throws

        Assert.All(results, r => Assert.Equal(new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.Faulted), r));
        Assert.Contains("InvalidOperationException", h.Logger.AllText); // the type is kept for diagnosis...
        AssertNoSecrets(h.EverythingObservable(results)); // ...the message never is
        Assert.Equal(0, h.Coordinator.InFlightCount);
    }

    // ================= NO CACHE =================

    [Theory(Timeout = 10000)]
    [InlineData("resolved")]
    [InlineData("notfound")]
    [InlineData("unavailable")]
    public async Task AfterCompletion_TheNextCall_StartsANewResolution(string kind)
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        int generation = 0;
        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(kind switch
        {
            "resolved" => Resolved($"gen-{Interlocked.Increment(ref generation)}"),
            "notfound" => NotFound(),
            _ => Unavailable(),
        });

        PlaybackCoordinationResult first = await h.Coordinator.ResolveAsync(movie, default);
        PlaybackCoordinationResult second = await h.Coordinator.ResolveAsync(movie, default);

        Assert.Equal(2, h.Probe.Calls); // the first result was not reused, whatever it was
        Assert.Equal(2, h.Probe.InstancesCreated);

        if (kind == "resolved")
        {
            Assert.NotEqual(first, second); // the second call carries the second resolution's URL
            Assert.Equal(UrlFor("gen-2"), ((PlaybackResolutionResult.Resolved)((PlaybackCoordinationResult.Completed)second).Resolution).Location.Reveal());
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ABusyOutcome_LeavesNoStaleEntry_AndALaterCallRetries()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid running = NewMovie();
        Guid queued = NewMovie();
        Task<PlaybackCoordinationResult> holder = h.Coordinator.ResolveAsync(running, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "the slot to be taken");

        Task<PlaybackCoordinationResult> refused = h.Coordinator.ResolveAsync(queued, default);
        await EventuallyAsync(() => h.Time.PendingTimers >= 2, "the queue-wait timer to be armed");
        h.Time.Advance(h.QueueWait);

        Assert.IsType<PlaybackCoordinationResult.Busy>(await refused);
        Assert.Equal(1, h.Coordinator.InFlightCount); // only the running one - the refused flight left no entry

        h.Probe.Complete(running, Resolved("running"));
        await holder;
        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("retry"));

        PlaybackCoordinationResult retry = await h.Coordinator.ResolveAsync(queued, default);

        Assert.IsType<PlaybackCoordinationResult.Completed>(retry);
    }

    [Fact(Timeout = 10000)]
    public async Task AFaultedResolution_LeavesNoStaleEntry_AndALaterCallRetries()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        h.Probe.Behavior = (_, _) => throw new InvalidOperationException("boom");

        Assert.IsType<PlaybackCoordinationResult.Failed>(await h.Coordinator.ResolveAsync(movie, default));
        Assert.Equal(0, h.Coordinator.InFlightCount);

        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("recovered"));

        Assert.IsType<PlaybackCoordinationResult.Completed>(await h.Coordinator.ResolveAsync(movie, default));
        Assert.Equal(2, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task RepeatedCompletionRemovalRaces_NeverReuseACompletedResult()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 8);
        Guid movie = NewMovie();
        int generation = 0;
        h.Probe.Behavior = async (_, _) =>
        {
            int mine = Interlocked.Increment(ref generation);
            await Task.Yield(); // let callers interleave with completion and removal
            return Resolved($"{mine}");
        };

        static int GenerationOf(PlaybackCoordinationResult r) =>
            int.Parse(((PlaybackResolutionResult.Resolved)((PlaybackCoordinationResult.Completed)r).Resolution).SourceName, System.Globalization.CultureInfo.InvariantCulture);

        for (int round = 0; round < 300; round++)
        {
            PlaybackCoordinationResult[] burst = await Task.WhenAll(
                Enumerable.Range(0, 5).Select(_ => Task.Run(() => h.Coordinator.ResolveAsync(movie, default))));
            int highestInBurst = burst.Max(GenerationOf);

            // Every caller above has its result, so that resolution is over: a new caller must get NEW work.
            PlaybackCoordinationResult after = await h.Coordinator.ResolveAsync(movie, default);

            Assert.True(GenerationOf(after) > highestInBurst, $"round {round}: a finished result was reused");
            Assert.Equal(0, h.Coordinator.InFlightCount);
        }
    }

    // ================= WAITER CANCELLATION =================

    [Fact(Timeout = 10000)]
    public async Task ACancelledWaiter_GetsCancellation_WhileAnotherWaiterKeepsGoing()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        using var cancelA = new CancellationTokenSource();
        Task<PlaybackCoordinationResult> a = h.Coordinator.ResolveAsync(movie, cancelA.Token);
        Task<PlaybackCoordinationResult> b = h.Coordinator.ResolveAsync(movie, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        await cancelA.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        Assert.False(b.IsCompleted);
        Assert.Equal(1, h.Coordinator.InFlightCount); // the shared resolution is still running

        h.Probe.Complete(movie, Resolved("for-b"));

        Assert.IsType<PlaybackCoordinationResult.Completed>(await b);
        Assert.Equal(1, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task ACancelledWaiter_DoesNotCancelTheUnderlyingResolverToken()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        using var cancelA = new CancellationTokenSource();
        Task<PlaybackCoordinationResult> a = h.Coordinator.ResolveAsync(movie, cancelA.Token);
        Task<PlaybackCoordinationResult> b = h.Coordinator.ResolveAsync(movie, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        CancellationToken resolverToken = h.Probe.TokenOfCallFor(movie);
        await cancelA.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);

        Assert.False(resolverToken.IsCancellationRequested);
        Assert.NotEqual(cancelA.Token, resolverToken); // it is the coordinator's own token, not anybody's request token

        h.Probe.Complete(movie, Resolved("x"));
        await b;
    }

    [Fact(Timeout = 10000)]
    public async Task TheFirstCaller_DoesNotOwnTheSharedWorksLifetime()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        using var cancelFirst = new CancellationTokenSource();
        Task<PlaybackCoordinationResult> first = h.Coordinator.ResolveAsync(movie, cancelFirst.Token); // it starts the flight
        Task<PlaybackCoordinationResult> second = h.Coordinator.ResolveAsync(movie, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        await cancelFirst.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        Task<PlaybackCoordinationResult> late = h.Coordinator.ResolveAsync(movie, default); // still joins the same flight
        h.Probe.Complete(movie, Resolved("shared"));

        PlaybackCoordinationResult[] results = await Task.WhenAll(second, late);
        Assert.Same(results[0], results[1]);
        Assert.Equal(1, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task WhenEveryWaiterCancels_TheSharedWorkContinues_AndFinishesCleanly()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        using var cancelAll = new CancellationTokenSource();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 3, cancelAll.Token);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");
        CancellationToken resolverToken = h.Probe.TokenOfCallFor(movie);

        await cancelAll.CancelAsync();
        foreach (Task<PlaybackCoordinationResult> caller in callers)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        }

        // Nobody is waiting, and the resolution is still running (no reference-count cancellation).
        Assert.False(resolverToken.IsCancellationRequested);
        Assert.Equal(1, h.Coordinator.InFlightCount);
        Assert.Equal(1, h.Probe.Running);

        h.Probe.Complete(movie, Resolved("orphaned"));
        await EventuallyAsync(() => h.Coordinator.InFlightCount == 0, "the orphaned flight to unwind");

        // ...and the next call starts fresh work.
        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("next"));
        await h.Coordinator.ResolveAsync(movie, default);
        Assert.Equal(2, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task ACallerThatIsAlreadyCancelled_StartsNothing()
    {
        using var h = new CoordinatorHarness();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Coordinator.ResolveAsync(NewMovie(), cancelled.Token));

        Assert.Equal(0, h.Probe.Calls);
        Assert.Equal(0, h.Coordinator.InFlightCount);
    }

    // ================= GLOBAL CONCURRENCY =================

    [Fact(Timeout = 10000)]
    public async Task DifferentMovies_NeverExceedTheGlobalLimit()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 2);
        Guid[] movies = Enumerable.Range(0, 4).Select(_ => NewMovie()).ToArray();
        Task<PlaybackCoordinationResult>[] calls = movies.Select(m => h.Coordinator.ResolveAsync(m, default)).ToArray();

        await EventuallyAsync(() => h.Probe.Calls == 2, "two resolutions to start");
        Assert.Equal(2, h.Probe.Running);
        Assert.Equal(4, h.Coordinator.InFlightCount); // two executing, two queued - the queued ones hold no slot

        // Complete whichever two started; the others must then start, one per freed slot.
        Guid[] firstTwo = h.Probe.MoviesCalled.ToArray();
        h.Probe.Complete(firstTwo[0], Resolved("1"));
        await EventuallyAsync(() => h.Probe.Calls == 3, "the third resolution to take the freed slot");
        Assert.True(h.Probe.Running <= 2);

        h.Probe.Complete(firstTwo[1], Resolved("2"));
        await EventuallyAsync(() => h.Probe.Calls == 4, "the fourth resolution to take the freed slot");

        foreach (Guid next in h.Probe.MoviesCalled.Skip(2))
        {
            h.Probe.Complete(next, Resolved("later"));
        }

        Assert.All(await Task.WhenAll(calls), r => Assert.IsType<PlaybackCoordinationResult.Completed>(r));
        Assert.Equal(2, h.Probe.MaxRunning); // it never went above the limit, and did reach it
    }

    [Fact(Timeout = 10000)]
    public async Task ASecondWaiterForARunningMovie_ConsumesNoExtraSlot()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 2);
        Guid a = NewMovie();
        Guid b = NewMovie();

        Task<PlaybackCoordinationResult> a1 = h.Coordinator.ResolveAsync(a, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "movie A to start");
        Task<PlaybackCoordinationResult> a2 = h.Coordinator.ResolveAsync(a, default); // joins A
        Task<PlaybackCoordinationResult> b1 = h.Coordinator.ResolveAsync(b, default);

        // If a2 had taken a slot, b would be queued behind a limit of 2. It is not: it runs alongside A.
        await EventuallyAsync(() => h.Probe.Calls == 2, "movie B to start alongside A");
        Assert.Equal(2, h.Probe.Running);
        Assert.Equal(new[] { a, b }.Order(), h.Probe.MoviesCalled.Order());

        h.Probe.Complete(a, Resolved("a"));
        h.Probe.Complete(b, Resolved("b"));
        await Task.WhenAll(a1, a2, b1);
        Assert.Equal(2, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task AQueuedMovie_StartsWhenASlotIsReleased()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid first = NewMovie();
        Guid second = NewMovie();
        Task<PlaybackCoordinationResult> firstCall = h.Coordinator.ResolveAsync(first, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "the first resolution to start");

        Task<PlaybackCoordinationResult> secondCall = h.Coordinator.ResolveAsync(second, default);
        await EventuallyAsync(() => h.Time.PendingTimers >= 2, "the second to be waiting for a slot");
        Assert.Equal(1, h.Probe.Calls); // it has not started

        h.Probe.Complete(first, Resolved("first"));
        await EventuallyAsync(() => h.Probe.Calls == 2, "the queued resolution to start");
        h.Probe.Complete(second, Resolved("second"));

        Assert.IsType<PlaybackCoordinationResult.Completed>(await firstCall);
        Assert.IsType<PlaybackCoordinationResult.Completed>(await secondCall);
    }

    [Fact(Timeout = 10000)]
    public async Task QueueTimeout_ReturnsBusyToEveryWaiter_AndTheResolverIsNeverCalledForThatMovie()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid running = NewMovie();
        Guid starved = NewMovie();
        Task<PlaybackCoordinationResult> holder = h.Coordinator.ResolveAsync(running, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "the slot to be taken");

        Task<PlaybackCoordinationResult>[] refused = h.StartCallers(starved, 3); // three waiters share the one queued flight
        await EventuallyAsync(() => h.Time.PendingTimers >= 2, "the queue-wait timer to be armed");
        h.Time.Advance(h.QueueWait);

        PlaybackCoordinationResult[] results = await Task.WhenAll(refused);
        Assert.All(results, r => Assert.IsType<PlaybackCoordinationResult.Busy>(r));
        Assert.DoesNotContain(starved, h.Probe.MoviesCalled); // never called
        Assert.Equal(1, h.Probe.Calls);

        h.Probe.Complete(running, Resolved("running"));
        await holder;
    }

    [Fact(Timeout = 10000)]
    public async Task ABusyEntryIsRemoved_SoALaterRequestCanRetryAndSucceed()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid running = NewMovie();
        Guid retried = NewMovie();
        Task<PlaybackCoordinationResult> holder = h.Coordinator.ResolveAsync(running, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "the slot to be taken");
        Task<PlaybackCoordinationResult> refused = h.Coordinator.ResolveAsync(retried, default);
        await EventuallyAsync(() => h.Time.PendingTimers >= 2, "the queue-wait timer to be armed");
        h.Time.Advance(h.QueueWait);
        Assert.IsType<PlaybackCoordinationResult.Busy>(await refused);

        h.Probe.Complete(running, Resolved("running"));
        await holder;
        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("second-try"));

        PlaybackCoordinationResult retry = await h.Coordinator.ResolveAsync(retried, default);

        Assert.IsType<PlaybackCoordinationResult.Completed>(retry); // no stale Busy was served
        Assert.Contains(retried, h.Probe.MoviesCalled);
    }

    [Fact(Timeout = 10000)]
    public async Task AZeroQueueWait_MeansNeverQueue_ButAFreeSlotIsStillUsed()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1, queueWait: TimeSpan.Zero);
        Guid running = NewMovie();
        Task<PlaybackCoordinationResult> holder = h.Coordinator.ResolveAsync(running, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "a free slot to be used");

        PlaybackCoordinationResult refused = await h.Coordinator.ResolveAsync(NewMovie(), default); // no timers, no advance needed

        Assert.IsType<PlaybackCoordinationResult.Busy>(refused);
        h.Probe.Complete(running, Resolved("running"));
        await holder;
    }

    // ================= EXECUTION BUDGET =================

    [Fact(Timeout = 10000)]
    public async Task WhenTheBudgetExpires_TheResolverTokenIsCancelled_AndCallersGetAStableFailure()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 3);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");
        CancellationToken resolverToken = h.Probe.TokenOfCallFor(movie);
        Assert.False(resolverToken.IsCancellationRequested);

        h.Time.Advance(h.Budget);

        PlaybackCoordinationResult[] results = await Task.WhenAll(callers);
        Assert.True(resolverToken.IsCancellationRequested); // the budget reached the underlying resolver
        Assert.All(results, r => Assert.Equal(new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.BudgetExceeded), r));
        AssertNoSecrets(h.EverythingObservable(results));
        Assert.DoesNotContain("OperationCanceledException", string.Join('\n', results.Select(r => r.ToString())));
    }

    [Fact(Timeout = 10000)]
    public async Task ABudgetTimeout_LeavesNoStaleEntry_AndALaterCallResolvesNormally()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult> stuck = h.Coordinator.ResolveAsync(movie, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        h.Time.Advance(h.Budget);
        Assert.IsType<PlaybackCoordinationResult.Failed>(await stuck);
        Assert.Equal(0, h.Coordinator.InFlightCount);

        // The slot came back too (limit 1), so a new resolution is admitted and runs to a normal result.
        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("after-timeout"));
        PlaybackCoordinationResult next = await h.Coordinator.ResolveAsync(movie, default);

        var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(Assert.IsType<PlaybackCoordinationResult.Completed>(next).Resolution);
        Assert.Equal(UrlFor("after-timeout"), resolved.Location.Reveal());
    }

    [Fact(Timeout = 10000)]
    public async Task TheBudgetOnlyStartsAtAdmission_NotWhileQueued()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid first = NewMovie();
        Guid second = NewMovie();
        Task<PlaybackCoordinationResult> firstCall = h.Coordinator.ResolveAsync(first, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");
        Task<PlaybackCoordinationResult> secondCall = h.Coordinator.ResolveAsync(second, default);
        await EventuallyAsync(() => h.Time.PendingTimers >= 2, "queued");

        h.Time.Advance(h.QueueWait - TimeSpan.FromSeconds(1)); // queued for just under the wait, far under the budget
        h.Probe.Complete(first, Resolved("first"));
        await EventuallyAsync(() => h.Probe.Calls == 2, "the queued resolution to be admitted");

        Assert.False(h.Probe.TokenOfCallFor(second).IsCancellationRequested);
        h.Probe.Complete(second, Resolved("second"));
        Assert.IsType<PlaybackCoordinationResult.Completed>(await secondCall);
        await firstCall;
    }

    // ================= SHUTDOWN =================

    [Fact(Timeout = 10000)]
    public async Task Shutdown_CancelsTheSharedWork_WithAGenericOutcomeAndNoUrlInLogsOrResults()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 3);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");
        CancellationToken resolverToken = h.Probe.TokenOfCallFor(movie);

        await h.Shutdown.CancelAsync();

        PlaybackCoordinationResult[] results = await Task.WhenAll(callers);
        Assert.True(resolverToken.IsCancellationRequested);
        Assert.All(results, r => Assert.Equal(new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown), r));
        AssertNoSecrets(h.EverythingObservable(results));
        Assert.Equal(0, h.Coordinator.InFlightCount); // nothing remains once the work has unwound
    }

    [Fact(Timeout = 10000)]
    public async Task Shutdown_ReleasesAResolutionThatWasStillQueued_AsShuttingDownNotBusy()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        Guid running = NewMovie();
        Guid queued = NewMovie();
        Task<PlaybackCoordinationResult> holder = h.Coordinator.ResolveAsync(running, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "the slot to be taken");
        Task<PlaybackCoordinationResult> waiting = h.Coordinator.ResolveAsync(queued, default);
        await EventuallyAsync(() => h.Time.PendingTimers >= 2, "the second to be queued");

        await h.Shutdown.CancelAsync();

        Assert.Equal(new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown), await waiting);
        Assert.Equal(new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown), await holder);
        Assert.DoesNotContain(queued, h.Probe.MoviesCalled);
        Assert.Equal(0, h.Coordinator.InFlightCount);
    }

    [Fact(Timeout = 10000)]
    public async Task ACallAfterShutdown_IsRefusedWithoutCallingTheResolver()
    {
        using var h = new CoordinatorHarness();
        await h.Shutdown.CancelAsync();

        PlaybackCoordinationResult result = await h.Coordinator.ResolveAsync(NewMovie(), default);

        Assert.Equal(new PlaybackCoordinationResult.Failed(PlaybackCoordinationFailure.ShuttingDown), result);
        Assert.Equal(0, h.Probe.Calls);
        Assert.Equal(0, h.Coordinator.InFlightCount);
    }

    // ================= SCOPE / DI =================

    [Fact(Timeout = 10000)]
    public async Task SharedWork_RunsInAScopeItOwns_WhichIsDisposedWhenTheWorkEnds()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult> call = h.Coordinator.ResolveAsync(movie, default);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        ProbeResolver resolver = Assert.Single(h.Probe.Instances);
        Assert.NotSame(h.Root, resolver.ScopeProvider); // created from a scope, not from the root provider
        Assert.False(resolver.Disposed); // the scope is alive while the work runs

        h.Probe.Complete(movie, Resolved("x"));
        await call;

        Assert.True(resolver.Disposed); // and disposed with it
    }

    [Fact(Timeout = 10000)]
    public async Task AllWaitersOfOneSharedResolution_UseOneResolverAndOneScope()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 8);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");

        h.Probe.Complete(movie, Resolved("x"));
        await Task.WhenAll(callers);

        Assert.Equal(1, h.Probe.InstancesCreated);
        Assert.Equal(1, h.Probe.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task ALaterNonOverlappingResolution_GetsANewResolverInANewScope()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("x"));

        await h.Coordinator.ResolveAsync(movie, default);
        await h.Coordinator.ResolveAsync(movie, default);

        ProbeResolver[] instances = h.Probe.Instances.ToArray();
        Assert.Equal(2, instances.Length);
        Assert.NotSame(instances[0], instances[1]);
        Assert.NotSame(instances[0].ScopeProvider, instances[1].ScopeProvider);
        Assert.All(instances, i => Assert.True(i.Disposed));
    }

    [Fact]
    public void TheCoordinatorCanBeASingleton_BecauseItCapturesNoScopedDependency()
    {
        // With scope validation on, registering a singleton that captured a scoped service fails at build/resolve time.
        var services = new ServiceCollection();
        var probe = new ResolverProbe { Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("x")) };
        services.AddScoped<IPlaybackResolver>(scope => probe.Create(scope));
        services.AddSingleton<IPlaybackResolutionCoordinator>(sp => new PlaybackResolutionCoordinator(
            sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new PlaybackResolutionOptions()), TimeProvider.System,
            NullLogger<PlaybackResolutionCoordinator>.Instance, CancellationToken.None));

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        IPlaybackResolutionCoordinator fromRoot = provider.GetRequiredService<IPlaybackResolutionCoordinator>();
        using IServiceScope requestA = provider.CreateScope();
        using IServiceScope requestB = provider.CreateScope();

        Assert.Same(fromRoot, requestA.ServiceProvider.GetRequiredService<IPlaybackResolutionCoordinator>()); // one instance for every request
        Assert.Same(fromRoot, requestB.ServiceProvider.GetRequiredService<IPlaybackResolutionCoordinator>());
    }

    [Fact(Timeout = 10000)]
    public async Task TheCoordinatorHoldsNoResolverScopeOrRequestState()
    {
        // Structural guard: nothing scoped or per-request is stored on the coordinator; the registry key is the movie Guid alone.
        FieldInfo[] fields = typeof(PlaybackResolutionCoordinator).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.DoesNotContain(fields, f => typeof(IPlaybackResolver).IsAssignableFrom(f.FieldType));
        Assert.DoesNotContain(fields, f => f.FieldType == typeof(IServiceProvider) || f.FieldType == typeof(IServiceScope));
        FieldInfo flights = Assert.Single(fields, f => f.Name == "_flights");
        Assert.Equal([typeof(Guid), typeof(PlaybackCoordinationResult)], flights.FieldType.GetGenericArguments());

        await Task.CompletedTask;
    }

    // ================= URL SAFETY =================

    [Fact(Timeout = 10000)]
    public async Task TheUrl_IsOnlyReachableThroughRevealOnASuccessfulResult()
    {
        using var h = new CoordinatorHarness();
        Guid movie = NewMovie();
        Task<PlaybackCoordinationResult>[] callers = h.StartCallers(movie, 4);
        await EventuallyAsync(() => h.Probe.Calls == 1, "start");
        h.Probe.Complete(movie, Resolved("secret-source"));
        PlaybackCoordinationResult[] results = await Task.WhenAll(callers);

        var resolved = Assert.IsType<PlaybackResolutionResult.Resolved>(Assert.IsType<PlaybackCoordinationResult.Completed>(results[0]).Resolution);
        Assert.Equal(UrlFor("secret-source"), resolved.Location.Reveal()); // the one intended path

        string observable = h.EverythingObservable(results);
        AssertNoSecrets(observable);
        Assert.NotEmpty(h.Logger.Entries); // it does log, just never the URL
        foreach (PlaybackCoordinationResult r in results)
        {
            AssertNoSecrets($"{r}");
            AssertNoSecrets(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", r));
        }
    }

    [Fact(Timeout = 10000)]
    public async Task EveryOutcomeKind_LogsAndReportsWithoutAnyUrl()
    {
        using var h = new CoordinatorHarness(maxConcurrent: 1);
        var results = new List<PlaybackCoordinationResult>();

        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Resolved("ok"));
        results.Add(await h.Coordinator.ResolveAsync(NewMovie(), default));

        h.Probe.Behavior = (_, _) => Task.FromResult<PlaybackResolutionResult>(Unavailable());
        results.Add(await h.Coordinator.ResolveAsync(NewMovie(), default));

        h.Probe.Behavior = (_, _) => throw new InvalidOperationException($"{RawExceptionText} {UrlFor("thrown")}");
        results.Add(await h.Coordinator.ResolveAsync(NewMovie(), default));

        AssertNoSecrets(h.EverythingObservable([.. results]));
    }

    // ================= OPTIONS =================

    [Fact]
    public void Options_DefaultsAreValid_SoAnExistingDeploymentNeedsNoConfiguration()
    {
        var options = new PlaybackResolutionOptions();

        Assert.Empty(Validate(options));
        Assert.Equal(2, options.MaxConcurrentResolutions);
        Assert.Equal(TimeSpan.FromSeconds(5), options.QueueWait);
        Assert.Equal(TimeSpan.FromSeconds(60), options.ResolutionBudget);
    }

    [Theory]
    [InlineData(0, "00:00:05", "00:01:00")]   // no slots at all
    [InlineData(21, "00:00:05", "00:01:00")]  // absurdly many
    [InlineData(2, "-00:00:01", "00:01:00")]  // negative wait
    [InlineData(2, "00:02:00", "00:01:00")]   // wait beyond the maximum
    [InlineData(2, "00:00:05", "00:00:01")]   // budget too small to run anything
    [InlineData(2, "00:00:05", "00:30:00")]   // budget beyond the maximum
    public void Options_InvalidValuesAreRejected(int max, string queueWait, string budget)
    {
        var options = new PlaybackResolutionOptions
        {
            MaxConcurrentResolutions = max,
            QueueWait = TimeSpan.Parse(queueWait, System.Globalization.CultureInfo.InvariantCulture),
            ResolutionBudget = TimeSpan.Parse(budget, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.NotEmpty(Validate(options));
    }

    [Fact]
    public void Options_HaveNoCacheOrFreshnessSettings()
    {
        string[] names = typeof(PlaybackResolutionOptions).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["MaxConcurrentResolutions", "QueueWait", "ResolutionBudget"], names); // no TTL, reuse window or base URL
    }

    private static List<ValidationResult> Validate(PlaybackResolutionOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
