# ADR-014: Enable SQLite WAL and a connection busy-timeout

## Status

Accepted

## Context

[ADR-002](ADR-002-sqlite.md) chose SQLite; Phase 1 deliberately left the journal mode at
its default (rollback journal, not WAL) since a single-instance server with no
background workers had no meaningful write concurrency to worry about. Phase 4 changes
that: the API host and two `Scheduling` `BackgroundService`s (`CatalogMaintenanceWorker`,
`EpisodeProcessingWorker`) now run concurrently, all against the same SQLite file, and
`GET /api/status`/`GET /api/episodes` are exactly the kind of read that can now overlap a
worker's write tick.

## Decision 1: enable WAL (Write-Ahead Logging)

`PRAGMA journal_mode=WAL;` is executed once, in `Program.cs`, immediately after
`dbContext.Database.MigrateAsync()` and before `app.Run()` (so before any
`BackgroundService` starts). WAL mode lets readers proceed without blocking on an
in-progress writer (and vice versa, for the common case) - rollback-journal mode takes an
exclusive lock for the whole duration of a write transaction, which would make e.g.
`GET /api/status` briefly block whenever `CatalogMaintenanceWorker` is mid-tick. WAL mode
is stored in the SQLite file's own header, so this call is idempotent across restarts
(a no-op once already WAL) - it is deliberately run once at startup, never per-request or
per-repository-call.

## Decision 2: a 30-second connection busy-timeout

`SqliteConnectionStringBuilder.DefaultTimeout = 30` is set in `CatalogModule` alongside
the existing `ForeignKeys = true` setting. Even under WAL, two writers can still
momentarily contend (SQLite allows only one writer at a time). Without a busy-timeout, a
`SQLITE_BUSY` under contention surfaces immediately as an exception; with it,
Microsoft.Data.Sqlite retries internally for up to the configured duration before giving
up. 30 seconds is generous relative to how quickly the actual write operations in this
codebase complete (a handful of small `INSERT`/`UPDATE` statements per `SaveChangesAsync`
call) - it is meant to absorb brief overlap between a maintenance tick and a manual API
write, not to mask a real, sustained problem.

## Consequences

- WAL mode adds a `-wal` and `-shm` file alongside the main SQLite database file - normal
  SQLite behavior, not addressed further here (no manual checkpointing is configured;
  SQLite's automatic checkpoint behavior is left at its default, which is adequate at
  this project's write volume).
- Docker volumes (`/app/data`) already persist the whole data directory, so these
  additional files are backed up/restored the same way the main database file already
  is - no change needed there.
- If write contention ever becomes a real, sustained problem at higher load, the next
  step would be reducing how much work happens inside a single `SaveChangesAsync`-guarded
  transaction, not switching database engines - see
  [ADR-002](ADR-002-sqlite.md)'s original reasoning for why SQLite remains appropriate
  for this project's scale.
