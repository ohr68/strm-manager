# ADR-007: Metadata provider boundary and catalog synchronization

## Status

Accepted

## Context

Phase 2 needed to turn an IMDb id into a persisted Series/Season/Episode graph, backed
by Cinemeta (the Stremio addon) today and replaceable later. [ADR-004](ADR-004-provider-abstractions.md)
sketched the conceptual contracts before any of this existed; this ADR records what was
actually built and why, including where it deliberately differs from ADR-004's original
sketch.

## Decision 1: module boundary - embedded in Catalog, not a separate `Providers` module

ADR-004 originally imagined a standalone `Providers` module (`Providers.Application`/
`Providers.Infrastructure`). Phase 2 does **not** do that. Instead:

- `IMetadataProvider`, `SeriesMetadata`, `EpisodeMetadata`, `MetadataProviderErrors`, and
  `CatalogSynchronizer` live in `StrmManager.Modules.Catalog.Application/Metadata/`.
- `CinemetaMetadataProvider`, its DTOs, its mapper, and `CinemetaOptions` live in
  `StrmManager.Modules.Catalog.Infrastructure/Metadata/Cinemeta/`.

No new project was created. This is the smallest structure that still preserves every
constraint that mattered: Domain doesn't know Cinemeta exists (doesn't even know
`IMetadataProvider` exists - the interface lives in Application); Application depends
only on the `IMetadataProvider` abstraction, never on Cinemeta; Cinemeta is replaceable
by swapping one DI registration in `CatalogModule`. With exactly one consumer (`Catalog`)
and one provider (`Cinemeta`), a dedicated module/PublicApi-style project would have been
pure ceremony - the same reasoning as [ADR-001](ADR-001-modular-monolith.md)'s rejection
of Evently's `PublicApi` pattern. `test/StrmManager.ArchitectureTests/MetadataProviderBoundaryTests.cs`
enforces the boundary that matters (Domain/Application never reference the Cinemeta
namespace; Cinemeta DTOs are never public) regardless of project layout.

Revisit this (split into a real `Providers` module) the day a second provider of a
*different kind* appears (e.g. a stream provider in a later phase) and the `Metadata`
folder starts accumulating unrelated provider concerns - not before.

## Decision 2: `IMetadataProvider` and provider-neutral models

```csharp
public interface IMetadataProvider
{
    Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken = default);
}

public sealed record SeriesMetadata(ExternalIds ExternalIds, string Title, string? OriginalTitle, int Year, SeriesStatus Status, IReadOnlyList<EpisodeMetadata> Episodes);
public sealed record EpisodeMetadata(string ExternalId, string Title, int SeasonNumber, int EpisodeNumber, TimeSpan? Runtime, DateTime? ReleaseAtUtc);
```

Only what `CatalogSynchronizer` actually needs - not Cinemeta's full schema (posters,
genres, cast, trailers, IMDb rating, etc. are all left unmapped). `EpisodeMetadata.ReleaseAtUtc`
is nullable on purpose: a provider can legitimately have no reliable release date for an
episode, and nothing is allowed to invent one (see Decision 5).

## Decision 3: Cinemeta implementation

`CinemetaMetadataProvider` is a typed `HttpClient` (`AddHttpClient<IMetadataProvider, CinemetaMetadataProvider>`),
configured via `CinemetaOptions` (`Metadata:Cinemeta:BaseUrl`, default
`https://v3-cinemeta.strem.io/` - a public, non-machine-specific default, still
overridable via configuration/environment variables), validated at startup
(`.ValidateDataAnnotations().ValidateOnStart()`).

**HTTP resilience** is configured once, on the `HttpClient` registration in
`CatalogModule` (`.AddResilienceHandler(...)`, `Microsoft.Extensions.Http.Resilience`) -
`CinemetaMetadataProvider` itself never retries. Conservative: `AddTimeout` (default 10s,
configurable) plus `HttpRetryStrategyOptions` with 2 retries, exponential backoff with
jitter. `HttpRetryStrategyOptions`' default `ShouldHandle` predicate only treats 5xx/408/
network/timeout outcomes as transient - a 404 ("series not found") is a normal, expected
outcome and is never retried.

