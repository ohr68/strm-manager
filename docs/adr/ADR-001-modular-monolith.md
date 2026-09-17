# ADR-001: Modular monolith, not microservices

## Status

Accepted

## Context

The reference architecture, [Evently](../../../Evently), is a modular monolith with four
modules (Events, Ticketing, Users, Attendance) that communicate through integration
events over MassTransit (in-memory transport), backed by an Outbox/Inbox pattern for
reliable, idempotent delivery, and a `PublicApi` project per module for synchronous
cross-module reads.

STRM Manager runs on a single home server, processes a low, bursty volume of work
(a handful of episodes/movies at a time, gated by external release dates and provider
availability), and currently has one real consumer boundary: `Catalog` produces data that
`Providers`/`MediaProcessing`/`Scheduling` consume directly, in-process.

## Decision

Build STRM Manager as a modular monolith: one ASP.NET Core process, one deployable
Docker image, modules separated by project boundaries (Domain/Application/Infrastructure/
Presentation) and enforced with architecture tests - but **not** microservices, and
**not** Evently's full eventing machinery:

- No MassTransit, no message broker, no Outbox/Inbox tables, no Quartz-scheduled event
  dispatch. `IDomainEvent`/`Entity.Raise()` exist as a hook for the day a real in-process
  consumer needs them, dispatched synchronously if/when that happens.
- No `PublicApi` project per module. With four small modules and no plan to ever split
  them into separate deployables, an extra "public surface" project per module is
  ceremony without a payoff - a plain internal service reference registered in DI is
  the entire mechanism Evently's `PublicApi` boils down to once you remove the
  cross-process concern.
- Module isolation is still enforced the way Evently does it: `test/StrmManager.ArchitectureTests`
  asserts Domain/Application/Infrastructure/Presentation dependency direction with
  NetArchTest, so the codebase stays honest about boundaries even without a message bus
  forcing them.

## Consequences

- Cross-module calls are plain constructor-injected interfaces, resolved in the same
  process, in the same transaction where useful. Simpler to reason about, debug, and
  test than an eventual-consistency pipeline - correct for a single-writer, single-process
  system.
- If a module ever needs to become an independently deployable service (unlikely for a
  home server, but the boundaries would make it possible), the interfaces at each
  module's Application layer are the natural seam - re-introducing MassTransit/Outbox at
  that point is an additive change, not a rewrite.
- We accept that domain events are inert today. This is intentional per "don't add
  events without a real consumer" - re-evaluate once `Scheduling` needs to react to
  `EpisodeCompleted`/`EpisodeBecameUnavailable`.
