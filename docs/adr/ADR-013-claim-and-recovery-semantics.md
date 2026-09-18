# ADR-013: Claim and recovery semantics for autonomous processing

## Status

Accepted - supersedes [ADR-011](ADR-011-processing-restart-semantics.md)

## Context

[ADR-011](ADR-011-processing-restart-semantics.md) decided, for Phase 3, that
`Episode.Status` never needs to be persisted as `Searching`/`Validating` - only the
terminal outcome is saved, so a crash mid-pipeline just leaves the row as `Pending`,
recoverable by simply calling `POST /api/episodes/{id}/process` again. That reasoning
depended on there being exactly one caller at a time (a human, manually).

Phase 4 breaks that assumption: `EpisodeProcessingWorker` now calls the same pipeline on
a timer, concurrently with any manual API call, and with itself across overlapping
ticks. Never persisting `Searching` means two callers can both load the same `Pending`
episode, both proceed, and both do the FrostStream/ffprobe/`.strm`-write work - wasted
effort at best, and at worst two `SourceAttempt` rows and a `.strm` file racing each
other. ADR-011's design needs a claim; this ADR is the re-evaluation ADR-011's own
"Consequences" section anticipated ("this remains a `Scheduling` module concern").

## Decision 1: `Episode.UpdatedAtUtc` becomes an optimistic-concurrency token

No new column. `UpdatedAtUtc` already exists and is already updated by every domain
transition (`StartSearching`, `StartValidating`, `MarkCompleted`, ...). Marking it
`.IsConcurrencyToken()` in `EpisodeConfiguration` makes EF Core append
`AND [UpdatedAtUtc] = @original_UpdatedAtUtc` to its `UPDATE` statement - so two
`DbContext`s that both loaded the same row (same `UpdatedAtUtc`) and both try to save a
change to it will not both succeed: the second `SaveChangesAsync` affects zero rows and
EF throws `DbUpdateConcurrencyException`. This is a real, general, EF-Core-native
compare-and-swap, not a bespoke lock - SQLite serializes the writes themselves, but only
this token makes the *second* writer's stale view detectable rather than silently
overwriting.

`IUnitOfWork` gained `TrySaveChangesAsync` (translates that exception into `false`) so
`Catalog.Application` never needs to reference `Microsoft.EntityFrameworkCore` directly
- the Domain/Application layers stay technology-agnostic, per this codebase's standing
layering rule.

## Decision 2: the claim - `StartSearching` is persisted before any external call

`ProcessEpisodeCommandHandler.Handle` now calls `unitOfWork.TrySaveChangesAsync()`
immediately after `episode.StartSearching(utcNow)`, *before* `IStreamProvider` is ever
invoked. Two overlapping calls for the same `Pending` episode both succeed at the
in-memory `StartSearching` (both see `Pending`), but only one wins the save; the loser
gets `false` back and returns `EpisodeErrors.AlreadyBeingProcessed` (409 Conflict)
immediately, before doing any FrostStream/ffprobe work at all. This satisfies the
brief's requirement directly: *"A claim must become visible to competing operations
before the slow FrostStream/ffprobe work begins."* No explicit database transaction is
held open across that external work either - `SaveChangesAsync` commits and returns; the
slow I/O happens with no transaction in flight, consistent with
[ADR-007](ADR-007-metadata-provider-and-catalog-synchronization.md) Decision 10's
transaction-boundary discipline.

`StartValidating` is saved too, right after it's set, for a different reason (Decision 3
below) - not because it needs its own concurrency check (only one caller can reach that
point at all, since the other lost the earlier claim).

Proven directly in `ProcessEpisodeConcurrencyTests`: two real concurrent
`POST /api/episodes/{id}/process` requests for the same episode, asserting exactly one
`StrmFile` and exactly one `Approved` `SourceAttempt` - run 8 times locally with no
flakiness.

## Decision 3: stale-processing recovery reuses `UpdatedAtUtc`, no new field

Persisting `Searching`/`Validating` means a genuinely interrupted run (crash, container
restart, cancelled shutdown) can now leave a row stuck in one of those states forever
unless something notices. Detecting "stuck" needs a timestamp - `UpdatedAtUtc` is
already exactly that timestamp (set the moment `StartSearching`/`StartValidating` was
persisted), so no new column was added. `RunCatalogMaintenanceCommandHandler` computes
`staleThresholdUtc = utcNow - ProcessingOptions.StaleProcessingThreshold` (15 minutes by
default - generous relative to FrostStream's 15s and ffprobe's 30s configured timeouts)
and queries `GetStaleProcessingAsync(staleThresholdUtc)` for any `Searching`/`Validating`
row whose `UpdatedAtUtc` is older than that.

A distributed lease (a separate "who owns this, until when" record) was considered and
rejected as unnecessary machinery for a single-instance service - the brief's own
guidance ("do not add distributed lease machinery... unless necessary"). The concurrency
token already prevents two callers from *simultaneously* claiming the same episode;
staleness only needs to answer "has *nobody* been working on this for too long," which a
plain timestamp comparison answers correctly.

