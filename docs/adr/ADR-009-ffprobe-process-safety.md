# ADR-009: ffprobe execution and process safety

## Status

Accepted

## Context

`FfprobeMediaValidator` shells out to a real external process (`ffprobe`) with a URL
supplied by an external, untrusted stream provider. This ADR records the trust-boundary
and process-safety decisions, and the duration-tolerance rules ported from the legacy
Python validator.

## Decision 1: no shell, `ArgumentList` only

`ProcessStartInfo.UseShellExecute = false`, and every argument (including
`candidate.Url`) is added via `ArgumentList.Add(...)`, never via a concatenated
`Arguments` string. This is not a style preference - it is the only correct way to pass
an externally-supplied string to a child process without a command-injection risk. A
malicious or malformed candidate URL (e.g. containing shell metacharacters) can at worst
be passed as a single, literal ffprobe argument; it can never be interpreted by a shell.

## Decision 2: URL scheme allowlist before the process ever starts

Before `ProcessStartInfo` is even constructed, `candidate.Url` is parsed with
`Uri.TryCreate(..., UriKind.Absolute, ...)` and rejected unless the scheme is exactly
`http` or `https`. `file://`, `ftp://`, and anything else are rejected with
`SourceAttemptResult.ProbeError` before ffprobe is invoked at all - verified in
`FfprobeMediaValidatorTests.ValidateAsync_NonHttpUrl_IsRejectedWithoutInvokingFfprobe`
(pointed at a nonexistent ffprobe binary, so the test only passes if the scheme check
short-circuits first). ffprobe itself can read local files and several other protocols
- without this check, a compromised or buggy stream provider could hand back a `file://`
URL and have the server's own filesystem probed and partially leaked through ffprobe's
JSON output (an SSRF-adjacent risk, scoped to the local filesystem rather than internal
network services).

## Decision 3: bounded, non-blocking output capture

`ReadBoundedAsync` reads stdout/stderr concurrently with `WaitForExitAsync` (avoiding the
classic deadlock where a child process blocks writing to a full pipe while the parent is
still blocked waiting for exit), and caps captured output at `MaxCapturedOutputBytes`
(4 MiB) per stream. A misbehaving or malicious binary at the configured `ExecutablePath`
producing unbounded output cannot exhaust server memory.

## Decision 4: timeout with process-tree kill

`FfprobeOptions.TimeoutSeconds` (default 30) bounds `WaitForExitAsync` via a linked
`CancellationTokenSource` (timeout token + the caller's own cancellation token, so a
processing-run cancellation also cancels an in-flight probe). On timeout,
`TryKillProcessTree` calls `process.Kill(entireProcessTree: true)` - not just the direct
child - since ffprobe can spawn its own subprocesses for some inputs, and a lone
`process.Kill()` would leak them. The timeout branch is distinguished from a caller-
initiated cancellation by checking `timeoutCts.IsCancellationRequested &&
!cancellationToken.IsCancellationRequested`, mirroring the same pattern already used in
`CinemetaMetadataProvider`/`FrostStreamProvider` for HTTP timeouts (see
[ADR-007](ADR-007-metadata-provider-and-catalog-synchronization.md)).

## Decision 5: missing executable is a `Result`, never an unhandled exception

`Process.Start` throwing `Win32Exception` (executable not found) or
`InvalidOperationException` is caught and mapped to `SourceAttemptResult.ProbeError` -
an expected, diagnosable outcome (a fresh deployment without ffprobe installed, or a
misconfigured `ExecutablePath`), not a crash. This is also what makes
`FfprobeMediaValidatorTests` runnable on a Windows dev machine with no ffprobe installed
at all: the "missing executable" path is exercised directly and deterministically,
without needing the real binary anywhere.

## Decision 6: duration tolerance rules (ported from the legacy validator)

`FfprobeOutputParser.EvaluateDuration` applies exactly one of two rules, never both:

- **Expected runtime known** (`MediaValidationReference.ExpectedRuntime` present):
  approve if `|found - expected| / expected <= EpisodeRuntimeTolerancePercentage`
  (default 35%). Verified against the two legacy-validated numeric examples: expected
  3120s/found 3003.88s (~3.72% off, approved) and expected 3120s/found 2089.07s (~33.04%
  off, still approved, edge-of-tolerance).
- **No expected runtime**: approve if `found >= MinimumEpisodeDurationSeconds` (default
  1200s = 20 minutes) - a safety floor rather than inventing an expected value, matching
  [ADR-007](ADR-007-metadata-provider-and-catalog-synchronization.md) Decision 4's "never
  invent a release date" principle applied to runtime instead.

A stream is also rejected as `InvalidMedia` if it has no valid video stream (an
image-only codec such as `png`/`mjpeg`/`bmp`/`gif`/`tiff` - e.g. embedded cover art -
does not count as video) or no audio stream at all, regardless of duration.

## Decision 7: `codec_type`/`codec_name` deserialize via explicit `JsonPropertyName`

ffprobe's JSON output uses snake_case keys (`codec_type`, `codec_name`), but
`FfprobeOutputParser`'s `JsonSerializerOptions` uses `JsonSerializerDefaults.Web`
(camelCase) for consistency with the rest of the codebase's HTTP client DTOs. Without
`[JsonPropertyName("codec_type")]`/`[JsonPropertyName("codec_name")]` on
`FfprobeStreamDto`, camelCase's naming policy never matches ffprobe's actual keys, both
properties silently deserialize to `null`, and every probe falls through to "no valid
video stream" before duration is ever evaluated - a real bug caught while writing
`FfprobeOutputParserTests` (all-null codecs against a stream with real video/audio
content), not by a failing production report. `Streams`/`Format`/`Duration` are single
words and happen to match either naming policy, which is why only the two `codec_*`
fields needed the explicit attribute.

## Decision 8: health check reports `Degraded`, never `Unhealthy`

`FfprobeHealthCheck` runs `ffprobe -version` with a 5s timeout and is registered with
`failureStatus: HealthStatus.Degraded`. A missing/broken ffprobe means episode
*processing* cannot succeed, but every other capability (catalog browsing, metadata
sync, adding series) works fine without it - `Unhealthy` would be a false signal to any
orchestrator that restarts/reroutes traffic away from unhealthy instances, when nothing
about the instance itself is actually broken.

## Consequences

- `ExecutablePath` (default `"ffprobe"`, relies on PATH) and `TimeoutSeconds` are the
  only two required pieces of Docker configuration - the image must have ffprobe on
  PATH (see the Dockerfile: Debian's `ffmpeg` package bundles it).
- The scheme allowlist means `IStreamProvider` implementations could in principle return
  non-http(s) candidates without this layer catching it any earlier - `FrostStreamMapper`
  already filters to http/https at the mapping stage (defense in depth, not reliance on
  a single layer).
- Not addressed in this phase: no configurable retry for a transient ffprobe failure
  (a single probe failure is one `SourceAttempt`, not retried within the same processing
  run - the pipeline simply moves on to the next candidate, or `Unavailable`/`Error` if
  none remain).
