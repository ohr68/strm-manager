# ADR-011: Processing crash/restart semantics need no new recovery mechanism

## Status

Accepted

## Context

The Phase 3 brief explicitly asked: if the server crashes or restarts mid-processing
(after `StartSearching`/`StartValidating` but before a terminal outcome), how is that
episode recovered? It also explicitly asked for this to be *discussed*, not silently
implemented as a new background mechanism - Phase 3 has no scheduler, so any
"auto-recover stuck episodes" worker would be new automation smuggled in under a
different name.

## Decision: reuse Phase 1's existing design; no new mechanism

No new recovery code was written. The answer already exists in `Episode`'s state machine
as designed in Phase 1 (see [docs/architecture.md](../architecture.md#episode-state-machine),
"Crash/restart safety") and is simply *used correctly* by `ProcessEpisodeCommandHandler`:

- `episode.StartSearching(utcNow)` and `episode.StartValidating(utcNow)` are called and
  transition the in-memory `Episode` object, but **no `SaveChangesAsync` happens after
  either call**.
- Exactly one `SaveChangesAsync` happens in the entire method, at whichever terminal
  outcome is reached: inside `FinishAsUnavailable`, inside `FinishAsError`, or after
  `episode.MarkCompleted(utcNow)` on the success path.
- If the process crashes (or the request is aborted, or the pod is killed) at any point
  between `StartSearching` and that single terminal save, **nothing was ever written to
  the database**. The `Episode` row in SQLite is untouched - still exactly `Pending`,
  because that's what it was when `episodeRepository.GetAsync` loaded it at the start of
  `Handle`.

A crashed episode therefore requires no special "was this stuck in Searching?" recovery
query, no cleanup job, no timeout-based unstick logic: the next `POST
/api/episodes/{id}/process` call for that episode (whenever it happens - a manual retry
today, a scheduler's retry policy in a future phase) simply starts the whole pipeline
over from a still-valid `Pending` state, exactly as if the crashed attempt had never
happened.

## Why this is sufficient and not a gap

The alternative design this ADR is *rejecting* - persisting `Searching`/`Validating` as
their own committed states - was considered and explicitly not built, for two reasons:

1. It buys nothing. The only thing a persisted `Searching` state could be used for is
   detecting "this episode has been stuck mid-pipeline for too long" and forcing it back
   to `Pending` - but that is exactly what happens automatically, for free, by simply
   *not* persisting the intermediate state at all. A stuck-detector for a state that can
   never actually get stuck in the database is a mechanism solving a problem the current
   design doesn't have.
2. It would require new automation (a background sweep, or at minimum a startup
   reconciliation step) to actually reset a stuck `Searching` row back to `Pending` -
   precisely the kind of hidden automation the Phase 3 brief asked to avoid without prior
   discussion. This ADR **is** that discussion, and the conclusion is that no such
   automation is needed.

This mirrors [ADR-007](ADR-007-metadata-provider-and-catalog-synchronization.md)
Decision 10's transaction-boundary reasoning (all the risky I/O - the HTTP/ffprobe/
filesystem work - happens before the single `SaveChangesAsync`, never interleaved with
it) applied specifically to the crash-recovery question.

## Consequences

- No behavior changed in Phase 3 to support this - it was already correct by
  construction once `ProcessEpisodeCommandHandler` followed the same "in-memory
  transitions, single terminal save" pattern every other write path in this codebase
  already uses.
- A crashed run's partial work (any `SourceAttempt` rows inserted via `RecordAttempt`
  during the same `Handle` call) is also lost on crash, since `sourceAttemptRepository.Insert(...)`
  only stages the row in the `DbContext` change tracker - nothing is written until the
  same terminal `SaveChangesAsync`. This is intentional, not an oversight: a
  `SourceAttempt` describes "why an outcome happened," and there was no outcome yet when
  the process died, so there is nothing meaningful to record. The re-run will produce a
  fresh, complete set of attempts for whatever it discovers this time.
- This reasoning does not, by itself, provide automatic retry - something still has to
  call `POST /api/episodes/{id}/process` again. That remains a `Scheduling` module
  concern, explicitly out of scope for Phase 3 (see [docs/architecture.md](../architecture.md#incremental-plan)
  step 5), and whatever retry policy it eventually implements can rely on exactly the
  guarantee this ADR describes: a `Pending` episode has never partially processed.
