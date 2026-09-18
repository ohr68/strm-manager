using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StrmManager.Modules.Catalog.Domain.Movies;
using StrmManager.Modules.Catalog.Domain.Shared;
using StrmManager.Modules.Catalog.Infrastructure.Database;

namespace StrmManager.Modules.Catalog.IntegrationTests.Processing;

internal sealed record SelectedSourceResponse(string Provider, string Name);

internal sealed record ProcessMovieResponse(
    Guid MovieId,
    string Status,
    int Attempts,
    SelectedSourceResponse? SelectedSource,
    string? StrmPath,
    string? Reason);

internal sealed record ProblemResponse(string? Title, string? Detail);

/// <summary>
/// Seeds a Movie directly in a chosen lifecycle state through the aggregate's own
/// transitions (never by poking properties), always stamped before the fake "now" the
/// processing tests use - a claim must move UpdatedAtUtc (the concurrency token) for a
/// lost race to be detectable, exactly as in production.
/// </summary>
internal static class ProcessMovieTestSupport
{
    public static readonly DateTime Now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime SeededAtUtc = Now.AddDays(-1);

    public static async Task<Guid> SeedMovieAsync(
        IServiceProvider services,
        MediaStatus status,
        DateTime? nextAttemptAtUtc = null,
        bool withoutImdbId = false)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        DateTime releaseAtUtc = status == MediaStatus.Scheduled ? Now.AddDays(30) : Now.AddDays(-30);
        Movie movie = Movie.Schedule(
            withoutImdbId
                ? new ExternalIds(null, $"tmdb{Guid.NewGuid():N}"[..12], null)
                : new ExternalIds($"tt{Guid.NewGuid():N}"[..14], null, null),
            "Process Movie Test",
            2025,
            TimeSpan.FromMinutes(100),
            releaseAtUtc,
            SeededAtUtc);

        switch (status)
        {
            case MediaStatus.Scheduled:
            case MediaStatus.Pending:
                break;
            case MediaStatus.Searching:
                movie.StartSearching(SeededAtUtc);
                break;
            case MediaStatus.Validating:
                movie.StartSearching(SeededAtUtc);
                movie.StartValidating(SeededAtUtc);
                break;
            case MediaStatus.Completed:
                movie.StartSearching(SeededAtUtc);
                movie.MarkCompleted(SeededAtUtc);
                break;
            case MediaStatus.Unavailable:
                movie.StartSearching(SeededAtUtc);
                movie.MarkUnavailable(SeededAtUtc, nextAttemptAtUtc, "no candidate passed validation");
                break;
            case MediaStatus.Error:
                movie.StartSearching(SeededAtUtc);
                movie.MarkError(SeededAtUtc, "stream provider unavailable", nextAttemptAtUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        Assert.Equal(status, movie.Status);

        context.Set<Movie>().Add(movie);
        await context.SaveChangesAsync();

        return movie.Id;
    }

    public static async Task<Movie> LoadMovieAsync(IServiceProvider services, Guid movieId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        CatalogDbContext context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // Straight from the database, never through IMovieRepository: verification reads
        // must not trip a test's InterceptingMovieRepository hook.
        return await context.Set<Movie>().AsNoTracking().SingleAsync(movie => movie.Id == movieId);
    }
}

/// <summary>
/// Wraps the real IMovieRepository so a test can run code at the exact point where a
/// process request has loaded its Movie but has not yet tried to claim it - the only way
/// to make a claim race deterministic instead of hoping two requests interleave.
/// </summary>
internal sealed class InterceptingMovieRepository(IMovieRepository inner, MovieLoadHook hook) : IMovieRepository
{
    public async Task<Movie?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Movie? movie = await inner.GetAsync(id, cancellationToken);

        if (movie is not null)
        {
            await hook.AfterLoadAsync(movie);
        }

        return movie;
    }

    public Task<Movie?> GetByImdbIdAsync(string imdbId, CancellationToken cancellationToken = default) =>
        inner.GetByImdbIdAsync(imdbId, cancellationToken);

    public Task<IReadOnlyList<Movie>> GetScheduledDueAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        inner.GetScheduledDueAsync(utcNow, cancellationToken);

    public Task<IReadOnlyList<Movie>> GetRetryableAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        inner.GetRetryableAsync(utcNow, cancellationToken);

    public void Insert(Movie movie) => inner.Insert(movie);
}

internal sealed class MovieLoadHook
{
    public Func<Movie, Task> AfterLoadAsync { get; set; } = _ => Task.CompletedTask;

    /// <summary>
    /// Hook that holds every caller until <paramref name="participants"/> of them have
    /// loaded the movie - guarantees they all observed the same pre-claim state before
    /// any of them saves. Fails loudly (not hangs) if the participants never arrive.
    /// </summary>
    public static Func<Movie, Task> Barrier(int participants)
    {
        int arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return async _ =>
        {
            if (Interlocked.Increment(ref arrived) >= participants)
            {
                release.TrySetResult();
            }

            Task finished = await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(15)));

            if (finished != release.Task)
            {
                throw new TimeoutException("The other participant never loaded the movie.");
            }
        };
    }
}

/// <summary>
/// Records what a process request does to persistence, in order. On the concurrency-aware
/// claim save it also captures the tracked Movie's in-memory status and - via a database
/// read taken before the save is issued - the status the database still held at that
/// instant. Every plain (non-claim) save is recorded as "plain-save". Shared by the
/// ProcessMovie test classes together with the "external:*" events their fakes enqueue, so
/// one ordered log shows claim-save -> provider -> validator -> saves.
/// </summary>
internal sealed class SpyUnitOfWork(
    CatalogDbContext context,
    System.Collections.Concurrent.ConcurrentQueue<string> events) : StrmManager.Modules.Catalog.Application.Abstractions.Data.IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        events.Enqueue("plain-save");
        return context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entry = context.ChangeTracker.Entries<Movie>().Single();
        events.Enqueue($"claim-save:in-memory={entry.Entity.Status}");

        var persisted = await entry.GetDatabaseValuesAsync(cancellationToken);
        events.Enqueue($"claim-save:persisted-before={persisted!.GetValue<MediaStatus>(nameof(Movie.Status))}");

        bool saved = await context.TrySaveChangesAsync(cancellationToken);
        events.Enqueue($"claim-save:result={saved}");

        return saved;
    }
}
