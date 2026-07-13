# Data Layer Foundation (F-01) — Plan Brief

> Full plan: `context/changes/data-layer-foundation/plan.md`
> Research: `context/changes/data-layer-foundation/research.md`

## What & Why

Stand up the backend data layer that every downstream slice depends on: EF Core 10 + Npgsql connected to Supabase Postgres (with PostGIS), migration tooling, and one initial migration that creates and seeds three reference tables — `Sports`, `Venues`, `VenueSports`. Without this, no user-facing slice (auth, map, events) can run.

## Starting Point

The API (`server/`) is a clean `net10.0` minimal-API host with only `Microsoft.AspNetCore.OpenApi`; no EF Core, ORM, or DB code exists. `data/warsaw-venues.csv` holds 100 Warsaw venues (Polish text; a `wspierane_dyscypliny` column already keyed to the 1–10 sport ids) and will gain stable integer venue ids during this change. `dotnet-ef` is not yet installed.

## Desired End State

`dotnet ef database update --project server --connection "<ConnectionStrings:AppDbMigrations session-mode string>"` enables PostGIS, creates the three tables with a GiST index on the venue coordinate, and seeds 10 sports (English codes, Polish names), 100 venues keyed by stable CSV ids (each a `geometry(Point,4326)`), and all venue→sport links. The app registers the DbContext with `.UseNetTopologySuite()`, reads its runtime connection string from config/secrets, and never auto-migrates. Downstream S-02/S-03 can query venues spatially.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|----------|--------|------------------|--------|
| Provider & migrations | Npgsql provider 10.0.3 + EF Core Migrations | Additive, tied to the provider already required; no third-party tool | Research |
| Venue coordinate storage | PostGIS `geometry(Point,4326)` via NetTopologySuite | S-02 viewport bbox queries hit a GiST index; getting the type right now avoids a later column migration | Plan |
| Sports seeding | `HasData` (model-managed, explicit ids) | Static 10-row lookup with a frozen id/code contract | Research |
| Venue identity | Stable integer `id` column in `data/warsaw-venues.csv`, mapped to `Venue.Id` | Explicit ids make migrate-time seeding idempotent without relying on mutable names/addresses | Plan |
| Venue seeding | Runtime `UseAsyncSeeding` from the CSV | 100 rows kept out of the migration; venue list can grow without new migrations | Plan |
| Text language | Polish values (names/addresses/descriptions/sport names); English member names + sport `Code`s | App UI is Polish; stable identifiers stay English | Plan |
| Migration execution | Explicit `dotnet ef database update --project server --connection "<ConnectionStrings:AppDbMigrations session-mode string>"`; no app auto-migrate | Runtime uses transaction pooling (6543); `UseAsyncSeeding` still fires on the explicit session-mode update | Plan |
| DB role / RLS | Single pooled app role, RLS off for these reference tables | Public, read-mostly reference data; privacy RLS lands with user/event tables | Plan |
| dotnet-ef install | Committed local tool manifest (+ optional global) | Reproducible CI/migrations, version pinned to the provider | Plan |

## Scope

**In scope:** provider/spatial/tooling wiring, `Sport`/`Venue`/`VenueSport` entities + DbContext, PostGIS extension + GiST index, Sports `HasData` seed, stable venue ids in the CSV, CSV runtime seeding, `InitialCreate` migration + apply.

**Out of scope:** Users/Events/JoinRequests tables, API query endpoints, RLS policies, app-startup auto-migration, i18n framework, venue-editing UI, iOS/app changes.

## Architecture / Approach

Additive change to the existing minimal-API host. Packages + tool manifest → POCO entities (venue location as an NTS `Point`, `Venue.Id` from the CSV) + `ChoNaBojoContext` (`HasPostgresExtension("postgis")`, GiST index, Sports `HasData`) → a `WarsawVenueSeeder` wired into `UseSeeding`/`UseAsyncSeeding` that parses the CSV idempotently by stable venue id → generate and apply the `InitialCreate` migration against Supabase session mode, which both builds the schema and runs the CSV seed.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|-------|------------------|----------|
| 1. Provider, Spatial & Tooling Wiring | Packages, tool manifest, DbContext DI + connection string | Supabase pooler/SSL connectivity (IPv4 from CI) |
| 2. Domain Model & DbContext | Entities, PostGIS extension, GiST index, Sports seed | Locked sport id/code contract; correct spatial mapping |
| 3. CSV Runtime Seeding | Stable CSV ids plus idempotent venue + join seeding from CSV | CSV quoting; lng/lat order; duplicate/missing venue ids |
| 4. Initial Migration & Apply | `InitialCreate` migration applied + verified | `CREATE EXTENSION postgis` + session-mode apply |

**Prerequisites:** Supabase project + runtime `ConnectionStrings:AppDb` and migration `ConnectionStrings:AppDbMigrations` pooler connection strings in user-secrets; `dotnet-ef` via the committed manifest; SDK 10.0.300 (present).
**Estimated effort:** ~1–2 sessions across 4 phases.

## Open Risks & Assumptions

- Supabase permits `CREATE EXTENSION postgis` on the target project (supported by default).
- Migrations run with `ConnectionStrings:AppDbMigrations` against the Supavisor **session-mode** endpoint (5432), not transaction mode.
- The CSV's `wspierane_dyscypliny` ids stay aligned with the frozen 1–10 Sports contract.
- The CSV's stable venue `id` values stay unique and are not reused for different venues.

## Success Criteria (Summary)

- After migrate: 10 sports (Polish names) / 100 venues (with `Location`) / all CSV sport links present; PostGIS enabled.
- A bounding-box viewport query returns venues and uses the GiST index.
- A second migrate run adds no duplicate data by `Venue.Id`; the app boots without auto-migrating.
