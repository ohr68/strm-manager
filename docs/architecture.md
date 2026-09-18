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

## Dependency diagram (current: Catalog + MediaProcessing + Scheduling)

```
StrmManager.Api
  |-- Catalog.Presentation --> Catalog.Application --> Catalog.Domain
  |-- Catalog.Infrastructure --> Catalog.Application, Catalog.Domain
  |-- MediaProcessing.Infrastructure --> MediaProcessing.Application
  |-- Scheduling.Infrastructure --> Catalog.Application, Catalog.Domain
  |-- Common.Presentation

Catalog.Application --> MediaProcessing.Application, Catalog.Domain
MediaProcessing.Application --> Catalog.Domain   (SourceAttemptResult only)
Common.Presentation --> Common.Application --> Common.Domain
```

`IMetadataProvider` (Phase 2) lives inside `Catalog.Application/Metadata/`, and its
Cinemeta implementation inside `Catalog.Infrastructure/Metadata/Cinemeta/` - **not** a
separate `Providers` project, despite ADR-004 originally sketching one. See
[ADR-007](adr/ADR-007-metadata-provider-and-catalog-synchronization.md) for why the
smaller structure was chosen once there was something real to build.

`MediaProcessing` (Phase 3) similarly gets only two projects - `Application` and
`Infrastructure`, no `Domain`/`Presentation` - and the processing *orchestrator*
(`ProcessEpisodeCommandHandler`) lives in `Catalog.Application`, not in
`MediaProcessing`, since it drives `Episode`'s state machine directly. See
[ADR-008](adr/ADR-008-media-processing-module-boundary.md) for the full reasoning.

`Scheduling` (Phase 4) goes one step further: a single `Scheduling.Infrastructure`
project, no `Scheduling.Application` at all. Its two `BackgroundService`s
(`CatalogMaintenanceWorker`, `EpisodeProcessingWorker`) are thin callers - all the actual
autonomous-behavior logic lives in `Catalog.Application` (`RunCatalogMaintenanceCommand`,
`ProcessEpisodeCommandHandler`), the same way an HTTP endpoint is a thin caller into
Application. See [ADR-012](adr/ADR-012-scheduling-module-architecture.md).
`GET /api/status` needs to know whether the scheduler is enabled without
`Catalog.Application` depending on `Scheduling` - solved with a small Dependency
Inversion abstraction (`ISchedulerStatusProvider`, defined in `Catalog.Application`,
implemented in `Scheduling.Infrastructure`), the same shape `IMetadataProvider`/
`IStreamProvider` already use.

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
  ffprobe crash, filesystem error). `MarkError`'s `nextAttemptAtUtc` (Phase 4) is the sole
  retryability signal for `Error` - present for transient/technical failures (stream
  provider outages), absent for potentially persistent ones (a `.strm` write failure) -
  see [ADR-013](adr/ADR-013-claim-and-recovery-semantics.md).
- `Completed` never goes back to `Pending`: `Retry()` only accepts `Unavailable`/`Error`
  as the source state.
