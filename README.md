# STRM Manager

STRM Manager is the .NET replacement for a two-part experimental system (PowerShell on
Windows + a Python backend on Ubuntu) that keeps a Jellyfin library populated with
`.strm` files for movies and TV series, sourced automatically from stream providers and
validated with `ffprobe` before being written to disk.

This repository holds the **server** side: a single, self-contained ASP.NET Core service
meant to run continuously in Docker on a home server. Windows is not part of the target
architecture - it only exists today as the legacy client being replaced.

## Status

Phase 1 (foundation) and Phase 1.1 (hardening) are done: solution structure, the
`Catalog` module's domain model (Series/Season/Episode/Movie lifecycle), SQLite
persistence with referential integrity, global exception handling, and the test/Docker
scaffolding. Phase 2 added metadata: given an IMDb series id, STRM Manager retrieves
metadata from Cinemeta and persists/refreshes its Series/Season/Episode graph
idempotently. Phase 3 added the media processing pipeline: `POST /api/episodes/{id}/process`
finds candidate streams (FrostStream), validates each with `ffprobe` (duration tolerance,
codec checks), and writes the first approved candidate to a Jellyfin-compatible `.strm`
file. Processing is explicit/on-demand only - there is still no scheduler, so nothing
runs automatically yet; see [docs/architecture.md](docs/architecture.md) for the full
plan.

## Architecture

STRM Manager is a **modular monolith**. Each module is a vertical slice with its own
Domain / Application / Infrastructure / Presentation projects; modules only talk to each
other through public interfaces registered in DI, never through internal types.

```
                         Jellyfin
                            ^
                            |
                          .strm
                            |
                    STRM Manager Server
                            |
        +-------------------+
        |                   |
     Catalog             MediaProcessing
 (Series/Season/     (FrostStream discovery/
  Episode/Movie,       ffprobe validation/
  + metadata sync)        .strm writing)
```

See [docs/architecture.md](docs/architecture.md) for the dependency diagram, the Episode
state machine, and the architectural decisions (ADRs) behind these choices - including
why this is deliberately *not* a copy of the [Evently](../Evently) reference architecture's
full complexity (no MediatR, no Outbox/Inbox, no Postgres/Redis/MassTransit).

## Modules

There is no separate `Providers` module - metadata retrieval turned out to be small
enough to live inside `Catalog` (`Catalog.Application/Metadata/`,
`Catalog.Infrastructure/Metadata/Cinemeta/`). See
[ADR-007](docs/adr/ADR-007-metadata-provider-and-catalog-synchronization.md).
`MediaProcessing` similarly gets only `Application`/`Infrastructure` projects (no
`Domain`/`Presentation`) - see [ADR-008](docs/adr/ADR-008-media-processing-module-boundary.md).

| Module | Status | Responsibility |
|---|---|---|
| `Catalog` | Implemented | Series, Season, Episode, Movie, SourceAttempt, StrmFile, plus metadata retrieval (`IMetadataProvider`/Cinemeta), synchronization (`CatalogSynchronizer`), and the `ProcessEpisode` orchestrator. |
| `MediaProcessing` | Implemented (episodes only) | `IStreamProvider` (FrostStream), `IMediaValidator` (ffprobe), `IStrmWriter` (`.strm` writing). No movie support yet. |
| `Scheduling` | Planned | `BackgroundService`s for release processing and retries - processing today is explicit/on-demand only. |

## Stack

- .NET 10, ASP.NET Core Minimal APIs
- EF Core 10 + SQLite
- FluentValidation
- xUnit + NetArchTest.Rules

Deliberately **not** used (see [ADR-001](docs/adr/ADR-001-modular-monolith.md) and
[ADR-002](docs/adr/ADR-002-sqlite.md)): PostgreSQL, Redis, MassTransit/RabbitMQ, Hangfire,
Kubernetes, MediatR.

## Running locally

```bash
dotnet restore
dotnet build
dotnet test
```

Run the API:

```bash
cd src/Api/StrmManager.Api
dotnet run
```

