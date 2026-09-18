# ADR-008: MediaProcessing module boundary

## Status

Accepted

## Context

Phase 3 needed to turn a `Pending` episode into a written `.strm` file: find candidate
streams (FrostStream), validate each one (ffprobe), and write the winning candidate to
disk. [ADR-004](ADR-004-provider-abstractions.md) originally sketched a `MediaProcessing`
module with the usual four projects (Domain/Application/Infrastructure/Presentation),
mirroring `Catalog`. This ADR records what was actually built.

## Decision 1: two projects, not four

`MediaProcessing` gets `MediaProcessing.Application` and `MediaProcessing.Infrastructure`
only - no `MediaProcessing.Domain`, no `MediaProcessing.Presentation`.

- **No Domain project**: `MediaProcessing` has no aggregates of its own. `Episode`/`Movie`
  (the things being processed) already live in `Catalog.Domain`, and `SourceAttempt`/
  `StrmFile` (the records of what happened) do too. `MediaProcessing.Application` defines
  plain records (`StreamCandidate`, `MediaValidationResult`, etc.) that describe data
  flowing *through* the pipeline, not persistent aggregates with their own lifecycle -
  they don't need a Domain layer's invariant-guarding.
- **No Presentation project**: nothing in `MediaProcessing` is called directly from HTTP.
  `POST /api/episodes/{id}/process` lives in `Catalog.Presentation` and calls
  `ProcessEpisodeCommandHandler` in `Catalog.Application`, which orchestrates
  `MediaProcessing`'s interfaces as one of several dependencies (alongside the `Catalog`
  repositories) - the same shape `CatalogSynchronizer` already has relative to
  `IMetadataProvider` (see [ADR-007](ADR-007-metadata-provider-and-catalog-synchronization.md)).
  `MediaProcessing` provides capabilities; it does not expose endpoints.

This is the same reasoning ADR-007 applied to `Providers`: build the smallest structure
that still preserves the boundaries that matter, not a fixed four-project template
regardless of what a module actually needs. Revisit (add a Domain project) only if
`MediaProcessing` grows an aggregate with real invariants of its own - not before.

## Decision 2: dependency direction - orchestration lives in Catalog, not the reverse

```
Catalog.Presentation --> Catalog.Application --> Catalog.Domain
Catalog.Infrastructure --> Catalog.Application, Catalog.Domain
Catalog.Application --> MediaProcessing.Application, Catalog.Domain
MediaProcessing.Infrastructure --> MediaProcessing.Application
MediaProcessing.Application --> Catalog.Domain   (SourceAttemptResult only)
```

`ProcessEpisodeCommandHandler` - the pipeline orchestrator - lives in
`Catalog.Application`, not in `MediaProcessing`. `Episode` is a `Catalog.Domain`
aggregate; the thing that decides *when* to call `StartSearching`/`StartValidating`/
`MarkCompleted` naturally belongs next to the aggregate whose state machine it drives,
the same way `CatalogSynchronizer` (which drives `Episode.Schedule`/`UpdateMetadata`)
lives in `Catalog.Application` rather than inside a `Providers` module. `MediaProcessing`
supplies capabilities (`IStreamProvider`, `IMediaValidator`, `IStrmWriter`); it has no
opinion on when they're invoked or what happens to the result.

`MediaProcessing.Application` depends on `Catalog.Domain` for exactly one type -
`SourceAttemptResult`, reused directly by `MediaValidationResult` rather than defining a
competing enum (see the type's own XML doc). `Catalog.Domain` has zero outward
dependencies, so this does not create a cycle - and `test/StrmManager.ArchitectureTests/MediaProcessingBoundaryTests.cs`
enforces `Catalog.Domain` never depends back on `MediaProcessing`,
`MediaProcessing.Application` never depends on `Catalog.Application`, and
`MediaProcessing.Infrastructure` never depends on `Catalog` at all (it only implements
`MediaProcessing.Application`'s interfaces - it doesn't need to know `Episode` exists).

## Decision 3: FrostStream uses only the public, documented interface

`FrostStreamProvider` calls the same public Stremio-compatible HTTP endpoint
(`GET stream/series/{episodeId}.json`) the legacy PowerShell/Python system used - no
cookie extraction, no captured browser sessions, no anti-bot/CAPTCHA bypass technique.
The episode id is always `Uri.EscapeDataString`-encoded, never hand-concatenated into the
URL. This was a hard constraint from the start of the phase, not a decision made under
ambiguity.

## Decision 4: per-candidate episode identity validation (intentional deviation from legacy)

The legacy PowerShell validated episode identity (does this candidate actually match
S{season}E{episode}?) once per batch of candidates. `EpisodeIdentityValidator.Evaluate`
is instead called once per candidate, inside the same loop that runs media validation
(`ProcessEpisodeCommandHandler`). This is a deliberate, safer improvement, not a
faithful port: a batch-level check can't tell *which* candidate in a mixed batch is the
mismatch, and would either reject good candidates alongside bad ones or (worse) accept
the batch and let a wrong-episode candidate through. Recorded here and in the Phase 3
legacy-parity comparison as an intentional difference.

## Consequences

- Adding a second stream provider or a movie-processing pipeline later requires only new
  `MediaProcessing.Infrastructure` implementations and a new orchestrating command in the
  relevant module's `Application` project - no change to `MediaProcessing.Application`'s
  interfaces.
- `MediaProcessing` cannot be exercised as an isolated module through HTTP - it is only
  reachable through `Catalog`'s endpoints. This matches its role: a capability provider,
  not an independently addressable module.
- Not addressed in this phase (deferred, not forgotten): movie processing (no
  `WriteMovieAsync` on `IStrmWriter` yet - added the day a movie pipeline actually needs
  it, not speculatively); a `Scheduling` module that would call `ProcessEpisodeCommandHandler`
  automatically (still explicitly out of scope, per the Phase 3 brief).
