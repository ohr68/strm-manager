# ADR-012: Scheduling module architecture

## Status

Accepted

## Context

Phase 4 needed the server to become autonomous: promote released episodes, retry due
Unavailable/Error episodes, recover interrupted processing, refresh Series metadata
periodically, and process Pending episodes - all without manual intervention.
[ADR-003](ADR-003-background-service-scheduler.md) already decided *how* a
`BackgroundService` talks to the database (`IServiceScopeFactory`, never a directly
injected `CatalogDbContext`); this ADR decides how much of a module `Scheduling` gets,
and where the actual autonomous-behavior logic lives.

## Decision 1: one project, `Scheduling.Infrastructure` - no `Scheduling.Application`

Every other module in this codebase (`Catalog`, `MediaProcessing`) got progressively
smaller project structures as their actual needs became clear - see
[ADR-007](ADR-007-metadata-provider-and-catalog-synchronization.md) and
[ADR-008](ADR-008-media-processing-module-boundary.md). `Scheduling` continues that
trend to its logical end: it gets **one** project, not two, and definitely not four.

A `BackgroundService` is architecturally a **caller**, the same role an HTTP endpoint
plays - it resolves a use case from DI and invokes it. `StrmManager.Api`'s Presentation
endpoints don't get their own "Application" layer just because they call into
`Catalog.Application`; `Scheduling`'s workers don't need one either. The actual
autonomous-behavior *logic* - what counts as "released," what counts as "stale," what
counts as "due for a metadata refresh" - is Catalog domain knowledge, not scheduling
knowledge, and lives in `Catalog.Application` (`RunCatalogMaintenanceCommand`,
`ProcessEpisodeCommandHandler`) precisely so it can be tested and reasoned about without
a timer anywhere nearby. `Scheduling.Infrastructure` contains exactly two
`BackgroundService` classes (`CatalogMaintenanceWorker`, `EpisodeProcessingWorker`), an
options class, and one small DI-inversion class (`SchedulerStatusProvider`) - nothing
else. This directly satisfies the brief's own constraint: *"A BackgroundService should
not become a new business-logic layer."* With no `Scheduling.Application` project to put
business logic in, that constraint enforces itself structurally, not just by convention.

## Decision 2: two workers, not one and not five

`CatalogMaintenanceWorker` bundles release-eligibility, retry, stale-processing
recovery, and metadata-refresh scheduling into a single ticking loop, because all four
are cheap, independent, read-a-small-table-then-write operations that make sense to
sweep together on one cadence - there is no reason a episode-retry check and a
stale-processing check need different timers. `EpisodeProcessingWorker` is separate
because it does fundamentally different, expensive work (FrostStream HTTP calls,
ffprobe child processes) that needs its own concurrency bound
(`MaxConcurrentEpisodeProcessing`) and its own, much shorter, polling interval - bundling
it into the maintenance loop would mean every maintenance tick blocks on however long
episode processing takes.

Five workers (one each for release/retry/recovery/metadata-refresh/processing) was
considered and rejected: four of those five are the same kind of operation (a query,
then a domain transition, then one save) differing only in *which* query - splitting
them into separate `BackgroundService`s would mean five copies of the same
`IServiceScopeFactory`/`PeriodicTimer`/error-isolation boilerplate for no behavioral
benefit, the "one BackgroundService per tiny operation" extreme the brief explicitly
warned against.

## Decision 3: config split - operational cadence vs business-rule timing

```
Scheduling:*                              (Scheduling.Infrastructure - SchedulingOptions)
  Enabled, MaintenanceInterval, ProcessingInterval, MaxConcurrentEpisodeProcessing, BatchSize

Processing:*                              (Catalog.Application - ProcessingOptions)
  UnavailableRetryDelay, RetryableErrorDelay, StaleProcessingThreshold

Metadata:Refresh:*                        (Catalog.Application - MetadataRefreshOptions)
  ActiveSeriesRefreshInterval
```

The brief's illustrative config example put `RetryableErrorDelay` and friends under one
`Scheduling:` section. This ADR deliberately diverges from that shape: *how long before
retrying a failed episode* and *how long is a processing run allowed to sit before it's
considered abandoned* are business rules about the `Episode` processing pipeline, not
facts about the scheduler - exactly the same category `ProcessingOptions.UnavailableRetryDelay`
already occupied before Phase 4 existed. Keeping them there means `RunCatalogMaintenanceCommandHandler`
and `ProcessEpisodeCommandHandler` (both in `Catalog.Application`) read their own
business-rule config directly, without a project reference to `Scheduling` - which would
otherwise invert the module dependency direction (`Scheduling -> Catalog`, never the
reverse). `SchedulingOptions` ends up holding only mechanical values: how often the
poller wakes up, and how many items it takes per tick.

## Decision 4: `ISchedulerStatusProvider` - Dependency Inversion, not a new coupling

`GET /api/status` (in `Catalog.Presentation`) needs to report whether the scheduler is
enabled, but `Catalog.Application` must not depend on `Scheduling` (wrong direction).
The abstraction (`ISchedulerStatusProvider`, one boolean property) lives in
`Catalog.Application/Status/` - the consumer's side - and `Scheduling.Infrastructure`
provides the real implementation (`SchedulerStatusProvider`, reading its own
`SchedulingOptions`), registered in DI after `CatalogModule`'s safe fallback
(`DisabledSchedulerStatusProvider`) so the status endpoint still returns a sane answer
even in a hypothetical host that never adds the `Scheduling` module. This is the exact
same shape `IMetadataProvider`/`IStreamProvider`/`IMediaValidator` already use
throughout this codebase - nothing new was invented for this one boolean.

## Consequences

- Adding a third worker (e.g. a future `Movie` processing loop) means one more
  `BackgroundService` class in `Scheduling.Infrastructure` plus a corresponding use case
  in whichever `Application` project owns that domain - no change to this ADR's
  structure.
- `RunCatalogMaintenanceCommand` and `ProcessEpisodeCommand` are fully testable (and
  tested - see `CatalogMaintenanceTests`) without ever starting a `BackgroundService` or
  waiting on a real timer.
- Not addressed in this phase: distributed/multi-node scheduling (explicitly out of
  scope - this remains a single-instance service, per the brief).
