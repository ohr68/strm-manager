# ADR-006: Plain UTC `DateTime`, not `DateTimeOffset`

## Status

Accepted

## Context

The original brief suggested evaluating `DateTimeOffset` "when it preserves semantics
better." The Phase 1 checkpoint flagged that this repository uses plain `DateTime`
everywhere (matching Evently's convention) without an explicit decision recorded for it.

## Decision

Keep plain UTC `DateTime` for every timestamp in the domain and persistence model. No
migration to `DateTimeOffset` in this pass, or planned.

This is workable specifically because the following are already true in this codebase,
and remain the enforced rules going forward:

- **Every persisted timestamp represents UTC**, signaled by the `...AtUtc` naming
  convention (`ReleaseAtUtc`, `CreatedAtUtc`, `UpdatedAtUtc`, `LastAttemptAtUtc`,
  `NextAttemptAtUtc`, `AttemptedAtUtc`). There is no timestamp property anywhere in
  `Catalog.Domain` without that suffix.
- **Current time is obtained from `TimeProvider`**, not `DateTime.UtcNow`, at the one
  place that reads the system clock: `TimeProvider.System` is registered as a singleton
  in `Program.cs` and consumed via `TimeProvider.GetUtcNow().UtcDateTime` (see
  `AddSeriesCommandHandler`). This is what makes the domain testable without waiting on
  a real clock, and is the actual reason `DateTimeOffset` isn't needed for correctness:
  `TimeProvider.GetUtcNow()` already returns a `DateTimeOffset`, and converting it to
  `.UtcDateTime` at the Application layer boundary is a deliberate, single, well-defined
  point where the offset is discarded because it's already known to be zero (UTC).
- **Domain code never reads the system clock directly.** Every `Episode`/`Movie` method
  that needs "now" takes `DateTime utcNow` as a parameter (`Schedule(...)`,
  `TryBecomeEligible(utcNow)`, `StartSearching(utcNow)`, etc.) - confirmed by there being
  zero occurrences of `DateTime.UtcNow` under `src/`. The caller (Application layer)
  supplies it from `TimeProvider`.
- **Release eligibility is a plain UTC-to-UTC comparison** (`ReleaseAtUtc > utcNow` in
  `Episode.TryBecomeEligible`) - since both sides are guaranteed UTC by convention, no
  timezone-aware comparison is needed.

No runtime assertion (e.g. throwing if a `DateTime.Kind` isn't `Utc`) was added. SQLite/
EF Core's Sqlite provider does not preserve `DateTime.Kind` on round-trip in a way that
would make such a check reliable after a read from the database, so a `Kind`-based guard
would create false confidence rather than real safety; the naming convention plus "always
go through `TimeProvider`" is judged sufficient at this scale, and cheaper than adding
either `DateTimeOffset` (a real, if minor, complexity increase across every entity and
every EF configuration) or a runtime check that can't fully deliver on its promise.

## Consequences

- Consistent with Evently's convention - no mixed `DateTime`/`DateTimeOffset` usage to
  reason about.
- If STRM Manager ever needs to reason about a *local* release time (e.g. displaying
  "airs at 21:00 local" in a future UI) that reasoning belongs at the
  Presentation/consumption layer, converting from the stored UTC value - it does not
  require changing the persisted/domain representation.
- Revisit only if a concrete need for timezone-aware storage appears (none exists today).
