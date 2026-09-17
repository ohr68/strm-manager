# ADR-003: `BackgroundService`, not Hangfire

## Status

Accepted

## Context

Evently uses Quartz to run the Outbox/Inbox processors on an interval. The legacy
PowerShell/Python system relies on a human manually triggering "process next episode" /
"process season" - there is no automatic scheduler at all today.

STRM Manager needs a scheduler for two jobs: promoting `Scheduled` episodes/movies to
`Pending` once their release date passes, and retrying `Unavailable`/`Error` items whose
`NextAttemptAtUtc` has arrived. Both are simple polling loops over a small table.

## Decision

Use plain ASP.NET Core `BackgroundService` implementations (`ReleaseProcessing`,
`RetryProcessing` - to be added in the `Scheduling` module), each on its own timer,
querying `IEpisodeRepository`/`IMovieRepository` for due work. No Hangfire, no Quartz, no
persistent job store beyond the `Catalog` tables that already record `ReleaseAtUtc`/
`NextAttemptAtUtc`/`AttemptCount`.

Concurrency is bounded explicitly (a configurable `MaxConcurrentEpisodeProcessing`, via
`SemaphoreSlim` or `Channel<T>` - whichever turns out simplest once the processing
pipeline is implemented), not by a job-queue library's own concurrency model.

### DbContext access: `IServiceScopeFactory`, not `IDbContextFactory`

`CatalogDbContext` is registered `Scoped` (the default for `AddDbContext`). A
`BackgroundService` is instantiated once, as a singleton hosted service, so it cannot
inject `CatalogDbContext` directly - a scoped service cannot be constructor-injected into
a singleton.

We deliberately did **not** switch to `AddDbContextFactory`/`IDbContextFactory<CatalogDbContext>`
to solve this. Instead, every `BackgroundService` added in the `Scheduling` module must
follow this exact shape:

```
BackgroundService
    -> IServiceScopeFactory (injected in the constructor - this one IS singleton-safe)
    -> await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();  // per tick
    -> resolve an Application-layer command/query handler from scope.ServiceProvider
    -> the handler (Application layer) resolves its own repositories -> scoped CatalogDbContext
```

Concretely: `BackgroundService`s depend only on `IServiceScopeFactory` and call
`CreateAsyncScope()` once per unit of work (e.g., once per scheduler tick, or once per
episode processed within a tick). They **never** hold a `CatalogDbContext` field, and they
**never** write LINQ-to-EF queries themselves - they call an `ICommandHandler<,>`/
`IQueryHandler<,>` (or, for read-only polling, an `IEpisodeRepository`/`IMovieRepository`
method) resolved from that scope, exactly the same way `StrmManager.Api`'s Presentation
endpoints do. Persistence stays entirely behind the Application/Domain abstractions - a
`BackgroundService` is just another caller of the same use cases the HTTP API uses,
not a second path into the database.

This mirrors the one place a scope is already created manually in this codebase today:
`Program.cs`'s startup migration step (`using IServiceScope scope = app.Services.CreateScope();`).

Rejected alternative - `IDbContextFactory<CatalogDbContext>`: would also solve the
lifetime mismatch, and is EF Core's own recommended pattern for exactly this scenario.
Not adopted *yet* because it would add a second DbContext resolution path alongside the
existing `AddDbContext`/`Scoped` one (repositories still expect a scoped `CatalogDbContext`
injected via constructor), and nothing today needs it - revisit if/when a
`BackgroundService` needs a `DbContext` instance that outlives a single
`IServiceScopeFactory`-created scope, or needs to create many short contexts concurrently
without going through DI scopes.

## Consequences

- One fewer moving part (and one fewer NuGet dependency with its own storage
  requirements) than Hangfire, which wants its own tables/dashboard and is built for a
  scale of background-job traffic this project doesn't have.
- Restart safety comes from the domain model, not from the scheduler: since `Episode`/
  `Movie` never persist a "currently processing" state (see the Episode state machine in
  [architecture.md](../architecture.md)), a `BackgroundService` restart just means the
  next tick re-queries the same `Pending`/due rows - no separate job-recovery logic
  needed.
- If job volume or scheduling complexity ever outgrows a couple of timer loops, this ADR
  would need revisiting - not expected for a single-household media library.
- The `IServiceScopeFactory` requirement is a hard rule for the `Scheduling` module, not a
  suggestion: a `BackgroundService` that injects `CatalogDbContext` directly will fail at
  startup (singleton depending on scoped), and one that queries EF Core directly instead
  of going through Application handlers/repositories breaks the same Domain/Application/
  Infrastructure boundary that `StrmManager.ArchitectureTests` already enforces for the
  rest of the codebase - a review should reject either.