- `MarkUnavailable`/`MarkError` refuse to fire once the episode is `Completed`.
- **Claim and crash/restart safety (Phase 4, see** [ADR-013](adr/ADR-013-claim-and-recovery-semantics.md)
  **- supersedes ADR-011's Phase 3 design)**: `Searching` and `Validating` ARE now
  persisted, immediately, because `EpisodeProcessingWorker` and manual API calls can race
  for the same episode - `UpdatedAtUtc` doubles as an EF Core optimistic-concurrency
  token, so only one of two overlapping claims can win the save; the loser gets
  `EpisodeErrors.AlreadyBeingProcessed` (409) before any FrostStream/ffprobe work starts.
  A run genuinely interrupted (crash, restart, cancelled shutdown) leaves the row stuck
  in `Searching`/`Validating` until `RunCatalogMaintenanceCommandHandler`'s stale-
  processing sweep notices (`UpdatedAtUtc` older than `ProcessingOptions.StaleProcessingThreshold`,
  15 minutes by default) and calls the new `Episode.RecoverInterruptedProcessing(utcNow)`
  - back to `Pending`, without touching `AttemptCount`/`LastError` (an interrupted run
  reached no outcome, so it isn't recorded as one).

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

## Processing pipeline (implemented, Phase 3; claim added Phase 4)

```
POST /api/episodes/{id}/process   [called by a human, or by EpisodeProcessingWorker]
  -> already Completed?  return idempotently, no side effects
  -> Episode.StartSearching()
  -> TrySaveChangesAsync()   <-- the claim: persisted before any slow work begins
       lost (concurrency conflict) -> EpisodeErrors.AlreadyBeingProcessed (409)
  -> IStreamProvider.GetEpisodeStreamsAsync(reference)  [FrostStreamProvider]
       failure -> MarkError(retryable?)   |   empty -> MarkUnavailable
  -> Episode.StartValidating()
  -> SaveChangesAsync()   (no concurrency risk here - the claim already excluded other callers)
  -> for each StreamCandidate (provider's order):
       EpisodeIdentityValidator.Evaluate(candidateText, season, episode)  [per candidate]
       not Compatible -> record SourceAttempt(Rejected), try next candidate
       Compatible -> IMediaValidator.ValidateAsync(candidate, reference)  [FfprobeMediaValidator]
                     -> record SourceAttempt(result), approved? break : try next candidate
  -> no candidate approved -> Episode.MarkUnavailable(nextAttemptAtUtc: retry policy)
  -> approved -> IStrmWriter.WriteEpisodeAsync(...) -> insert/overwrite StrmFile -> Episode.MarkCompleted()
  -> IUnitOfWork.SaveChangesAsync()   (terminal outcome)
```

Each candidate's `SourceAttempt` is persisted (without the full source URL, only what's
needed to answer "why wasn't this created" - see the domain model's
`SourceAttempt.FailureReason`, `DifferencePercentage`, etc.), independently of whether
the overall episode ends up `Completed` or `Unavailable`. Full module-boundary reasoning
in [ADR-008](adr/ADR-008-media-processing-module-boundary.md), ffprobe process-safety
and duration-tolerance rules in [ADR-009](adr/ADR-009-ffprobe-process-safety.md), `.strm`
path/sanitization/atomicity rules in [ADR-010](adr/ADR-010-strm-filesystem-safety.md),
claim/recovery/retry semantics in [ADR-013](adr/ADR-013-claim-and-recovery-semantics.md).

`Movie` processing is still not implemented (no `WriteMovieAsync` yet).

## Autonomous scheduling (implemented, Phase 4)

```
CatalogMaintenanceWorker (ticks Scheduling:MaintenanceInterval, default 60s)
  -> RunCatalogMaintenanceCommand
       1. GetScheduledDueAsync -> TryBecomeEligible(utcNow)     [Scheduled -> Pending]
       2. GetRetryableAsync -> Retry(utcNow)                    [Unavailable/Error -> Pending, NextAttemptAtUtc due]
       3. GetStaleProcessingAsync -> RecoverInterruptedProcessing(utcNow)  [stuck Searching/Validating -> Pending]
       (one SaveChangesAsync for the three steps above)
       4. GetDueForMetadataRefreshAsync -> RefreshSeriesMetadataCommand per series  [Active only]

EpisodeProcessingWorker (ticks Scheduling:ProcessingInterval, default 15s)
  -> GetPendingForProcessingAsync(BatchSize)
  -> up to MaxConcurrentEpisodeProcessing concurrently (SemaphoreSlim):
       ProcessEpisodeCommand   [the exact same pipeline POST /api/episodes/{id}/process uses]
```

Both are plain `BackgroundService`s following [ADR-003](adr/ADR-003-background-service-scheduler.md)'s
`IServiceScopeFactory`-per-tick rule; neither touches `CatalogDbContext`, a repository, or
`Episode.Status` directly - they only call the `Catalog.Application` use cases above.
`Scheduling:Enabled=false` disables both without affecting the API, manual processing, or
health checks. Full module-structure reasoning in
[ADR-012](adr/ADR-012-scheduling-module-architecture.md); the claim (why two overlapping
calls for the same episode can't both process it), stale-processing recovery, and
retryable-error classification in
[ADR-013](adr/ADR-013-claim-and-recovery-semantics.md).

`GET /api/status`, `GET /api/episodes?status=`, and `POST /api/episodes/{id}/retry`
(flips `Unavailable`/`Error` back to `Pending`, does not itself reprocess) round out the
operational surface added in this phase.

## Persistence

Single SQLite file, one `DbContext` per module (`CatalogDbContext` today). Modules do not
currently share a schema-qualification story the way Evently's Postgres schemas do -
SQLite has no schema concept - so table names are simply prefixed by convention
(`series`, `seasons`, `episodes`, `movies`, `source_attempts`, `strm_files`). See
[ADR-002](adr/ADR-002-sqlite.md).

Migrations so far: `InitialCreate` (Phase 1), `AddCatalogForeignKeys` (Phase 1.1, see
[ADR-005](adr/ADR-005-catalog-relational-integrity.md)), `AddEpisodeExternalIdUniqueIndex`
(Phase 2, see [ADR-007](adr/ADR-007-metadata-provider-and-catalog-synchronization.md)),
`AddSourceAttemptAndStrmFileForeignKeys` (Phase 3 - `SourceAttempt`/`StrmFile` now have
FK constraints against `Episode`/`Movie`, `DeleteBehavior.Restrict`, consistent with
ADR-005's convention), `AddAutonomousSchedulingSupport` (Phase 4 - `Series.LastMetadataRefreshAtUtc`/
`NextMetadataRefreshAtUtc` + an index; `Episode.UpdatedAtUtc` becoming an EF Core
concurrency token produced no schema diff at all - see
[ADR-013](adr/ADR-013-claim-and-recovery-semantics.md)). WAL mode and a SQLite
busy-timeout were also enabled in Phase 4 - see
[ADR-014](adr/ADR-014-sqlite-wal-and-busy-timeout.md).

## Incremental plan

1. ~~Foundation: solution, `Catalog` domain + persistence, health check, tests, Docker~~ (Phase 1)
2. ~~Phase 1.1 hardening: FK relationships, global exception handling, `BackgroundService`
   scope decision, cleanup~~
3. ~~Metadata: `IMetadataProvider`/`CinemetaMetadataProvider`, catalog synchronization
   (`Series`/`Season`/`Episode` from an IMDb id), with fixture-based tests (no live HTTP
   calls in CI)~~ (Phase 2)
4. ~~`MediaProcessing`: `IStreamProvider`/`FrostStreamProvider`,
   `IMediaValidator`/`FfprobeMediaValidator` (duration tolerance, codec checks, matching
   the Python backend's validated rules), `IStrmWriter` (atomic temp-file-then-rename
   writes, Jellyfin-compatible paths), driven explicitly via
   `POST /api/episodes/{id}/process` - no scheduler yet~~ (Phase 3 - this delivery)
5. ~~`Scheduling` module: `CatalogMaintenanceWorker` (release, retry, stale-processing
   recovery, metadata-refresh scheduling) and `EpisodeProcessingWorker` (bounded-
   concurrency processing), both `IServiceScopeFactory`-based per
   [ADR-003](adr/ADR-003-background-service-scheduler.md); an optimistic-concurrency
   claim (`Episode.UpdatedAtUtc`) so overlapping manual/automatic processing calls can't
   double-process the same episode; `GET /api/status`,
   `GET /api/episodes?status=`, `POST /api/episodes/{id}/retry`~~ (Phase 4 - this
   delivery)
6. Movie use cases mirroring the Episode metadata-sync and processing pipeline.
7. A Jellyfin plugin / web UI, once there's something worth pointing them at.
8. Only after the above has test coverage equivalent to the current Python backend:
   decommission the PowerShell/Python system (never before).
