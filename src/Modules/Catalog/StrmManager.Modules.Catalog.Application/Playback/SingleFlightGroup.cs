namespace StrmManager.Modules.Catalog.Application.Playback;

/// <summary>
/// The single-flight registry: at most one in-flight <see cref="Flight"/> per key, and nothing else. It holds no
/// completed results - an entry exists only while its work is running, so it can never behave as a cache.
///
/// Ownership and removal are explicit:
///  - <see cref="JoinOrStart"/> either returns the flight already in progress (the caller is a waiter) or registers a new
///    one (the caller is its owner and must run the work and call <see cref="Complete"/> exactly once);
///  - <see cref="Complete"/> is the ONLY thing that removes an entry, it removes only the entry that IS that flight
///    (reference identity - an older flight can never remove a newer one for the same key), and it does so BEFORE the
///    result is published. So once a result is observable its entry is already gone, and a caller that arrives afterwards
///    starts a fresh flight instead of reusing the finished one;
///  - waiters never touch the registry.
/// One lock guards the dictionary; the work itself always runs outside it.
/// </summary>
internal sealed class SingleFlightGroup<TKey, TResult>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Flight> _flights = [];
    private readonly bool _runContinuationsAsynchronously;

    public SingleFlightGroup()
        : this(runContinuationsAsynchronously: true)
    {
    }

    /// <param name="runContinuationsAsynchronously">
    /// Always true in production, so publishing a result never runs a waiter's code inside <see cref="Complete"/>. A test
    /// passes false to make waiters run INSIDE the publish, which lets it observe exactly what a new caller would see at
    /// the instant a result becomes visible (that the finished entry is already gone).
    /// </param>
    internal SingleFlightGroup(bool runContinuationsAsynchronously) => _runContinuationsAsynchronously = runContinuationsAsynchronously;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _flights.Count;
            }
        }
    }

    public Flight JoinOrStart(TKey key, out bool isOwner)
    {
        lock (_gate)
        {
            if (_flights.TryGetValue(key, out Flight? existing))
            {
                isOwner = false;
                return existing;
            }

            var flight = new Flight(key, _runContinuationsAsynchronously);
            _flights.Add(key, flight);
            isOwner = true;
            return flight;
        }
    }

    /// <summary>Removes this exact flight's entry (if it is still the registered one), then publishes <paramref name="result"/> to its waiters.</summary>
    public void Complete(Flight flight, TResult result)
    {
        lock (_gate)
        {
            if (_flights.TryGetValue(flight.Key, out Flight? current) && ReferenceEquals(current, flight))
            {
                _flights.Remove(flight.Key);
            }
        }

        flight.Publish(result);
    }

    public sealed class Flight
    {
        // Continuations run asynchronously so publishing a result never runs a waiter's code inside Complete.
        private readonly TaskCompletionSource<TResult> _completion;

        internal Flight(TKey key, bool runContinuationsAsynchronously)
        {
            Key = key;
            _completion = new TaskCompletionSource<TResult>(
                runContinuationsAsynchronously ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
        }

        public TKey Key { get; }

        /// <summary>Completes with the shared result. Waiters cancel their own wait on it; nothing a waiter does can cancel or fault it.</summary>
        public Task<TResult> Result => _completion.Task;

        internal void Publish(TResult result) => _completion.TrySetResult(result);
    }
}