Pending EF Core migrations are applied automatically on every startup, in every
environment (this service owns its SQLite file end-to-end - there is no separate
deploy-time migration step). In `Development` the SQLite file is created under
`src/Api/StrmManager.Api/data/` (gitignored). Then:

```bash
curl http://localhost:5221/health

# Adding a series retrieves its metadata from Cinemeta inline (best-effort - the series
# is still created even if Cinemeta is unreachable or has no entry for this id).
curl -X POST http://localhost:5221/api/series \
  -H "Content-Type: application/json" \
  -d '{"imdbId":"tt27497393","title":"Placeholder","year":2026}'

curl http://localhost:5221/api/series/{id}/seasons
curl http://localhost:5221/api/seasons/{seasonId}/episodes

# Re-sync later (idempotent - safe to call repeatedly):
curl -X POST http://localhost:5221/api/series/{id}/refresh

# Process one episode once it's Pending (finds a stream, validates it with ffprobe,
# writes a .strm file):
curl -X POST http://localhost:5221/api/episodes/{episodeId}/process
```

## Configuration

Standard ASP.NET Core configuration (`appsettings.json`, `appsettings.{Environment}.json`,
environment variables). The connection string is:

```json
{
  "ConnectionStrings": {
    "Database": "Data Source=/app/data/strm-manager.db"
  }
}
```

Override it with the `ConnectionStrings__Database` environment variable in Docker/Compose.

Metadata provider (Cinemeta), validated at startup:

```json
{
  "Metadata": {
    "Cinemeta": {
      "BaseUrl": "https://v3-cinemeta.strem.io/",
      "TimeoutSeconds": 10
    }
  }
}
```

`BaseUrl` defaults to the public Cinemeta addon (not machine-specific, safe to ship as a
default) - override via `Metadata__Cinemeta__BaseUrl`/`Metadata__Cinemeta__TimeoutSeconds`
if needed. HTTP resilience (timeout + a couple of retries on transient failures only,
never on 404) is configured once in `CatalogModule` - see
[ADR-007](docs/adr/ADR-007-metadata-provider-and-catalog-synchronization.md).

Stream provider (FrostStream) and media validation (ffprobe), both validated at startup:

```json
{
  "StreamProviders": {
    "FrostStream": {
      "BaseUrl": "https://froststream.cloutteam.com/",
      "TimeoutSeconds": 15
    }
  },
  "MediaValidation": {
    "Ffprobe": {
      "ExecutablePath": "ffprobe",
      "TimeoutSeconds": 30,
      "EpisodeRuntimeTolerancePercentage": 35,
      "MinimumEpisodeDurationSeconds": 1200
    }
  },
  "Strm": {
    "RootPath": "/stream"
  }
}
```

`ExecutablePath` relies on `ffprobe` being on `PATH` (installed in the Docker image - see
Docker below); override with an absolute path if needed.
`EpisodeRuntimeTolerancePercentage`/`MinimumEpisodeDurationSeconds` are the two duration
rules ffprobe validation applies - never both at once, see
[ADR-009](docs/adr/ADR-009-ffprobe-process-safety.md). `Strm:RootPath` is where `.strm`
files are written (mounted as the `/stream` Docker volume in production); see
[ADR-010](docs/adr/ADR-010-strm-filesystem-safety.md) for the path/sanitization rules.

`Processing:UnavailableRetryDelay` (default `06:00:00`) controls how far in the future
`NextAttemptAtUtc` is set when an episode is marked `Unavailable` - informational only in
this phase, since nothing yet reads it automatically (no scheduler).

## Database & migrations

SQLite, managed through EF Core migrations (no `EnsureCreated`). To add a new migration
after changing the `Catalog` module's entities:

```bash
dotnet tool update -g dotnet-ef --version 10.0.12   # once, to match the EF Core version used here
dotnet ef migrations add <Name> \
  --project src/Modules/Catalog/StrmManager.Modules.Catalog.Infrastructure \
  --startup-project src/Api/StrmManager.Api \
  --output-dir Database/Migrations
```

