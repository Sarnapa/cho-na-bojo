# Data Layer Foundation (F-01) Implementation Plan

## Overview

Stand up the backend data layer that every downstream slice depends on: connect the ASP.NET Core Web API to Supabase Postgres via EF Core 10 + Npgsql, enable PostGIS, configure migration tooling, and create one initial migration that creates and seeds three reference tables — `Sports` (10 locked disciplines), `Venues` (Warsaw, with a spatial coordinate), and `VenueSports` (many-to-many join). Sports are seeded model-managed via `HasData`; the 100 Warsaw venues and their sport links are seeded at migrate-time via runtime `UseAsyncSeeding` reading `data/warsaw-venues.csv`. Venue coordinates are stored as a PostGIS `geometry(Point,4326)` via NetTopologySuite so the S-02 map viewport can query them against a GiST index. All user-facing text values (sport names, venue names/addresses/descriptions) are Polish; schema member names and sport `Code`s stay English.

## Current State Analysis

- The server (`server/server.csproj`) targets `net10.0` with `Nullable=enable`, `ImplicitUsings=enable`, `RootNamespace=ChoNaBojo.Server`, and references only `Microsoft.AspNetCore.OpenApi 10.0.8`. No EF Core, Npgsql, or ORM/migration tooling exists — the data layer is greenfield and additive, zero dependency conflicts.
- `server/Program.cs` is a standard minimal-API host: `WebApplication.CreateBuilder(args)`, Railway `PORT` binding, forwarded-headers config, `builder.Services.AddOpenApi()`, `/health` and sample `/weatherforecast` endpoints. DbContext DI registration drops into the same `builder.Services` block.
- `server/appsettings.json` and `appsettings.Development.json` hold no connection strings — consistent with the hard rule that connection strings live in user-secrets (dev) / env vars (prod).
- Local SDK is `10.0.300` (EF Core 10 requirement satisfied). `dotnet-ef` is **not installed** — a plan prerequisite, not a blocker.
- `data/warsaw-venues.csv` exists: 100 venues, header `nazwa,szerokosc_geograficzna,dlugosc_geograficzna,adres,opis,wspierane_dyscypliny`. Phase 3 will add a stable integer `id` column for venue identity. The `wspierane_dyscypliny` column is a comma-separated list of sport ids already matching the locked 1–10 Sports contract. Text values are Polish; the schema uses English member names.
- Infrastructure decision (`infrastructure.md`): Railway host + external Supabase Postgres, reached through the Supavisor pooler. Supabase supports the PostGIS extension.
- Downstream driver: S-02 map-venue-discovery renders venues within the current map viewport under a "< 2s pan/zoom" NFR — a spatial coordinate + index is required at the foundation to avoid a later column-type migration.

## Desired End State

After this plan, `dotnet ef database update` against the Supabase session-mode endpoint (port 5432) enables PostGIS, creates the `Sports`, `Venues`, and `VenueSports` tables (with a GiST index on the venue coordinate), and populates them: 10 sports (Polish display names, English codes), 100 Warsaw venues keyed by stable CSV ids (each with a `geometry(Point,4326)` location), and every venue→sport link from the CSV. The API host registers `ChoNaBojoContext` in DI with `.UseNetTopologySuite()` and a pooler + `SSL Mode=Require` connection string pulled from configuration (never committed). The app never auto-migrates. Re-running the migrate step is idempotent (no duplicate venues). Downstream slices (S-02 viewport/filter, S-03 sport picker) can query these tables spatially.

Verification: `dotnet build solutions/ChoNaBojo.slnx` succeeds; `dotnet ef migrations list` shows `InitialCreate`; after `database update`, row counts are 10 / 100 / (sum of CSV discipline ids), a bbox query returns venues in a viewport, and a second `database update` adds no duplicate rows.

### Key Discoveries:

