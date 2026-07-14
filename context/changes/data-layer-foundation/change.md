---
change_id: data-layer-foundation
title: Data layer foundation — Supabase Postgres connection, migrations, seeded sports & Warsaw venues
status: implemented
created: 2026-07-12
updated: 2026-07-14
archived_at: null
---

## Notes

Foundation slice **F-01** from `context/foundation/roadmap.md`. Stand up the data layer that every downstream slice depends on:

- Working Postgres connection (Supabase per `infrastructure.md`) + migration tooling (EF Core or Npgsql + FluentMigrator).
- Initial migration creating & seeding reference tables:
  - `Sports` — predefined list, seeded once with 10 disciplines: football, basketball, volleyball, tennis, running, cycling, rollerblading, gym, street workout, swimming. Referenced by stable id/code, never free-text.
  - `Venues` — Warsaw venues (manual seed per Non-Goals §1).
  - `VenueSports` — many-to-many join populated from the seed (used by S-02 filter and S-03 sport picker).

**PRD refs:** FR-003, Non-Goals §1, NFR (privacy/perf), tech-stack `database: PostgreSQL`.
**Prerequisites:** — (sequenced first; nothing else runs without the DB).
**Unlocks:** S-02, S-03, S-04, F-02, and every later slice.
**Risk:** Configuring Supabase RLS / connection string / pooler on Railway touches three systems at once. Domain tables (`Users`, `Events`, `JoinRequests`) are deferred to the slices that use them.

Related tracking: this change corresponds to GitHub issue **[F-01]** per `context/foundation/tasks-github.md` (roadmap → Issues mirror).