**Failure differentiation** (`MetadataProviderErrors`): `SeriesNotFound` (404 -> `ErrorType.NotFound`,
maps to HTTP 404 for the caller), `ProviderUnavailable` (5xx/network failure),
`InvalidResponse` (malformed JSON, missing `meta`, missing required fields),
`Timeout` (request exceeded the configured timeout - covers both `Polly.Timeout.TimeoutRejectedException`
from the resilience pipeline and a plain `HttpClient.Timeout`-triggered `OperationCanceledException`,
but **not** the caller's own `CancellationToken` being cancelled, which propagates as a
normal `OperationCanceledException` instead of being swallowed into a `Result`). All are
expected `Result` failures - none of this reaches `GlobalExceptionHandler`; only a
genuine bug would.

## Decision 4: release date mapping - `firstAired` preferred, `released` fallback, never invented

`CinemetaMetadataMapper.MapEpisode` prefers `firstAired`, falls back to `released`, and
returns `ReleaseAtUtc: null` if neither parses - preserving the **exact UTC instant**
(`DateTime.TryParse` with `AdjustToUniversal | AssumeUniversal`, not just the date part).
Verified against the real `tt27497393` ("Stuart Fails to Save the Universe") schedule in
`test/StrmManager.Modules.Catalog.UnitTests/Metadata/StuartFailsToSaveTheUniverseScheduleTests.cs`,
using literal controlled UTC timestamps throughout (never the machine clock) - S01E09 at
exactly `2026-09-18T05:00:00Z` is `Scheduled` one second before that instant and `Pending`
at or after it; S01E10 (a week later) stays `Scheduled` at the same boundary.

`CatalogSynchronizer` skips (counts, logs, never persists) any episode metadata with
`ReleaseAtUtc: null` - `Episode.Schedule` requires a non-nullable release date and no
caller is allowed to fabricate one (no "default to now"/"default to midnight"). A skipped
episode is picked up automatically on a later refresh once the provider has a date for it.

## Decision 5: Season 0 is preserved

Cinemeta represents specials as season `0`. Nothing in the mapper or
`CatalogSynchronizer` filters it out - `MapEpisode` only requires `season >= 0`. There
was no concrete reason found to special-case it, and discarding data by default is a
worse failure mode than keeping it (an unwanted Season 0 can be hidden/ignored at a
consumption layer later; silently dropped data cannot be recovered without a resync).

## Decision 6: series status mapping is conservative

Cinemeta's `status` string ("Continuing", "Ended", or absent/anything else) maps to
`SeriesStatus.Ended` only on an exact "Ended" match; everything else (including missing
or unrecognized values) maps to `SeriesStatus.Active`. An unknown status must not fail
synchronization - "assume still airing" is the safer wrong guess (worst case: a refresh
keeps checking a show that already ended) versus "assume ended" (worst case: a show
still airing stops being checked).

## Decision 7: `AddSeries` synchronizes inline; `refresh` is the explicit resync path

`POST /api/series` creates the `Series` from caller-supplied data (still required/validated
as before) **and**, in the same request, calls `IMetadataProvider` and runs
`CatalogSynchronizer` - so adding a series results in its seasons/episodes being
available immediately, without a separate manual step (replacing the legacy PowerShell
workflow's manual metadata paste). If the provider call fails, this is **not** fatal: the
`Series` is still created from the caller's data, the command still returns success (the
failure is logged, not swallowed silently), and `POST /api/series/{id}/refresh` can
retry later. The response body does not surface whether metadata sync succeeded in this
phase - `GET`/`GET .../seasons` shows the actual result.

Re-`POST`ing for an already-existing `Series` (same IMDb id) keeps its Phase 1 behavior
unchanged: returns the existing id, **no** side effect, no re-sync. Refresh is the only
path that re-syncs an existing series - kept as one clear responsibility per operation
rather than overloading `AddSeries`' idempotency check with a "maybe also refresh" branch.

