# ADR-015: Stable playback endpoint with just-in-time source resolution

## Status

Accepted - partially supersedes [ADR-010](ADR-010-strm-filesystem-safety.md): only the
Consequences bullet stating that `.strm` file content is the raw candidate URL (and the
rationale attached to it) no longer applies to movies. ADR-010's path-construction,
sanitization, traversal-protection and atomic-write decisions remain in force
unchanged. ADR-010 is intentionally left unedited as the historical record.

## Context

Until now a successfully processed movie's `.strm` file contained the provider media URL
that passed validation (see ADR-010, Consequences, and `ProcessMovieCommandHandler`,
which hands the approved candidate URL to `IStrmWriter.WriteMovieAsync`). That URL is
ephemeral: it is only valid for a limited time, so a `.strm` file that was correct when
written eventually points at a source that no longer plays.

`Movie.Status == Completed` therefore currently means "a `.strm` was written", which
says nothing about whether the URL inside it still works, and `ProcessMovie` never
revisits a `Completed` movie (Phase 6.2 idempotency: the handler returns early, with no
provider call and no rewrite).

## Evidence

Two throwaway spikes, run outside the product repository against the real provider and a
real, isolated Jellyfin 12.1 / FFmpeg, informed this decision. Their harnesses are not
part of this repository.

**P0-H (URL lifetime).** A previously persisted provider URL stopped working while a
newly resolved source for the same movie was still available: a ranged request to the
old URL was refused (HTTP 403), Jellyfin playback of the unchanged `.strm` failed, and
FFmpeg exited with an error. Re-resolving through the provider produced a usable source.

**P0-I (stable endpoint).** With the `.strm` pointing at a stable STRM Manager URL,
validated on real Jellyfin/FFmpeg:

- the stable URL can answer with HTTP 307;
- Jellyfin/FFmpeg follows the redirect;
- normal playback works;
- HTTP Range requests work;
- a distant seek works;
- FFmpeg can return to the stable endpoint for a subsequent Range request or reopen;
- a new playback session started more than 10 minutes later returned to the stable
  endpoint and triggered a new provider resolution;
- STRM Manager does not need to proxy video bytes.

## Decision 1: the movie `.strm` content is a stable STRM Manager URL

The `.strm` file written for a movie contains

```
{PublicBaseUrl}/media/{movieId}/stream
```

where `{movieId}` is the internal opaque `Movie.Id` (a `Guid`) - the same identifier
already used by `api/movies/{id}`. IMDb ids, TMDB ids and provider identifiers never
appear in the route. The URL is stable for the lifetime of the `Movie` row and contains
no provider data.

`PublicBaseUrl` is playback configuration: an absolute `http`/`https` URL under which
the Jellyfin server can reach STRM Manager. It is never inferred from an incoming
request's `Host` header.

The path format, sanitization, traversal protection and atomic write of the `.strm`
file itself (ADR-010) are unchanged. `IStrmWriter.WriteMovieAsync` stays
content-agnostic; only the string passed to it changes.

## Decision 2: request flow

```
Jellyfin/FFmpeg
  -> GET|HEAD {PublicBaseUrl}/media/{movieId}/stream          (stable endpoint)
  -> load Movie (must exist and be Completed)
  -> just-in-time provider discovery                          (existing IStreamProvider)
  -> positive-evidence identity validation                    (existing MovieIdentityValidator)
  -> media validation                                         (existing IMediaValidator / ffprobe)
  -> 307 Temporary Redirect, Location: <validated provider URL>
  -> Jellyfin/FFmpeg talks to the provider directly
```

Candidate selection at playback time applies the same policy as `ProcessMovie` (skip
candidates that require custom request headers, then identity validation, then media
validation, first approved candidate wins). That policy is shared, not duplicated: it is
extracted from `ProcessMovieCommandHandler` into one component used by both flows, so
the two cannot drift apart.

Identity validation is not weakened: a candidate must carry positive evidence (Confirmed
or Compatible) of being the requested movie, exactly as `ProcessMovie` requires today;
an undetermined candidate is rejected. Media validation
(ffprobe, [ADR-009](ADR-009-ffprobe-process-safety.md)) answers a different question -
is this URL readable now and plausibly the right length - and does not substitute for
identity. Both run on every fresh resolution, because a freshly returned provider list
is unrelated to the one that was approved when the movie was processed.