## Decision 4: recovery is a distinct domain transition, not `MarkUnavailable`

New `Episode.RecoverInterruptedProcessing(utcNow)`: allowed only from `Searching`/
`Validating`, transitions to `Pending`, and - unlike `MarkUnavailable`/`MarkError` -
does **not** increment `AttemptCount` or touch `LastError`. An interrupted run reached no
outcome (no candidates were exhausted, no technical failure was diagnosed) - recording it
as if it were "we looked and found nothing" (`Unavailable`) or "a technical failure
happened" (`Error`) would misrepresent what's known, and would inflate `AttemptCount` for
a run that never actually ran to completion. The next pickup starts completely fresh,
exactly as if this run had never begun - which, from the database's perspective before
this ADR's claim existed, is exactly what ADR-011 already guaranteed for Phase 3.

## Decision 5: retryable-error classification lives entirely in `Catalog.Application`

`Episode.MarkError` gained an optional `nextAttemptAtUtc` - its mere presence *is* the
retryability signal; no new Domain enum, no leaking of `HttpRequestException`/ffprobe
process types into Domain (the brief's explicit constraint). `ProcessEpisodeCommandHandler`
classifies at each failure site using information it already has: a stream-provider
failure (`StreamProviderErrors.ProviderUnavailable`/`Timeout`/`InvalidResponse`) is
always transient/technical, so it's retryable (`ProcessingOptions.RetryableErrorDelay`,
15 minutes by default - shorter than `UnavailableRetryDelay`'s 6 hours, since "the
provider hiccuped" should be checked again sooner than "we thoroughly found nothing to
show"). A missing Season/Series record or a `.strm` write failure (traversal rejection,
permission, disk) is treated as a potentially persistent configuration problem and is
**not** automatically retried - it stays `Error` until a human fixes the underlying issue
and calls `POST /api/episodes/{id}/retry` (or the API's `process` endpoint) manually.
`MarkError` always assigns `nextAttemptAtUtc` explicitly (defaulting to `null`), even
when it's not being set to a real value - this matters because an episode can cycle
through `Unavailable` (which does set `NextAttemptAtUtc`) before later failing with a
non-retryable `Error`; without an explicit overwrite, the stale `Unavailable`-era value
would linger and make `GetRetryableAsync` incorrectly retry a failure that was never
meant to be automatic.

`GetRetryableAsync` (already written in Phase 1, unmodified) already filters
`(Unavailable OR Error) AND NextAttemptAtUtc != null AND NextAttemptAtUtc <= now` - it
required no change at all to correctly support this: a non-retryable `Error` simply never
has a `NextAttemptAtUtc`, so it was never a match.

## Decision 6: first processing attempts have priority over retries

`EpisodeProcessingWorker` selects `Pending` episodes through
`GetPendingForProcessingAsync`, bounded by `SchedulingOptions.BatchSize`. Once automatic
retries were introduced, `Pending` stopped representing a single kind of work: it can
contain both episodes that have never reached a processing outcome and episodes returning
from `Unavailable`/retryable `Error`.

A retry backlog must not delay newly eligible content that has never been processed.
`Episode.AttemptCount` already provides the distinction without adding another persisted
field: `AttemptCount == 0` means no processing attempt has reached an outcome yet, while
`AttemptCount > 0` means at least one outcome has already been recorded. Therefore,
`GetPendingForProcessingAsync` selects first-attempt episodes before retry episodes.

Within the same priority class, episodes that have been `Pending` longer are selected
first using `UpdatedAtUtc`. `ReleaseAtUtc` and `Id` provide deterministic tie-breaking;
they do not introduce additional business-priority classes.

Interrupted processing requires no special queue marker. As established by Decision 4,
`RecoverInterruptedProcessing` preserves `AttemptCount`, so an interrupted run naturally
returns to the priority class appropriate to its history: a first attempt interrupted
before any outcome remains a first attempt, while an interrupted retry remains a retry.

`SchedulingOptions.BatchSize` continues to bound how many `Pending` episodes the worker
selects per processing tick. Catalog maintenance may promote multiple due retries to
`Pending` in one maintenance run, but those retries cannot move ahead of first-attempt
work solely because they entered the queue earlier.

## Consequences

- `ProcessEpisodeCommandHandler` now performs two `SaveChangesAsync`-family calls before
  reaching a terminal outcome (the claim, then the `StartValidating` save) instead of
  zero - a small overhead accepted deliberately for correctness under concurrency.
- Manual `POST /api/episodes/{id}/process` and `EpisodeProcessingWorker`'s own tick are
  now safe to run concurrently against the same episode by construction, not by
  convention - verified directly, not just argued.
- `Episode.AttemptCount` now has a precise meaning: it counts completed processing
  attempts (an outcome was reached), never interrupted or claim-rejected ones.
- A backlog of automatic retries cannot delay `Pending` episodes that have never reached
  a processing outcome; first attempts are selected first regardless of how long a retry
  has already been waiting.
- Queue priority requires no new persisted state or migration: it derives from the
  existing `AttemptCount` semantics and remains bounded independently by
  `SchedulingOptions.BatchSize`.