- Seeding mechanism decided in research (`npgsql-docs.md` §6): `HasData` for stable reference rows requires explicit PKs and explicit FK values.
- `UseAsyncSeeding` **is** triggered by `dotnet ef database update` (and `Migrate()`), but **not** by `EnsureCreated()` — verified. The app never calls `Migrate()`, so the CSV seed fires only during the explicit migrate step, exactly as intended.
- Supabase connectivity gotchas (`research-libraries.md` §4): use the Supavisor **pooler host** (dual-stack, IPv4-reachable from CI/Railway), migrations on **session mode 5432**, runtime on **transaction mode 6543**, `SSL Mode=Require`.
- The 10 `Sports` ids **and** English `Code`s are a **frozen contract** — downstream code references rows by id/code, never by the Polish display name; under `HasData`, changing a seeded PK is a delete+insert (`npgsql-docs.md` §6b).
- PostGIS spatial storage: Npgsql maps an NTS `Point` to `geometry(Point,4326)` when `.UseNetTopologySuite()` is enabled; the extension is registered via `modelBuilder.HasPostgresExtension("postgis")` so the migration emits `CREATE EXTENSION`. NTS `Point(x, y)` is `(longitude, latitude)`.
- Server settings must not change: keep `Nullable` and `ImplicitUsings` enabled (AGENTS.md hard rule).

## What We're NOT Doing

- No `Users`, `Events`, or `JoinRequests` tables — deferred to F-02 and the S-NN slices that use them.
- No API endpoints exposing Sports/Venues — F-01 is data layer only; query endpoints (including the actual viewport query) belong to S-02/S-03.
- No RLS policies on these tables for the MVP (public reference data; a single pooled app role). Privacy-boundary RLS lands with the deferred user/event tables.
- No app-startup auto-migration in any environment.
- No user-contributed venues, no venue-editing UI (Non-Goals §1) — the CSV is the manual source of truth.
- No localization/i18n framework — Polish is hard-coded as the single UI language for the MVP.
- No iOS/app-side changes.

## Implementation Approach

Build additively in the existing minimal-API host. Add the provider, NetTopologySuite, and design packages plus a committed `dotnet-ef` local tool manifest first so migrations are reproducible. Define POCO entities (venue coordinate as an NTS `Point`) and a `ChoNaBojoContext` whose `OnModelCreating` registers the PostGIS extension, configures keys/relationships + a GiST spatial index, and seeds the 10 sports via `HasData`. Attach `UseSeeding`/`UseAsyncSeeding` that parses the CSV, uses stable CSV venue ids for idempotency, and check-then-adds venues + join rows, building each `Point` from the CSV lng/lat. Finally generate the `InitialCreate` migration and apply it against Supabase session mode, which creates the schema (with PostGIS) and runs the CSV seed.

## Critical Implementation Details

- **Seeding trigger & pooling mode.** `UseAsyncSeeding` runs on `dotnet ef database update`. Run that command with `--connection "<session-mode-string>"` using `ConnectionStrings:AppDbMigrations` (**session-mode 5432**), not `ConnectionStrings:AppDb` — transaction pooling (6543) is reserved for app runtime. The seeder must be idempotent (query-then-add) because the migrate step can run repeatedly.
- **HasData explicit values.** Every `Sport` row needs an explicit `Id` (1–10, frozen) and `Code`; EF will not generate them for seed rows.
- **Venue identity.** Add an integer `id` column to `data/warsaw-venues.csv` with stable values starting at 1. Map it to `Venue.Id`, configure venue ids as explicit values, and make the seeder fail on missing, non-positive, or duplicate venue ids. This CSV id is the canonical idempotency key for MVP venue seeding.
- **PostGIS coordinate order.** NTS `Point` is `(x = longitude, y = latitude)` with `SRID = 4326`. The CSV columns are `szerokosc_geograficzna` (latitude) and `dlugosc_geograficzna` (longitude) — do not swap them when constructing the `Point`.
- **CSV parsing robustness.** `adres` and `opis` are quoted and contain commas; use a real CSV parser (`CsvHelper` or `Microsoft.VisualBasic.FileIO.TextFieldParser`), not naive `Split(',')`. `wspierane_dyscypliny` is a quoted comma-separated id list split after CSV field extraction.
- **CSV path resolution.** The seeder runs from the design-time/host working directory; resolve the CSV via a configurable path (default `data/warsaw-venues.csv` relative to content root / repo root), not a hard-coded absolute path.

## Phase 1: Provider, Spatial & Tooling Wiring

### Overview

Add the EF Core Npgsql provider, the NetTopologySuite spatial plugin, and the design-time package; commit a reproducible `dotnet-ef` local tool manifest; and register `ChoNaBojoContext` in DI with `.UseNetTopologySuite()` and a Supabase pooler + SSL connection string sourced from configuration. Do not wire CSV seeding hooks in this phase; `WarsawVenueSeeder` is introduced and wired in Phase 3.