Playback-time resolution is read-only with respect to the catalog: it neither changes
`Movie` state nor persists `SourceAttempt` rows, and a playback failure does not move a
movie out of `Completed`.

## Decision 3: GET and HEAD contract

- `GET` and `HEAD` are both supported on `/media/{movieId:guid}/stream`. Minimal-API
  `MapGet` alone would answer `HEAD` with 405, so the endpoint maps both methods
  explicitly.
- `HEAD` follows the same resolution path, and returns the same status and headers as
  `GET`, with no body. The `Location` a client receives is always one that has just been
  validated; there is no cheaper, unvalidated `HEAD` answer.
- Any other method is rejected by routing (405).
- Range headers on the stable endpoint are not interpreted. The redirect carries no
  range semantics; the client re-issues its Range request against the `Location`
  (observed with Jellyfin/FFmpeg in P0-I).

## Decision 4: HTTP status behavior

| Situation | Response |
|---|---|
| Unknown movie id | 404 |
| Movie exists but is not `Completed` | 404 |
| Provider failure, no acceptable candidate, identity rejection, or media-validation failure | 503, generic |
| Resolution capacity or queue limit reached | 429 with `Retry-After` |
| Success | 307 Temporary Redirect, `Location` set, empty body |

Internal `Movie` status and failure detail are never exposed: a non-`Completed` movie is
indistinguishable from an unknown one, and every resolution failure returns the same
generic response regardless of which stage failed. Failures fail closed - the endpoint
never redirects to a candidate that did not pass identity and media validation.

## Decision 5: redirects must not be cached

Every successful response carries `Cache-Control: no-store`. The `Location` is a
short-lived, per-resolution value, and a client, proxy or the media server caching it
would recreate the expiry problem this ADR exists to remove. The 307 status (not 301,
302 or 308) is used because the redirect is temporary and must preserve the request
method.

## Decision 6: provider URL confidentiality

A complete provider media URL must not appear in:

- persisted `.strm` content;
- normal logs or diagnostics, including exception messages and logged exception objects;
- exception responses or any other API response body;
- `SourceAttempt` persistence or any other database column.

The single intentional exception is the `Location` header of the successful 307
response: it is the only place the resolved URL may leave the service.

Consequences for implementation:

- ffprobe receives the URL as an argument and may echo it in its error output. The
  current `FfprobeMediaValidator` logs a truncated copy of ffprobe's standard error on
  failure. Removing that (logging only the exit code and a classified category) is a
  **prerequisite**, delivered before playback-time resolution is exposed.
- The redirect is written directly by the endpoint. Framework result helpers that log
  their destination must not be used for it.
- Global exception handling and transport-level logging must not carry a URL to a
  client or a log sink.
- Logging outside STRM Manager (Jellyfin, FFmpeg, the provider) is outside this ADR's
  control.

This replaces ADR-010's rationale that a raw URL in the `.strm` is acceptable because
the file is meant to be read by the media server. With a stable URL in the file, there
is no longer any reason for a provider URL to be at rest anywhere in STRM Manager's
persisted output.

## Decision 7: STRM Manager stays outside the video data plane

Video bytes flow between the client and the provider only. STRM Manager answers a
small redirect and does not proxy, buffer, transcode or rewrite media. This keeps
bandwidth, connection count and playback latency independent of STRM Manager and was
sufficient in the P0-I validation.

## Decision 8: single-flight resolution, explicitly not a cache

Playback requests for the same movie often arrive in a burst (a player opens the file,
probes it, and re-opens or seeks). Resolving each of them independently would repeat
provider discovery and ffprobe.

- Concurrent requests for the same `Movie` share **one** in-progress resolution.
- When that resolution is no longer in flight (completed, failed or cancelled), its
  entry is removed.
- A request that arrives after removal starts a **fresh** resolution.

