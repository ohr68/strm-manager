# ADR-003: `BackgroundService`, not Hangfire

## Status

Accepted

## Context

Evently uses Quartz to run the Outbox/Inbox processors on an interval. The legacy
PowerShell/Python system relies on a human manually triggering "process next episode" /
"process season" - there is no automatic scheduler at all today.

STRM Manager needs a scheduler for two jobs: promoting `Scheduled` episodes/movies to
`Pending` once their release date passes, and retrying `Unavailable`/`Error` items whose
`NextAttemptAtUtc` has arrived. Both are simple polling loops over a small table.

## Decision

Use plain ASP.NET Core `BackgroundService` implementations (`ReleaseProcessing`,
`RetryProcessing` - to be added in the `Scheduling` module), each on its own timer,
querying `IEpisodeRepository`/`IMovieRepository` for due work. No Hangfire, no Quartz, no
persistent job store beyond the `Catalog` tables that already record `ReleaseAtUtc`/
`NextAttemptAtUtc`/`AttemptCount`.

Concurrency is bounded explicitly (a configurable `MaxConcurrentEpisodeProcessing`, via
`SemaphoreSlim` or `Channel<T>` - whichever turns out simplest once the processing
pipeline is implemented), not by a job-queue library's own concurrency model.

## Consequences

- One fewer moving part (and one fewer NuGet dependency with its own storage
  requirements) than Hangfire, which wants its own tables/dashboard and is built for a
  scale of background-job traffic this project doesn't have.
- Restart safety comes from the domain model, not from the scheduler: since `Episode`/
  `Movie` never persist a "currently processing" state (see the Episode state machine in
  [architecture.md](../architecture.md)), a `BackgroundService` restart just means the
  next tick re-queries the same `Pending`/due rows - no separate job-recovery logic
  needed.
- If job volume or scheduling complexity ever outgrows a couple of timer loops, this ADR
  would need revisiting - not expected for a single-household media library.