### Changes Required:

#### 1. NuGet packages

**File**: `server/server.csproj`

**Intent**: Add the Postgres EF Core provider, the NetTopologySuite spatial plugin, and the design-time package for migrations. Keep existing settings untouched.

**Contract**: New `PackageReference`s — `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.3`, `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` `10.0.x` (matching patch), `Microsoft.EntityFrameworkCore.Design` `10.0.x`. Do not disable `Nullable`/`ImplicitUsings`.

#### 2. Local tool manifest

**File**: `.config/dotnet-tools.json` (new)

**Intent**: Pin `dotnet-ef` for reproducible CI/local migrations alongside the provider version. A global install may also be used for convenience, but the manifest is the committed source of truth.

**Contract**: `dotnet new tool-manifest` then `dotnet tool install dotnet-ef --version 10.0.*`, producing a manifest entry for `dotnet-ef` at `10.0.x`.

#### 3. Connection string configuration

**File**: `server/appsettings.Development.json` (key placeholder only), user-secrets, Railway env

**Intent**: Provide a named connection string the DbContext reads, without committing secrets. Document the two Supabase endpoints (session 5432 for migrations, transaction 6543 for runtime).

**Contract**: Configuration key `ConnectionStrings:AppDb` is the runtime transaction-mode string (`Port=6543`). Configuration key `ConnectionStrings:AppDbMigrations` is the migration-only session-mode string (`Port=5432`) used by EF CLI commands via `--connection`. Both forms use the Supavisor pooler host: `Host=<supavisor-pooler-host>;Port=<5432-or-6543>;Database=postgres;Username=<user>;Password=<secret>;SSL Mode=Require;Pooling=true`. Values live in user-secrets (dev) and Railway env vars (prod) — never in `appsettings.json`.

#### 4. DbContext DI registration

**File**: `server/Program.cs`

**Intent**: Register `ChoNaBojoContext` with the Npgsql provider + NetTopologySuite in the existing `builder.Services` block, next to `AddOpenApi()`. Keep CSV seeding hooks out of Phase 1.

**Contract**: `builder.Services.AddDbContext<ChoNaBojoContext>(opt => opt.UseNpgsql(builder.Configuration.GetConnectionString("AppDb"), npgsql => npgsql.UseNetTopologySuite()))`. No `UseSeeding`/`UseAsyncSeeding` wiring yet, and no call to `Database.Migrate()` anywhere — the app never auto-migrates.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Tool manifest resolves: `dotnet tool restore` then `dotnet ef --version` reports `10.0.x`
- Packages restored with no version conflict: `dotnet restore server`

#### Manual Verification:

- `appsettings.json` / `appsettings.Development.json` contain no password/connection secret
- User-secrets holds working `ConnectionStrings:AppDb` (transaction mode, 6543) and `ConnectionStrings:AppDbMigrations` (session mode, 5432) values pointing at the Supavisor pooler host

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding to the next phase.

---

## Phase 2: Domain Model & DbContext

### Overview

Define the three reference entities (venue coordinate as an NTS `Point`) and `ChoNaBojoContext`, register the PostGIS extension, configure keys/relationships + the GiST spatial index, and seed the 10 `Sports` rows model-managed via `HasData` (English codes, Polish display names).

### Changes Required:

#### 1. Entity types

**File**: `server/Data/Entities/Sport.cs`, `Venue.cs`, `VenueSport.cs` (new; namespace `ChoNaBojo.Server.Data.Entities`)

**Intent**: POCO entities with English member names holding Polish values where applicable. `VenueSport` is an explicit join entity (research Option A) so downstream code references it directly. Venue location is a spatial point, not two doubles.

**Contract**:
- `Sport`: `int Id` (explicit), `string Code` (required, unique, English stable identifier), `string Name` (Polish display name), nav `ICollection<VenueSport>`.
- `Venue`: `int Id` (explicit stable CSV id), `string Name` (Polish), `Point Location` (`NetTopologySuite.Geometries.Point`, SRID 4326), `string Address` (Polish), `string Description` (Polish), nav `ICollection<VenueSport>`.
- `VenueSport`: `int VenueId`, `int SportId`, navs `Venue`, `Sport`; composite key `(VenueId, SportId)`.