`Microsoft.EntityFrameworkCore.Design` is referenced only by `StrmManager.Api` (the
`--startup-project`), not by `Catalog.Infrastructure` - that's the only place the `dotnet
ef` tooling actually needs it. Don't add it back to the migrations project itself unless
a future startup project stops referencing `Catalog.Infrastructure` transitively.

## Tests

```bash
dotnet test
```

- `test/StrmManager.Modules.Catalog.UnitTests` - Episode lifecycle/state machine, Cinemeta
  mapping (fixture-based, see `Metadata/Fixtures/`), `CinemetaMetadataProvider` HTTP/error
  handling (fake `HttpMessageHandler`, no live Cinemeta).
- `test/StrmManager.Modules.MediaProcessing.UnitTests` - episode-identity matching,
  `FrostStreamProvider` HTTP/error handling (fake `HttpMessageHandler`, no live
  FrostStream), ffprobe JSON-parsing rules as a pure function (no process involved -
  includes the legacy-validated duration/tolerance examples), ffprobe process-invocation
  failure paths (missing executable, rejected URL schemes - runnable with no ffprobe
  installed at all), and `FileSystemStrmWriter` path/sanitization/atomicity behavior.
- `test/StrmManager.Modules.Catalog.IntegrationTests` - API + SQLite, via `WebApplicationFactory`,
  including the full add/refresh/discover-new-episode flow against a `FakeMetadataProvider`,
  and the full `POST /api/episodes/{id}/process` flow against
  `FakeStreamProvider`/`FakeMediaValidator` and a temp `.strm` root
  (`ApiWebApplicationFactory` registers all three fakes by default for every test in this
  project - no test ever hits live Cinemeta/FrostStream or spawns a real ffprobe process).
- `test/StrmManager.ArchitectureTests` - layering rules (Domain -> Application -> Infrastructure/Presentation) and module boundaries (`Catalog` <-> `MediaProcessing`), enforced with NetArchTest.

## Docker

```bash
docker build -t strm-manager .
docker run -p 8080:8080 \
  -v strm-manager-data:/app/data \
  -v /srv/data/stream:/stream \
  strm-manager
```

Debian's `ffmpeg` package (which bundles `ffprobe`) is installed in the runtime image.
`/stream` is where `.strm` files are written - mount it to wherever Jellyfin's library
scan point is.

## Main endpoints (current)

| Method | Path | Description |
|---|---|---|
| GET | `/health` | Liveness/readiness, including database connectivity. |
| POST | `/api/series` | Adds a series by IMDb id (idempotent) and synchronizes its metadata inline, best-effort. |
| GET | `/api/series/{id}` | Returns a series by id (not its seasons/episodes - see below). |
| POST | `/api/series/{id}/refresh` | Re-syncs a series against the metadata provider; idempotent, preserves `Completed`/`Unavailable` episode state. |
| GET | `/api/series/{id}/seasons` | Lists a series' seasons. |
| GET | `/api/seasons/{id}/episodes` | Lists a season's episodes. |
| POST | `/api/episodes/{id}/process` | Runs the processing pipeline for one episode (find streams, validate, write `.strm`); idempotent if already `Completed`; 409 Conflict if not yet eligible (e.g. still `Scheduled`). |

`GET /api/series/{id}` intentionally does not return the full season/episode graph by
default (it can get large) - use the dedicated seasons/episodes endpoints. Response
bodies never include the underlying stream URL - only the selected source's provider
name and the resulting `.strm` path.

More endpoints (movies, retry, status) will be added as the corresponding use cases and
modules are implemented - see [docs/architecture.md](docs/architecture.md#incremental-plan).

## Related projects

- **Evently** ([`../Evently`](../Evently)) - the reference architecture this project's
  module/layer conventions are based on. Read-only reference; never modified by this
  project.
- **StrmManager.Jellyfin** (future, separate repository) - a thin Jellyfin plugin that
  will consume this service's HTTP API. Not part of this repository.
