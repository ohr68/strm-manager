# ADR-010: STRM filesystem safety

## Status

Accepted

## Context

`FileSystemStrmWriter` writes a `.strm` file to a path built from series/episode data
that ultimately traces back to an external metadata provider (Cinemeta) - untrusted
input, from the filesystem's perspective. This ADR records the path-construction,
sanitization, traversal-protection, and write-atomicity decisions.

## Decision 1: fixed path format, Jellyfin-compatible

```
{RootPath}/tv/{SeriesTitle} ({SeriesYear})/Season {SeasonNumber:D2}/{SeriesTitle} - S{SeasonNumber:D2}E{EpisodeNumber:D2}.strm
```

`RootPath` comes from configuration (`Strm:RootPath`, default `/stream` in production,
matching the Docker volume mount); everything after it is computed, never supplied
directly by a caller.

## Decision 2: sanitization uses a fixed, OS-independent denylist

`SanitizePathComponent` strips `..` sequences outright, then replaces any of
`< > : " / \ | ? *` (the Windows-reserved set) or a control character with `_`.

This is **not** `Path.GetInvalidFileNameChars()`, and that omission is deliberate:
`GetInvalidFileNameChars()` returns a platform-dependent set - a much smaller one on
Linux than on Windows. The service always runs in a Linux container, but its `.strm`
output is browsed from Jellyfin, which is very commonly reached over SMB from Windows
clients in this project's actual deployment. Sanitizing with the Linux-only set would
let characters like `:` `?` `<` `>` `|` survive into a filename that is valid where it
was written but broken wherever else it's read - exactly the kind of bug that only shows
up on the consuming side, long after the writing side looks correct. Using the larger,
fixed, Windows-reserved set as a superset closes that gap on every platform, at the cost
of being marginally more restrictive on Linux-only deployments than strictly necessary -
an acceptable trade favoring cross-platform correctness over that platform's own maximum
permissiveness. This was caught and fixed proactively while writing
`FileSystemStrmWriterTests`, before it could manifest as a real, hard-to-reproduce bug in
the field.

## Decision 3: traversal protection is two independent layers

1. `SanitizePathComponent` removes `..` from each component *before* it is combined into
   a path, so a malicious title like `"../../evil"` cannot inject navigation up the tree
   through the component itself.
2. After the full candidate path is built, `Path.GetFullPath` resolves it and
   `IsUnderRoot` checks it still starts with the configured root (with a trailing
   separator, so `/stream-evil` cannot pass a naive `StartsWith("/stream")` check against
   root `/stream`). If it doesn't, the write is refused with
   `StrmWriterErrors.PathEscapesRoot` - not silently corrected.

Layer 2 exists as defense in depth even though layer 1 should already prevent an escape -
verified directly in `FileSystemStrmWriterTests.WriteEpisodeAsync_TitleWithPathTraversalSequence_DoesNotEscapeRoot`
and `WriteEpisodeAsync_TitleWithEmbeddedSlashes_DoesNotCreateNestedDirectories`.

## Decision 4: atomic write via temp-file-then-rename

The file is first written in full to `{directory}/.{Guid}.strm.tmp`, then moved to the
final path with `File.Move(tempPath, candidatePath, overwrite: true)`. A reader (Jellyfin
scanning the library, or a user browsing over SMB) can never observe a partially-written
`.strm` file - it either sees the old content or the fully-written new content, never a
half-written intermediate state. `overwrite: true` makes re-processing an episode
(picking a different candidate on a later run) idempotent without a separate delete step;
`FileSystemStrmWriterTests.WriteEpisodeAsync_CalledTwice_OverwritesExistingFileAtomically`
verifies both the overwrite and that no leftover `.tmp` file remains.

## Consequences

- `.strm` file content is the raw candidate URL - the trust boundary this crosses (the
  stream URL, which the codebase otherwise never logs, ends up readable in a file on
  disk) is intentional and unavoidable: that URL is exactly what Jellyfin needs to stream
  the content, and `.strm` files are meant to be read by the media server, not treated as
  secrets. This differs from the URL-logging privacy rule, which is about not putting the
  URL in *logs* (a much wider-audience, longer-retained surface than one file Jellyfin
  itself needs to open).
- Only `WriteEpisodeAsync` exists - no `WriteMovieAsync` yet, since movie processing is
  out of scope for this phase (see [ADR-008](ADR-008-media-processing-module-boundary.md)
  Consequences). The same sanitization/traversal/atomicity rules will apply to it
  unchanged when it's added.
- `SeriesTitle`/`SeriesYear` come from `Catalog.Domain`'s `Series` entity as of the time
  processing runs - if a later metadata refresh changes the series title, a previously
  written `.strm` file is not renamed/moved; the next successful processing run for that
  episode will write to the new path, potentially leaving an orphaned file at the old
  path. Not addressed in this phase (no metadata-refresh-triggered file cleanup) - noted
  as a known gap rather than silently accepted.