#### 2. DbContext

**File**: `server/Data/ChoNaBojoContext.cs` (new)

**Intent**: `DbContext` exposing the three `DbSet`s and configuring the model in `OnModelCreating`, including PostGIS registration and the spatial index.

**Contract**: `DbSet<Sport> Sports`, `DbSet<Venue> Venues`, `DbSet<VenueSport> VenueSports`. In `OnModelCreating`: `modelBuilder.HasPostgresExtension("postgis")`; `Venue.Id` configured as explicit (`ValueGeneratedNever()`); `Venue.Location` mapped as `geometry(Point,4326)` with a **GiST index** (`HasIndex(v => v.Location).HasMethod("gist")`); `Sport.Code` required + unique index; `VenueSport` composite key `(VenueId, SportId)` with both FKs and cascade behavior; `Sports.HasData(...)` for the 10 locked rows. Seed contract (id, English `Code`, Polish `Name`):

| Id | Code | Name (PL) |
|----|------|-----------|
| 1 | `football` | Piłka nożna |
| 2 | `basketball` | Koszykówka |
| 3 | `volleyball` | Siatkówka |
| 4 | `tennis` | Tenis |
| 5 | `running` | Bieganie |
| 6 | `cycling` | Kolarstwo |
| 7 | `rollerblading` | Jazda na rolkach |
| 8 | `gym` | Siłownia |
| 9 | `street_workout` | Street workout |
| 10 | `swimming` | Pływanie |

No Venue/VenueSport `HasData` — those seed at runtime in Phase 3.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Model validates (no snapshot errors): `dotnet ef dbcontext info --project server`

#### Manual Verification:

- The 10 seeded sport ids/codes match the frozen contract exactly (no reordering, no id reuse); Polish `Name` values are correct and UTF-8-clean
- `Venue.Id` is an explicit stable CSV id; `Venue.Location` maps to `geometry(Point,4326)`; `Name`/`Description`/`Address` are single Polish strings; all schema member names are English

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding.

---

## Phase 3: CSV Runtime Seeding

### Overview

Implement the `UseSeeding`/`UseAsyncSeeding` logic that reads `data/warsaw-venues.csv`, maps rows into `Venue` (including stable CSV id and `Point` from lng/lat) + `VenueSport`, and check-then-adds them idempotently so repeated migrate runs never duplicate data.

### Changes Required:

#### 1. Stable venue ids in CSV

**File**: `data/warsaw-venues.csv`

**Intent**: Give every manually curated venue row a stable MVP identifier so idempotency does not depend on mutable venue names or addresses.

**Contract**: Add first column `id` with integer values starting at 1 and unique across all 100 rows. Keep existing Polish text columns and `wspierane_dyscypliny` values unchanged.

#### 2. CSV seeder

**File**: `server/Data/Seeding/WarsawVenueSeeder.cs` (new)

**Intent**: Parse the CSV robustly (quoted fields with embedded commas), map Polish columns to the English schema, build the spatial `Point`, resolve each `wspierane_dyscypliny` id to an existing `Sport`, and upsert venues + join rows guarded by existence checks.

**Contract**: A method invoked from both `UseSeeding` (sync) and `UseAsyncSeeding` (async). Column mapping: `id→Id`, `nazwa→Name`, `szerokosc_geograficzna→latitude`, `dlugosc_geograficzna→longitude` combined into `Location = new Point(longitude, latitude) { SRID = 4326 }`, `adres→Address`, `opis→Description`, `wspierane_dyscypliny→VenueSport rows`. Idempotency: validate `id` is present, positive, and unique in the CSV; skip venues already present by `Venue.Id`; only add missing join rows. Use a real CSV parser — never `Split(',')`. Resolve CSV path from content root / configurable setting, default `data/warsaw-venues.csv`.

#### 3. Wire seeder into provider options

**File**: `server/Program.cs`

**Intent**: Connect the seeder to EF Core `UseSeeding`/`UseAsyncSeeding` provider options so it runs at migrate time.

