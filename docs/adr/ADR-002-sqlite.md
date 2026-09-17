# ADR-002: SQLite, not PostgreSQL

## Status

Accepted

## Context

Evently uses PostgreSQL with one schema per module, snake_case naming
(EFCore.NamingConventions), and Dapper for read-side queries. That fits a system
designed to eventually scale beyond a single process/database instance.

STRM Manager runs on one home server. Its workload is a background scheduler processing
a handful of episodes/movies at a time, plus a small HTTP API for administration
(add series/movies, check status, retry). There is no concurrent-write-heavy workload,
no multi-instance deployment, and no operational appetite for running and backing up a
separate database server for this.

## Decision

Use SQLite as the only datastore, through EF Core with proper migrations (no
`EnsureCreated`). One `DbContext` per module (`CatalogDbContext` today), all pointing at
the same `.db` file, with tables distinguished by name (`series`, `episodes`, ...) rather
than by Postgres schema, since SQLite has no schema concept.

Concurrency: the scheduler (background processing) and the API share the same SQLite
file. EF Core `DbContext` instances stay scoped-per-operation with short-lived
transactions; no HTTP call or `ffprobe` invocation is ever made while a transaction is
open. WAL mode can be enabled later if contention becomes a measurable problem - it is
not enabled by default in this foundation because there is nothing yet to contend over.

## Consequences

- No read/write query split (no Dapper): with SQLite's scale, EF Core for both reads and
  writes is simpler and the "CQRS-lite" performance argument that justifies Dapper in
  Evently's Postgres setup does not apply here.
- Backups are "copy one file" instead of a database dump/restore procedure - appropriate
  for a home server.
- If STRM Manager ever needs true concurrent multi-writer throughput, this ADR would need
  to be revisited (Postgres, or at least SQLite WAL + a queue) - but that is a
  hypothetical, not a current requirement, so it isn't designed for now.