`CatalogSynchronizer` (the season/episode matching-and-persisting logic) is shared
between `AddSeriesCommandHandler` and `RefreshSeriesMetadataCommandHandler` so the
idempotency/matching/status-preservation rules exist in exactly one place.

## Decision 8: identity and matching

- **Series**: unique by IMDb id (owned-type index from Phase 1, unchanged).
- **Season**: unique by `(SeriesId, Number)` (Phase 1 index, unchanged).
- **Episode**: unique by `(SeasonId, EpisodeNumber)` (Phase 1 index, unchanged) **and**
  now also by `ExternalId` alone (new `AddEpisodeExternalIdUniqueIndex` migration) -
  `ExternalId` (e.g. `tt27497393:1:9`) is unique across the whole catalog, not just
  within a season, and this is a second, independent database-level guard against
  duplicate synchronization.
- **Matching precedence during sync**: prefer the existing episode with the same
  `ExternalId`; fall back to `(SeasonId, EpisodeNumber)` if no `ExternalId` match is
  found (covers a provider changing an episode's external id between syncs, which
  Cinemeta is not expected to do but isn't ruled out). **Never** matched by title -
  titles can and do change.

## Decision 9: metadata refresh preserves operational state

`Episode.UpdateMetadata(title, runtime, releaseAtUtc, utcNow)` (new domain method)
always updates `Title`/`Runtime`, but only updates `ReleaseAtUtc` while the episode is
still `Scheduled` - once processing has started (`Pending` and beyond), the release
timestamp is historical, not something a refresh should keep rewriting.
`UpdateMetadata` never touches `Status` at all. `CatalogSynchronizer` calls
`episode.TryBecomeEligible(utcNow)` right after `UpdateMetadata` for every matched
existing episode - a release-date correction that moves a `Scheduled` episode's date
into the past is picked up through the **domain's own transition method**, never by the
synchronizer assigning `Status` directly. `Completed` and `Unavailable` episodes keep
their status through any number of refreshes - verified by
`test/StrmManager.Modules.Catalog.IntegrationTests/Metadata/MetadataSynchronizationTests.cs`
(`Refresh_DoesNotResetACompletedEpisodeBackToPending`,
`Refresh_DoesNotResetAnUnavailableEpisodeBackToPending`).

## Decision 10: transaction boundary

The HTTP call to the metadata provider always happens **before** any `SaveChangesAsync`
call - no SQLite transaction is open while waiting on Cinemeta. Because `Series`/`Season`/
`Episode` IDs are client-generated (`Guid.NewGuid()` in their factory methods, not
database identity columns), a whole sync batch (new `Series` + new `Season`s + new
`Episode`s) can be added to the `DbContext` independently (no navigation properties
needed - see [ADR-005](ADR-005-catalog-relational-integrity.md)) and committed in a
**single** `SaveChangesAsync` call at the end - EF Core orders the INSERT statements
correctly from the configured FK relationships and the already-known key values, without
needing an intermediate save between "insert the Season" and "insert episodes that
reference it."

## Decision 11: concurrency

No distributed locking - this is a single-instance server. The unique indexes (Decision
8) are the actual safety net: two concurrent refreshes for the same series would each
attempt to insert the same `Season`/`Episode` rows, and the database rejects the loser
with a constraint violation rather than producing duplicates. That failure surfaces as an
unexpected exception today (not yet mapped to a specific `Result` - acceptable for a
low-concurrency single-user tool; revisit if it proves to be a real problem in practice).

## Consequences

- Adding a second metadata provider later requires only a new `Infrastructure`
  implementation of `IMetadataProvider` and a DI registration change - no change to
  `Catalog.Domain`, `CatalogSynchronizer`, or either command handler.
- The API surface grew by three endpoints (`POST /api/series/{id}/refresh`,
  `GET /api/series/{id}/seasons`, `GET /api/seasons/{id}/episodes`), each backed by a
  real use case introduced in this phase - no speculative endpoints were added.
- Not addressed in this phase (deferred, not forgotten): `SourceAttempt`/`StrmFile` still
  have no FK constraints (noted in ADR-005, still true - nothing writes them yet);
  concurrent-refresh conflicts surface as raw exceptions rather than a mapped `Result`;
  `Series.ExternalIds.Tmdb`/`Tvdb` are never populated from Cinemeta metadata (Cinemeta
  doesn't reliably provide them in the base response - out of scope for this phase).

## Addendum (Phase 6.1): movie metadata

Adds only what belongs to this ADR's scope - the provider-neutral metadata boundary and
its Cinemeta implementation. Decisions 1-11 above are unchanged (including Decision 5,
Season 0).

- **Movies use the same boundary.** `IMetadataProvider` gained `GetMovieAsync(externalId)`
  returning a provider-neutral `MovieMetadata` (external ids, title, year, runtime,
  release timestamp). Lookup is by a known stable id only; external-id discovery/search by
  title is explicitly outside this decision and not implemented.
- **Provider DTOs stay in Infrastructure.** `CinemetaMetadataProvider` serves
  `meta/series/{id}.json` and `meta/movie/{id}.json` through one shared HTTP/error path
  (same `HttpClient`, resilience pipeline, timeouts and error taxonomy as Decision 3);
  the Cinemeta envelope DTO is shared and never crosses into Application/Domain.
- **The release date is nullable at the boundary and never invented.**
  `MovieMetadata.ReleaseAtUtc` is `null` when Cinemeta has no usable date (Cinemeta does
  return `"released": null` for some real movies), mirroring Decision 4 for episodes.
- **`AddMovie` requires a reliable release date.** `Movie.Schedule` needs a release
  timestamp to decide Scheduled vs Pending, so metadata without one is rejected
  (`Movies.ReleaseDateUnavailable`, a validation error) instead of being scheduled with a
  fabricated date. Unlike `AddSeries` there is no best-effort fallback: a movie has nothing
  to be created from without metadata, so provider failures are returned to the caller.
  Title and year always come from the provider - the command carries only the IMDb id.
- **Duplicates are rejected, not idempotent.** Adding a movie whose canonical IMDb id
  already exists is a Conflict (`Movies.AlreadyExists`) - checked for the requested id
  (before any provider call) and again for the provider's canonical id when it differs.
  This deliberately differs from `AddSeries`, which returns the existing id.
- **Cinemeta's HTTP-200 "unknown id" responses are interpreted per media type, and
  conservatively.** Cinemeta answers 200 (not 404) for ids it does not know, and there is
  no single stub format: the representation differs by media type, and each is recognized
  only by the shape actually observed live for *that* type.
  - *Series:* an unknown id answers an empty top-level object `{}` -> `SeriesNotFound`.
  - *Movie:* an unknown id answers a meta object with exactly `id`, `type` (`"movie"`) and
    `behaviorHints` -> `MovieNotFound`.

  A shape observed for one type is deliberately **not** assumed for the other: a
  movie-style stub returned for a series lookup, or `{}` returned for a movie lookup, is an
  unrecognized response and stays `InvalidResponse`, as does anything else lacking usable
  data (a real media object missing its name, `{"meta":null}`, extra unrecognized fields,
  ...). Unrecognized variations fail safe as `InvalidResponse` rather than being guessed
  into `NotFound`; if Cinemeta is later observed to use another representation, it is added
  explicitly per type. The recognition lives in one Infrastructure class
  (`CinemetaUnknownMedia`); the mapper stays strict and knows nothing about not-found
  semantics. An HTTP 404 from Cinemeta remains `SeriesNotFound`/`MovieNotFound` as in
  Decision 3. This supersedes the earlier behavior where an unknown series id surfaced as
  `InvalidResponse` (HTTP 500).
- **`CatalogSynchronizer` stays series-specific.** It exists to reconcile seasons and
  episodes; a movie has no such structure, so nothing was generalized.
