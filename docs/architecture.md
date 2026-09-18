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
  |-- Common.Presentation

Common.Presentation --> Common.Application --> Common.Domain
```

`IMetadataProvider` (Phase 2) lives inside `Catalog.Application/Metadata/`, and its
Cinemeta implementation inside `Catalog.Infrastructure/Metadata/Cinemeta/` - **not** a
separate `Providers` project, despite ADR-004 originally sketching one. See
[ADR-007](adr/ADR-007-metadata-provider-and-catalog-synchronization.md) for why the
smaller structure was chosen once there was something real to build. `MediaProcessing`/
`Scheduling` (not implemented yet) will depend on `Catalog.Domain` (they operate on
`Episode`/`Movie`) and `Catalog.Application` (repositories, `IUnitOfWork`), but `Catalog`
will never depend back on them.

There is no `Common.Infrastructure` project. It existed in the initial foundation as an
empty shell (mirroring Evently's `Evently.Common.Infrastructure`, which holds shared
Outbox/Inbox/EventBus/Auth/Caching plumbing this project doesn't have), with zero source
files and two unused package references. Phase 1.1 removed it rather than keep an empty
project around "just in case" - nothing here currently needs cross-module shared
infrastructure that isn't already covered by `Common.Domain`/`Common.Application`/
`Common.Presentation`. Reintroduce it the day a second module needs to share concrete
infrastructure code (e.g. a common HTTP resilience policy setup once `Providers` exists)
rather than before.

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

**Validation** follows the same no-framework spirit: endpoints resolve `IValidator<TCommand>`
and the command's `ICommandHandler<,>` explicitly via DI (both are ordinary parameters on
the Minimal API delegate - no hidden pipeline), and call
`handler.HandleValidated(command, validator, cancellationToken)` - a small extension
method (`StrmManager.Common.Application.Messaging.ValidatedCommandHandlerExtensions`)
that runs the validator, short-circuits to a `Result.Failure` on invalid input, and
otherwise calls the handler. It replaces what would otherwise be a repeated 6-line
"validate, check `IsValid`, map to `Result`, return early" block copy-pasted into every
command endpoint - not a decorator, not a registered pipeline step, just a helper method.
A MediatR-style automatic pipeline was considered and rejected: with the current number
of command endpoints (one), the explicit two-parameter call site is easier to read and
debug than a registered pipeline behavior would be, and it costs nothing to add one more
call site as new commands appear.

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

**On the `Episode`/`Movie` duplication**: `Movie`'s state machine is currently a
line-for-line copy of `Episode`'s (same seven transition methods, same guards). This is
known and deliberately left alone for now - the duplication is small, and it's not yet
clear the two aggregates will stay identical once `MediaProcessing`/`Scheduling` exist
(e.g. movies have no `SeasonNumber`/`EpisodeNumber` concept and may end up with different
retry policy shapes). Extracting a shared base/state-machine abstraction before a second
real divergence shows up would be guessing at the wrong boundary. Revisit once
`Scheduling` is implemented and it's clear whether the two really do stay in lockstep.

## Catalog synchronization (implemented, Phase 2)

```
External series id (IMDb)
  -> IMetadataProvider.GetSeriesAsync(externalId)      [CinemetaMetadataProvider]
  -> SeriesMetadata (provider-neutral)
  -> Series.UpdateMetadata(title, originalTitle, year, status, utcNow)
  -> CatalogSynchronizer.SynchronizeAsync(seriesId, episodes, utcNow, ct)
       for each season group:
         get-or-create Season
         for each episode (skip if no ReleaseAtUtc):
           match by ExternalId, fall back to (SeasonNumber, EpisodeNumber)
           new -> Episode.Schedule(...)
           existing -> Episode.UpdateMetadata(...) + TryBecomeEligible(utcNow)
  -> IUnitOfWork.SaveChangesAsync()   (single call, after the HTTP work is done)
```

Driven by `POST /api/series` (first sync, inline, best-effort) and
`POST /api/series/{id}/refresh` (explicit resync) - both call the same
`CatalogSynchronizer`, so idempotency/matching/status-preservation rules exist in one
place. Full reasoning, including why this isn't a separate `Providers` module, in
[ADR-007](adr/ADR-007-metadata-provider-and-catalog-synchronization.md).

## Processing pipeline (planned - MediaProcessing/Scheduling)

```
Scheduler tick
  -> IEpisodeRepository.GetScheduledDueAsync / GetRetryableAsync
  -> Episode.StartSearching()
  -> IStreamProvider.GetStreamsAsync(episode)          [not implemented yet]
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

Migrations so far: `InitialCreate` (Phase 1), `AddCatalogForeignKeys` (Phase 1.1, see
[ADR-005](adr/ADR-005-catalog-relational-integrity.md)), `AddEpisodeExternalIdUniqueIndex`
(Phase 2, see [ADR-007](adr/ADR-007-metadata-provider-and-catalog-synchronization.md)).

## Incremental plan

1. ~~Foundation: solution, `Catalog` domain + persistence, health check, tests, Docker~~ (Phase 1)
2. ~~Phase 1.1 hardening: FK relationships, global exception handling, `BackgroundService`
   scope decision, cleanup~~
3. ~~Metadata: `IMetadataProvider`/`CinemetaMetadataProvider`, catalog synchronization
   (`Series`/`Season`/`Episode` from an IMDb id), with fixture-based tests (no live HTTP
   calls in CI)~~ (Phase 2 - this delivery)
4. `MediaProcessing`: `IStreamProvider`/`FrostStreamProvider`,
   `IMediaValidator`/`FfprobeMediaValidator` (wrapping the validation rules already
   proven in the Python backend - duration tolerance, codec checks), `ISourceSelector`,
   `IStrmWriter` (atomic temp-file-then-rename writes, Jellyfin-compatible paths).
5. `Scheduling` module: `BackgroundService`s for `ReleaseProcessing` (Scheduled -> Pending)
   and `RetryProcessing` (Unavailable/Error -> Pending), bounded concurrency
   (`MaxConcurrentEpisodeProcessing`) - see [ADR-003](adr/ADR-003-background-service-scheduler.md)
   for the `IServiceScopeFactory` pattern this must follow.
6. Movie use cases mirroring the Episode metadata-sync and processing pipeline.
7. Only after the above has test coverage equivalent to the current Python backend:
   decommission the PowerShell/Python system (never before).
