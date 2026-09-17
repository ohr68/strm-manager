# ADR-005: Catalog relational integrity (FK-only relationships, `Restrict` delete)

## Status

Accepted

## Context

`Series`, `Season` and `Episode` are deliberately flat, FK-linked entities with no
navigation properties (see [architecture.md](../architecture.md) and
[ADR-001](ADR-001-modular-monolith.md)) - `Series` does not expose a `Seasons`
collection, `Season` does not expose an `Episodes` collection or a `Series` reference.
This was intentional (the scheduler needs to query `Episode`s directly by status/date
without loading a full `Series` graph), but the Phase 1 checkpoint found that the EF Core
configurations never told EF about the relationship at all - `SeasonId`/`SeriesId` were
plain indexed columns with no `FOREIGN KEY` constraint, so the database could not prevent
a `Season` row pointing at a nonexistent `Series`, or an `Episode` row pointing at a
nonexistent `Season`.

## Decision

**Keep the FK-only, no-navigation domain model**, but configure the relationship at the
EF Core level using the non-navigation overload of `HasOne`/`WithMany`:

```csharp
// SeasonConfiguration
builder.HasOne<Series>()
    .WithMany()
    .HasForeignKey(season => season.SeriesId)
    .OnDelete(DeleteBehavior.Restrict)
    .IsRequired();

// EpisodeConfiguration
builder.HasOne<Season>()
    .WithMany()
    .HasForeignKey(episode => episode.SeasonId)
    .OnDelete(DeleteBehavior.Restrict)
    .IsRequired();
```

This gives EF Core (and therefore the underlying SQLite `FOREIGN KEY` constraint) full
knowledge of the relationship - and therefore referential integrity - without adding a
single navigation property to the domain entities. `Series`/`Season` still don't know
their children exist; the database still refuses to let a child exist without its parent.

**`DeleteBehavior.Restrict` for both relationships**, explicitly (not left to EF's
default, which for a required relationship is `Cascade`). A `Series` with existing
`Season`s cannot be deleted; a `Season` with existing `Episode`s cannot be deleted; both
attempts throw at `SaveChanges` time instead of silently deleting the entire subtree.

**SQLite foreign key enforcement is explicitly turned on** in `CatalogModule.AddCatalogModule`
via `SqliteConnectionStringBuilder { ForeignKeys = true }`. This is not optional
boilerplate: Microsoft.Data.Sqlite does not guarantee FK enforcement is on by default for
every connection, and an `OnDelete`/`HasForeignKey` configuration that SQLite never
actually enforces would be a worse trap than no configuration at all (it would look
correct in code review and in the migration, and silently do nothing at runtime). This is
covered by `test/StrmManager.Modules.Catalog.IntegrationTests/Persistence/CatalogRelationshipsTests.cs`,
which asserts both that valid Series->Season and Season->Episode rows persist, and that
rows referencing a nonexistent parent throw `DbUpdateException` on save.

### Why `Restrict`, not `Cascade`

Cascading delete was rejected because catalog deletion semantics haven't been designed
yet - "remove a series" might eventually need to mean "delete the seasons/episodes rows
and also delete their `.strm` files and clean up `SourceAttempt` history and possibly
notify Jellyfin", none of which exists yet. A silent, automatic multi-table cascade at
the database layer would happily delete all of that data the moment *anyone* deletes a
`Series` row for any reason (including a future bug), with no chance to hook in that
richer behavior. `Restrict` forces deletion to be a deliberate, explicit operation
(delete children first, or implement a real "retire a series" use case later) rather than
an accidental side effect.

### Why not `SetNull`

`SeriesId`/`SeasonId` are non-nullable `Guid` FK properties on the domain entities (an
`Episode` without a `Season` is not a meaningful state) - `SetNull` isn't applicable
without changing the domain shape, which is out of scope for this pass.

## Migration

Added as `AddCatalogForeignKeys` (does not modify `InitialCreate`). Adds two
`FOREIGN KEY ... ON DELETE RESTRICT` constraints:

- `FK_seasons_series_SeriesId`
- `FK_episodes_seasons_SeasonId`

## Consequences

- Orphaned `Season`/`Episode` rows are now impossible - enforced by the database, not
  just by application-layer discipline.
- Deleting a `Series`/`Season` that still has children will throw `DbUpdateException`
  until a real "cascade delete with cleanup" use case is designed and implemented
  deliberately - this is intentional friction, not a bug.
- `SourceAttempt`/`StrmFile` (also FK-linked to `Episode`/`Movie` by a nullable
  `EpisodeId`/`MovieId`, one or the other set) were **not** given equivalent FK
  constraints in this pass - they have no current writers (see the Phase 1 checkpoint),
  so adding constraints for relationships nothing exercises yet was left out to keep this
  change focused; revisit when `MediaProcessing` starts writing them.
