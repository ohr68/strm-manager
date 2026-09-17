# STRM Manager

STRM Manager is the .NET replacement for a two-part experimental system (PowerShell on
Windows + a Python backend on Ubuntu) that keeps a Jellyfin library populated with
`.strm` files for movies and TV series, sourced automatically from stream providers and
validated with `ffprobe` before being written to disk.

This repository holds the **server** side: a single, self-contained ASP.NET Core service
meant to run continuously in Docker on a home server. Windows is not part of the target
architecture - it only exists today as the legacy client being replaced.

## Status

This is the **foundation** of the project: solution structure, the `Catalog` module's
domain model (Series/Season/Episode/Movie lifecycle), SQLite persistence, a minimal HTTP
API, and the test/Docker/CI scaffolding. Metadata providers (Cinemeta), stream discovery
(FrostStream), media validation (`ffprobe`) and the scheduler are **not implemented yet** -
see [docs/architecture.md](docs/architecture.md) for the full plan.

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
        +-------------------+--------------------+
        |                   |                    |
     Catalog            Providers            MediaProcessing
   (Series/Season/     (Metadata/Stream    (Validation/SourceSelection/
    Episode/Movie)        abstractions)        StrmGeneration)
```

See [docs/architecture.md](docs/architecture.md) for the dependency diagram, the Episode
state machine, and the architectural decisions (ADRs) behind these choices - including
why this is deliberately *not* a copy of the [Evently](../Evently) reference architecture's
full complexity (no MediatR, no Outbox/Inbox, no Postgres/Redis/MassTransit).

## Modules

| Module | Status | Responsibility |
|---|---|---|
| `Catalog` | Implemented | Series, Season, Episode, Movie, SourceAttempt, StrmFile - the domain and its persistence. |
| `Providers` | Planned | `IMetadataProvider` (Cinemeta) and `IStreamProvider` (FrostStream) abstractions. |
| `MediaProcessing` | Planned | `IMediaValidator` (ffprobe), source selection, `.strm` writing. |
| `Scheduling` | Planned | `BackgroundService`s for release processing and retries. |

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
curl -X POST http://localhost:5221/api/series \
  -H "Content-Type: application/json" \
  -d '{"imdbId":"tt27497393","tmdbId":"12345","title":"Paradise","year":2025}'
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

## Tests

```bash
dotnet test
```

- `test/StrmManager.Modules.Catalog.UnitTests` - Episode lifecycle/state machine.
- `test/StrmManager.Modules.Catalog.IntegrationTests` - API + SQLite, via `WebApplicationFactory`.
- `test/StrmManager.ArchitectureTests` - layering rules (Domain -> Application -> Infrastructure/Presentation), enforced with NetArchTest.

## Docker

```bash
docker build -t strm-manager .
docker run -p 8080:8080 \
  -v strm-manager-data:/app/data \
  -v /srv/data/stream:/stream \
  strm-manager
```

`ffprobe`/`ffmpeg` are not installed in the image yet - they will be added once the
`MediaProcessing` module (media validation) lands.

## Main endpoints (current)

| Method | Path | Description |
|---|---|---|
| GET | `/health` | Liveness/readiness, including database connectivity. |
| POST | `/api/series` | Adds a series by IMDb id (idempotent). |
| GET | `/api/series/{id}` | Returns a series by id. |

More endpoints (seasons, episodes, movies, retry, status) will be added as the
corresponding use cases and modules are implemented - see
[docs/architecture.md](docs/architecture.md#incremental-plan).

## Related projects

- **Evently** ([`../Evently`](../Evently)) - the reference architecture this project's
  module/layer conventions are based on. Read-only reference; never modified by this
  project.
- **StrmManager.Jellyfin** (future, separate repository) - a thin Jellyfin plugin that
  will consume this service's HTTP API. Not part of this repository.