**Contract**: `.UseSeeding((ctx, _) => WarsawVenueSeeder.Seed(ctx, …))` and `.UseAsyncSeeding((ctx, _, ct) => WarsawVenueSeeder.SeedAsync(ctx, …, ct))`. Each guards with query-then-add and calls `SaveChanges`/`SaveChangesAsync`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- CSV parses without error and validates 100 unique positive venue ids in a smoke run (seeder unit/smoke test or `dotnet ef database update` against a local/dev PostGIS Postgres reads all 100 rows)

#### Manual Verification:

- After a migrate run, `Venues` has 100 rows with ids matching the CSV, non-null `Location`, and every CSV `wspierane_dyscypliny` id produced a `VenueSports` row
- A spatial spot-check (e.g. `ST_AsText(Location)` for "PGE Narodowy") shows the correct lng/lat order
- Re-running the migrate step adds zero duplicate venues or join rows (idempotent)
- Polish characters in `Name`/`Address`/`Description` persist correctly (UTF-8)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human before proceeding.

---

## Phase 4: Initial Migration & Apply

### Overview

Generate the `InitialCreate` migration from the model (including the PostGIS extension and GiST index) and apply it (plus the CSV seed) against Supabase session mode, then verify schema and data.

### Changes Required:

#### 1. Generate migration

**File**: `server/Migrations/*_InitialCreate.cs` + model snapshot (new, generated)

**Intent**: Produce the schema migration for PostGIS + the three tables (including the `Sports` `HasData` inserts and the spatial index).

**Contract**: `dotnet ef migrations add InitialCreate --project server`. The migration issues `CREATE EXTENSION postgis`, creates `Sports`, `Venues` (with `geometry(Point,4326)` `Location` + GiST index), `VenueSports` (composite join key), the unique `Sport.Code` index, and the 10 `Sports` inserts. Venues/VenueSports rows are NOT in the migration (runtime-seeded).

#### 2. Apply migration + seed

**File**: (operational step — no source change)

**Intent**: Apply the schema and trigger the CSV seed against Supabase via the session-mode endpoint.

**Contract**: `dotnet ef database update --project server --connection "<ConnectionStrings:AppDbMigrations session-mode string>"`. The connection string must use the Supavisor **session-mode port 5432**, not the runtime `ConnectionStrings:AppDb` transaction-mode port 6543. This enables PostGIS, creates the tables, inserts the 10 sports, and fires `UseAsyncSeeding` to load the 100 venues + joins.

### Success Criteria:

#### Automated Verification:

- Migration listed: `dotnet ef migrations list --project server` shows `InitialCreate`
- Solution builds after migration files added: `dotnet build solutions/ChoNaBojo.slnx`

#### Manual Verification:

- Against Supabase: `Sports` = 10 rows, `Venues` = 100 rows (all with `Location`), `VenueSports` = total of CSV discipline links; PostGIS extension present
- A bounding-box query (`Location && ST_MakeEnvelope(...)`) returns venues within a Warsaw viewport and uses the GiST index (`EXPLAIN`)
- A second `dotnet ef database update --project server --connection "<ConnectionStrings:AppDbMigrations session-mode string>"` is a no-op for data (no duplicate rows)
- Tables reachable through the Supavisor pooler with `SSL Mode=Require`; the app starts without auto-migrating

**Implementation Note**: After completing this phase and all automated verification passes, pause here for final manual confirmation from the human.

---

## Testing Strategy

### Unit Tests:

- CSV parser mapping: a quoted row with embedded commas in `adres`/`opis` yields correct field values.
- `Point` construction: latitude/longitude are not swapped — `Location.Y` = latitude, `Location.X` = longitude, SRID 4326.
- `wspierane_dyscypliny` splitting: `"5,6,7,3"` → four join rows with the right sport ids.
- CSV id validation: missing, non-positive, or duplicate venue ids fail the smoke test.
- Idempotency guard: seeding an already-present venue id adds no new venue row.

### Integration Tests:

- Full `database update` against a disposable PostGIS Postgres (Testcontainers `postgis/postgis` image or local) yields 10 / 100 / N rows; a second run is a data no-op; a bbox query returns expected venues.

### Manual Testing Steps:

