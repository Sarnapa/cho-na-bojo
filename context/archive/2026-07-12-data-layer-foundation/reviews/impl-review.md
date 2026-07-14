<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Data Layer Foundation (F-01) Implementation Plan

- **Plan**: `context/changes/data-layer-foundation/plan.md`
- **Scope**: Phases 1–4 of 4 (all complete)
- **Date**: 2026-07-15
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Evidence

- `dotnet build solutions/ChoNaBojo.slnx` → Build succeeded (2 warnings, 0 errors).
- `dotnet tool restore` → `dotnet-ef` 10.0.3 restored; `dotnet ef migrations list` shows `20260714180831_InitialCreate`.
- Migration emits `CREATE EXTENSION postgis`, three tables, GiST index on `Venues.Location`, unique `Sports.Code` index, and the 10 frozen `Sports` inserts (ids/codes/Polish names match the contract exactly). No Venue/VenueSport rows in migration (runtime-seeded) — matches plan.
- `data/warsaw-venues.csv` has the `id` column and 100 rows.
- `Program.cs` has no `Database.Migrate()` call — the "never auto-migrate" guardrail holds. Runtime uses `AppDb`; seeding hooks wired only in Phase 3 (prior plan-review F1 correctly applied). No `Users`/`Events`/endpoints/RLS added — scope guardrails respected.
- No secrets committed: `appsettings.Development.json` keeps only a commented `//ConnectionStrings` placeholder.

## Findings

### F1 — Venue seeding uses raw SQL + mixed persistence instead of the planned EF `Point` entity add

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: server/Data/Seeding/WarsawVenueSeeder.cs:88-116
- **Detail**: The plan's Phase 3 contract and Implementation Approach describe mapping each CSV row into a `Venue` entity "building each `Point` from the CSV lng/lat" and "check-then-adds venues + join rows" via EF. The implementation instead inserts venues with `ExecuteSqlInterpolated`/`ExecuteSqlInterpolatedAsync` raw SQL (`ST_SetSRID(ST_MakePoint(lng, lat), 4326)`, `ON CONFLICT DO NOTHING`) executed immediately per row, while `VenueSport` rows use EF tracked `Add` flushed later in a single `SaveChanges`. Consequences: (a) the persistence path diverges from the entity/`Point` approach the model and plan imply — `Venue.Location` is never written through the NTS mapping; (b) seeding is non-atomic — each venue auto-commits individually before the join-row batch, so a mid-seed failure can leave venues without their `VenueSports`. Both raw statements are parameterized, so there is no injection risk, and the idempotent guards (`ON CONFLICT`, existing-id/pair checks) recover partial state on re-run.
- **Fix A ⭐ Recommended**: Record the deviation as a plan addendum in `plan.md` — note that venue rows are seeded via parameterized raw SQL with `ST_MakePoint` (chosen over EF `Point` add) and that per-row inserts auto-commit while join rows batch.
  - Strength: Preserves working, verified code (Phase 4 manual checks 4.3–4.8 all passed against Supabase) and updates the source of truth before future reviews treat the plan as ground truth.
  - Tradeoff: Leaves the non-atomic seed as-is; a mid-run failure needs a re-run to reconcile.
  - Confidence: HIGH — behavior is verified end-to-end and idempotent.
  - Blind spot: Have not stress-tested a mid-seed crash; recovery relies on the re-run guard.
- **Fix B**: Switch venue insertion to EF (`dbContext.Venues.Add(new Venue { ..., Location = new Point(lng, lat) { SRID = 4326 } })`) so venues and join rows persist in one `SaveChanges` transaction.
  - Strength: Restores plan intent, makes the seed atomic, and exercises the NTS `Point` mapping the model already defines.
  - Tradeoff: Rewrites verified seeding code and re-tests against a PostGIS DB; must confirm NTS `Point` writes cleanly through `UseAsyncSeeding`.
  - Confidence: MEDIUM — NTS mapping should work, but the author may have hit a materialization issue that motivated raw SQL.
  - Blind spot: Unknown whether an EF-based `Point` add was tried and abandoned.
- **Decision**: FIXED via Fix A — deviation recorded as addendum A1 in plan.md

### F2 — Phase 3 manual success criteria missing from the Progress mirror

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/data-layer-foundation/plan.md (Progress → Phase 3 → Manual)
- **Detail**: The plan defines four Phase 3 manual verification items (row counts, spatial spot-check, idempotency, UTF-8), but the `#### Manual` block under Phase 3 in `## Progress` is empty — no `[ ]`/`[x]` lines. The equivalent DB-level checks were actually performed and recorded as Phase 4 manual items 4.3–4.8, so verification did happen; only the Phase 3 Progress mirror is incomplete.
- **Fix**: Add the four Phase 3 manual checkboxes to Progress (or annotate them as superseded by items 4.3–4.8) so completion math and future audits are accurate.
- **Decision**: FIXED — added Phase 3 manual items 3.3–3.6 to Progress, annotated as verified via Phase 4 items 4.3–4.8

### F3 — Transitive `Microsoft.OpenApi` 2.0.0 high-severity advisory (NU1903)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: server/server.csproj (transitive via `Microsoft.AspNetCore.OpenApi` 10.0.8)
- **Detail**: The build emits `NU1903: Package 'Microsoft.OpenApi' 2.0.0 has a known high severity vulnerability`. This is pre-existing — it arrives through the `Microsoft.AspNetCore.OpenApi` reference that predates this change; F-01 added only EF Core/Npgsql/NTS packages. Flagged for awareness, not as drift from this plan.
- **Fix**: Out of scope for F-01; track separately — bump `Microsoft.AspNetCore.OpenApi` (or pin a patched `Microsoft.OpenApi`) once a fixed version is available.
- **Decision**: SKIPPED — pre-existing and out of scope for F-01; tracked separately