This is single-flight, not caching: nothing is retained after the resolution finishes.
No provider result, validated URL or validation verdict is kept, and failures are not
remembered either, so the next request retries.

The shared work must not depend on request-scoped resources or on the first caller's
cancellation: one waiter disconnecting does not cancel the others, and exceptions are
converted to a generic outcome inside the shared work rather than handed to waiters.
Concurrent resolutions and per-resolution work are bounded; when the bound is reached
the endpoint answers 429 with `Retry-After` (Decision 4).

Single-flight state is in-process. This is consistent with the single-instance
deployment implied by [ADR-002](ADR-002-sqlite.md) and
[ADR-014](ADR-014-sqlite-wal-and-busy-timeout.md); it is not coordinated across
multiple STRM Manager instances.

## Decision 9: no post-resolution cache in the initial implementation

The initial implementation has **no** TTL or reuse window for resolved URLs or
validation results. A TTL cache would trade freshness and provider/ffprobe cost against
the risk of redirecting to a URL that has since expired, and choosing its policy
requires measurements that do not exist yet: playback burst timing for a real client,
provider URL lifetime lower bound, and provider and ffprobe latency. A reuse window may
be reconsidered later from measured data, in its own ADR or an amendment to this one.

## Decision 10: what `Completed` means

`Completed` now means the **stable playback artifact was successfully provisioned**:
the movie was identified, at least one candidate passed identity and media validation at
processing time, and the stable `.strm` was written. It does not mean any provider URL is
permanently valid, and it does not guarantee that playback resolution will succeed at any
later moment.

`ProcessMovie`'s `Completed` idempotency is unchanged: a `Completed` movie is still
returned as-is without re-running discovery or rewriting its `.strm`.

## Decision 11: existing Completed `.strm` files are not migrated

Movies that were `Completed` before this change still have a provider URL in their
`.strm` and keep it. This implementation does not rewrite them and does not change the
`Completed` early return to do so. Migrating or backfilling them is recorded as a
separate follow-up that needs its own explicit approval and design.

## Decision 12: a missing `PublicBaseUrl` prevents a new claim

If `PublicBaseUrl` is not configured (or is not a valid absolute `http`/`https` URL),
`ProcessMovie` fails **before** claiming or otherwise mutating the `Movie`. The movie
stays in its current state, no provider or ffprobe work is performed, and no `.strm`
containing a provider URL is written as a fallback. The application must still start
without this setting so that existing deployments and non-playback features are not
broken by it.

## Non-goals

This ADR and the change it describes do not include:

- Phase 6.3 recovery/retry, stale-processing sweeps, or any `Movie` state change caused
  by a playback failure;
- proxying or otherwise carrying video bytes;
- the Jellyfin plugin;
- episodes and series - episode `.strm` files still contain the provider URL and have the
  same ephemeral-URL problem; this is a separate backlog item;
- production or staging deployment configuration changes;
- authentication or authorization changes;
- migration or backfill of existing `Completed` movie `.strm` files;
- TTL selection or any post-resolution result caching.

## Consequences

- **Playback now depends on STRM Manager being available.** If it is down, a stable
  `.strm` cannot play, where a raw provider URL would have played until it expired.
- **Provider and ffprobe work move partly to playback time.** Every new playback session
  can trigger discovery and media validation, adding latency to playback start and load
  on the provider.
- **Single-flight controls duplicate simultaneous work only.** Sequential requests each
  resolve afresh, by design, so they always get a current source.
- **Resolution capacity is bounded** and overload is visible to clients as 429 rather
  than as unbounded queueing.
- **Playback failures are non-mutating.** A movie whose sources disappear stays
  `Completed` and returns 503 at playback; nothing in this design repairs or reclassifies
  it.
- **Existing tests and docs change.** Assertions that a movie `.strm` equals the provider
  URL become assertions that it equals the stable URL and that no provider URL is
  persisted anywhere. `docs/architecture.md` is updated alongside the implementation that
  makes these statements true, not with this ADR.
- **Later caching may be considered** using measured burst behavior, URL lifetime and
  latency data, without changing the stable `.strm` contract.
- **Episodes remain exposed** to the ephemeral-URL problem until they are addressed
  separately.