1. Set `ConnectionStrings:AppDbMigrations` to the session-mode (5432) connection string in user-secrets; run `dotnet tool restore` then `dotnet ef database update --project server --connection "<ConnectionStrings:AppDbMigrations session-mode string>"`.
2. Query row counts for `Sports`, `Venues`, `VenueSports` in Supabase; confirm PostGIS is enabled and sport `Name`s are Polish.
3. Spot-check a venue (e.g. "PGE Narodowy") — Polish text intact, `ST_AsText(Location)` correct, expected sport links.
4. Run a bbox viewport query and confirm it returns venues.
5. Re-run `database update`; confirm no duplicates.
6. Start the API with the transaction-mode (6543) string; confirm it boots and never auto-migrates.

## Performance Considerations

Seeding runs once at migrate time, not per request — the 100-row load is trivial. The GiST index on `Venue.Location` makes S-02 viewport bbox queries index-accelerated, supporting the "< 2s pan/zoom" NFR. Runtime uses transaction-mode pooling; keep Npgsql `Maximum Pool Size` modest under pgbouncer transaction pooling and avoid session-scoped features (prepared-statement reliance, `LISTEN`, session `SET`).

## Migration Notes

First migration on an empty database — no existing data to migrate. `CREATE EXTENSION postgis` must succeed on Supabase (supported). If the CSV grows, no schema migration is needed (venues are runtime-seeded); a re-run of `database update` upserts new rows via the stable CSV id idempotency guard.

## References

- Research: `context/changes/data-layer-foundation/research.md`
- External library research: `context/changes/data-layer-foundation/research-libraries.md`
- Npgsql/EF Core reference: `context/changes/data-layer-foundation/npgsql-docs.md`
- Roadmap F-01: `context/foundation/roadmap.md` (lines 64–75)
- Host insertion point: `server/Program.cs` (`AddOpenApi()` service block)
- Venue source data: `data/warsaw-venues.csv`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Provider, Spatial & Tooling Wiring

#### Automated

- [x] 1.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — c5d8d3e
- [x] 1.2 Tool manifest resolves: `dotnet tool restore` then `dotnet ef --version` reports 10.0.x — c5d8d3e
- [x] 1.3 Packages restored with no version conflict: `dotnet restore server` — c5d8d3e

#### Manual

- [x] 1.4 appsettings files contain no password/connection secret — c5d8d3e
- [x] 1.5 User-secrets holds working `ConnectionStrings:AppDb` (6543) and `ConnectionStrings:AppDbMigrations` (5432) values pointing at the Supavisor pooler host — c5d8d3e

### Phase 2: Domain Model & DbContext

#### Automated

- [x] 2.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — 5eab4a0
- [x] 2.2 Model validates: `dotnet ef dbcontext info --project server` — 5eab4a0

#### Manual

- [x] 2.3 The 10 seeded sport ids/codes match the frozen contract exactly; Polish Name values correct and UTF-8-clean — 5eab4a0
- [x] 2.4 Venue.Id is an explicit stable CSV id; Venue.Location maps to geometry(Point,4326); Name/Description/Address are single Polish strings; schema member names are English — 5eab4a0

### Phase 3: CSV Runtime Seeding

#### Automated

- [x] 3.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — ec40efa
- [x] 3.2 CSV parses without error and validates 100 unique positive venue ids in a smoke run (all 100 rows read) — ec40efa

#### Manual

### Phase 4: Initial Migration & Apply

#### Automated

- [x] 4.1 Migration listed: `dotnet ef migrations list --project server` shows InitialCreate — d56e7d2
- [x] 4.2 Solution builds after migration files added: `dotnet build solutions/ChoNaBojo.slnx` — d56e7d2

#### Manual

- [x] 4.3 Against Supabase: Sports = 10, Venues = 100 (all with Location), VenueSports = total CSV links; PostGIS enabled — d56e7d2
- [x] 4.4 A bbox viewport query returns venues and uses the GiST index (EXPLAIN) — d56e7d2
- [x] 4.5 A second database update using `--connection "<ConnectionStrings:AppDbMigrations session-mode string>"` is a no-op for data (no duplicate rows) — d56e7d2
- [x] 4.6 Tables reachable through the Supavisor pooler with SSL Mode=Require; app starts without auto-migrating — d56e7d2
- [x] 4.7 Spatial spot-check shows correct lng/lat order (Location.X=longitude, Location.Y=latitude) — d56e7d2
- [x] 4.8 Polish characters in Name/Address/Description persist correctly (UTF-8) — d56e7d2
