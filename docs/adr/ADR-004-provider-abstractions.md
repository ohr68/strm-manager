# ADR-004: Provider abstractions for metadata, streams, and validation

## Status

Accepted

## Context

The legacy system hard-codes Cinemeta (metadata), FrostStream (stream discovery), and
`ffprobe` (validation) directly into the PowerShell script and the Python backend. That
was fine for a prototype but means swapping or adding a provider requires touching
call-site code throughout.

The briefing is explicit that the domain must not depend on any specific provider:
Cinemeta, FrostStream and ffprobe are implementation details, not requirements the
`Catalog` domain should ever import.

## Decision

Define the provider contracts at the `Application` layer of dedicated modules, with
`Catalog.Domain` types (`Episode`, `Movie`, `StreamCandidate` - a DTO, not a domain
entity) as their vocabulary, and implement them in each module's `Infrastructure` layer:

```csharp
// Providers.Application
public interface IMetadataProvider
{
    Task<Result<SeriesMetadata>> GetSeriesAsync(string externalId, CancellationToken cancellationToken);
}

public interface IStreamProvider
{
    Task<Result<IReadOnlyList<StreamCandidate>>> GetStreamsAsync(Episode episode, CancellationToken cancellationToken);
}

// MediaProcessing.Application
public interface IMediaValidator
{
    Task<Result<MediaValidationResult>> ValidateAsync(StreamCandidate candidate, MediaReference reference, CancellationToken cancellationToken);
}

public interface IStrmWriter
{
    Task<Result<string>> WriteEpisodeAsync(Episode episode, string sourceUrl, CancellationToken cancellationToken);
    Task<Result<string>> WriteMovieAsync(Movie movie, string sourceUrl, CancellationToken cancellationToken);
}
```

`CinemetaMetadataProvider`, `FrostStreamProvider`, and `FfprobeMediaValidator` are the
first (and, for now, only) implementations, registered behind these interfaces in each
module's DI registration - nothing outside `Providers.Infrastructure`/
`MediaProcessing.Infrastructure` references the concrete provider types.

Validation thresholds (duration tolerance, minimum runtime for episodes without a
reliable runtime, codec checks) are `Options` classes, not literals scattered through the
validator - configurable, not hard-coded, per the "no magic numbers" requirement.

## Consequences

- Adding a second metadata or stream provider later (e.g. TMDB metadata, another stream
  index) is an additive `Infrastructure` implementation plus a registration change - no
  change to `Catalog` or to any use case that already depends on the interface.
- Provider tests use fixture JSON and a fake `HttpMessageHandler`, never live HTTP calls -
  keeps CI deterministic and independent of third-party uptime.
- This ADR intentionally does not specify a selection/fallback strategy across multiple
  providers of the same kind (e.g. "prefer Cinemeta, fall back to TMDB") - that's a
  concrete need to design for when a second provider actually exists, not before.
