# Architecture

## Boundaries

STRM Manager is a **modular monolith**: one deployable process, several internally
isolated modules. The rule that matters most:

```
Domain        knows nothing outside itself (no EF Core, no HTTP, no filesystem, no Cinemeta/FrostStream/ffprobe/Jellyfin/Docker)
Application   knows only abstractions (interfaces defined in Application/Domain)
Infrastructure implements those abstractions (EF Core, HTTP clients, ffprobe process, filesystem)
Presentation  calls Application through public handler interfaces, never Infrastructure directly
```

Dependencies always point inward. `test/StrmManager.ArchitectureTests` enforces this with
NetArchTest: Domain cannot depend on Application/Infrastructure/Presentation, Application
cannot depend on Infrastructure/Presentation, Presentation cannot depend on Infrastructure.

## Dependency diagram (current: Catalog only)

```
StrmManager.Api
  |-- Catalog.Presentation --> Catalog.Application --> Catalog.Domain
  |-- Catalog.Infrastructure --> Catalog.Application, Catalog.Domain
  |-- Common.Infrastructure, Common.Presentation

Common.Presentation --> Common.Application --> Common.Domain
Common.Infrastructure --> Common.Application, Common.Domain
```

Planned modules (`Providers`, `MediaProcessing`, `Scheduling`) will depend on
`Catalog.Domain` (they operate on `Episode`/`Movie`) and `Catalog.Application`
(repositories, `IUnitOfWork`), but `Catalog` will never depend back on them - see
[ADR-004](adr/ADR-004-provider-abstractions.md).

## Why no MediatR / no Outbox-Inbox

The reference architecture ([Evently](../../Evently)) uses MediatR with pipeline
behaviors, and an Outbox/Inbox pattern (backed by Quartz + a message bus) to dispatch
domain and integration events reliably across several modules and, eventually, several
processes. STRM Manager is a single process with a single writer (SQLite) and, at this
stage, no cross-module event consumers. Introducing that machinery now would be pure
ceremony - see [ADR-001](adr/ADR-001-modular-monolith.md).

Instead:

- Commands/Queries are plain records; handlers are resolved through
  `ICommandHandler<,>`/`IQueryHandler<,>` interfaces, registered by scanning each
  module's Application assembly (`AddHandlersFromAssembly`, in
  `StrmManager.Common.Application.Messaging`). No mediator indirection, no pipeline
  reflection beyond that one-time registration scan.
- `Entity.Raise()` / `IDomainEvent` exist in `Common.Domain` (so aggregates have
  somewhere to record what happened), but nothing dispatches them yet - there is no
  consumer. They will be wired up (in-process, synchronous, no Outbox) the moment a real
  consumer exists, per "don't add events without a real consumer."

## Episode state machine

```
Scheduled --[ReleaseAtUtc <= now]--> Pending --[StartSearching]--> Searching
                                                                       |
                                              +------------------------+------------------------+
                                              | no streams                                       | streams found
                                              v                                                   v
                                        Unavailable <--[no valid source]-- Validating --[technical error]--> Error
                                              ^                                |                    ^
                                              |                            [approved]                |
                                              |                                v                     |
                                              +----------[Retry, NextAttemptAtUtc]--- Pending <-------+
                                                                                |
                                                                            [approved]
                                                                                v
                                                                            Completed
```

Rules enforced by `Episode` (and mirrored by `Movie`) in
`StrmManager.Modules.Catalog.Domain`:

- `Unavailable` and `Error` are **distinct** terminal-but-recoverable states: `Unavailable`
  means "no technical failure, just nothing to show right now" (empty stream list, no
  candidate passed validation); `Error` means a technical failure (HTTP failure, timeout,
  ffprobe crash, filesystem error).
- `Completed` never goes back to `Pending`: `Retry()` only accepts `Unavailable`/`Error`
  as the source state.
- `MarkUnavailable`/`MarkError` refuse to fire once the episode is `Completed`.
- **Crash/restart safety**: `Searching` and `Validating` only exist as long as a single
  processing pipeline run is in flight; the entity is loaded as `Pending`, transitioned
  in memory, and only the *terminal* outcome (`Completed`/`Unavailable`/`Error`, or a
  no-op if the process dies mid-pipeline) is ever persisted. A crash between `Pending`
  and a terminal state simply leaves the row as `Pending` in the database - the next
  scheduler tick picks it up again. Nothing can get permanently stuck in `Searching`/
  `Validating`.

## Processing pipeline (planned - Providers/MediaProcessing/Scheduling)

```
Scheduler tick
  -> IEpisodeRepository.GetScheduledDueAsync / GetRetryableAsync
  -> Episode.StartSearching()
  -> IStreamProvider.GetStreamsAsync(episode)          [Providers]
  -> for each StreamCandidate (in preference order):
       Episode.StartValidating()
       IMediaValidator.ValidateAsync(candidate, reference)   [MediaProcessing, ffprobe]
       -> approved?  IStrmWriter.WriteEpisodeAsync(...) -> Episode.MarkCompleted()
       -> rejected?  record SourceAttempt, try next candidate
  -> no candidate approved -> Episode.MarkUnavailable(nextAttemptAtUtc: retry policy)
  -> technical failure at any step -> Episode.MarkError(reason)
```

Each step's `SourceAttempt` is persisted (without the full source URL, only what's needed
to answer "why wasn't this created" - see the domain model's `SourceAttempt.FailureReason`,
`DifferencePercentage`, etc.), independently of whether the overall episode ends up
`Completed` or `Unavailable`.

## Persistence

Single SQLite file, one `DbContext` per module (`CatalogDbContext` today). Modules do not
currently share a schema-qualification story the way Evently's Postgres schemas do -
SQLite has no schema concept - so table names are simply prefixed by convention
(`series`, `seasons`, `episodes`, `movies`, `source_attempts`, `strm_files`). See
[ADR-002](adr/ADR-002-sqlite.md).

## Incremental plan

1. ~~Foundation: solution, `Catalog` domain + persistence, health check, tests, Docker~~ (this delivery)
2. `Providers` module: `IMetadataProvider`/`CinemetaMetadataProvider`,
   `IStreamProvider`/`FrostStreamProvider`, with fixture-based tests (no live HTTP calls
   in CI).
3. `MediaProcessing` module: `IMediaValidator`/`FfprobeMediaValidator` (wrapping the
   validation rules already proven in the Python backend - duration tolerance,
   codec checks), `ISourceSelector`, `IStrmWriter` (atomic temp-file-then-rename writes,
   Jellyfin-compatible paths).
4. `Scheduling` module: `BackgroundService`s for `ReleaseProcessing` (Scheduled -> Pending)
   and `RetryProcessing` (Unavailable/Error -> Pending), bounded concurrency
   (`MaxConcurrentEpisodeProcessing`).
5. Season/Episode sync use cases (`RefreshSeriesMetadataCommand`) wiring `Catalog` to
   `Providers`.
6. Movie use cases mirroring the Episode pipeline.
7. Only after the above has test coverage equivalent to the current Python backend:
   decommission the PowerShell/Python system (never before).
