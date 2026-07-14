<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Data Layer Foundation (F-01) Implementation Plan

- **Plan**: `context/changes/data-layer-foundation/plan.md`
- **Mode**: Deep
- **Date**: 2026-07-13
- **Verdict**: REVISE
- **Findings**: 2 critical 1 warning 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | FAIL |

## Grounding

8/8 paths ✓, 6/6 symbols ✓, brief↔plan ✓

## Findings

### F1 — Phase 1 cannot build with seeding hooks before the seeder exists

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 — DbContext DI registration; Phase 3 — CSV Runtime Seeding
- **Detail**: Phase 1 requires `Program.cs` to attach `.UseSeeding(...).UseAsyncSeeding(...)` and then pass `dotnet build`, but the actual `WarsawVenueSeeder` is not introduced until Phase 3. Implementing Phase 1 literally either references a missing type/method or silently skips a Phase 1 contract. Phase 3 already has a separate “Wire seeder into provider options” task, so the duplicated Phase 1 hook is the source of the contradiction.
- **Fix**: Move all seeding-hook wiring out of Phase 1 and keep it only in Phase 3. Phase 1 should register `ChoNaBojoContext` with Npgsql + NetTopologySuite only, with no `WarsawVenueSeeder` reference.
- **Decision**: FIXED — moved seeding-hook wiring out of Phase 1 and kept it in Phase 3.

### F2 — No concrete path for migrations to use the session-mode connection

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 1 — Connection string configuration; Phase 4 — Apply migration
- **Detail**: The plan correctly says migrations must run through Supabase session mode on port 5432, while runtime uses transaction mode on port 6543. But the only concrete DbContext registration reads `ConnectionStrings:AppDb`, and Phase 1 defines that key as the runtime/transaction-mode string. Phase 4 says to run `dotnet ef database update --project server`, but does not specify `--connection`, a separate migration key, an env override, or a design-time factory. As written, the EF CLI can easily use the runtime pooler and violate the plan’s own migration prerequisite.
- **Fix A ⭐ Recommended**: Add a migration-only connection path: document `ConnectionStrings:AppDbMigrations` as the session-mode 5432 string and update Phase 4 commands to pass it via `dotnet ef database update --project server --connection "<session-mode-string>"`.
  - Strength: Keeps production runtime config unchanged and avoids adding design-time-only application code.
  - Tradeoff: Operators must use the exact EF command form when applying migrations.
  - Confidence: HIGH — the issue is the missing command/config contract, not a data-model uncertainty.
  - Blind spot: CI/deployment migration automation is not in this change.
- **Fix B**: Add an `IDesignTimeDbContextFactory<ChoNaBojoContext>` that reads a migration-specific session-mode key.
  - Strength: Makes EF tooling consistently use the intended connection without relying on a long command.
  - Tradeoff: Adds a design-time code path to maintain before the app has any other data-layer infrastructure.
  - Confidence: MEDIUM — workable, but more surface area than this MVP phase appears to need.
  - Blind spot: Factory behavior under Railway/Supabase env naming still needs to be specified.
- **Decision**: FIXED — applied Fix A; documented `ConnectionStrings:AppDbMigrations` and required `dotnet ef database update --project server --connection "<session-mode-string>"`.

### F3 — Idempotency key is left as “Name, or Name+Address”

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 — CSV seeder contract
- **Detail**: The desired end state promises repeated `database update` runs add no duplicate venues, but Phase 3 leaves the natural key open as “Name, or Name+Address.” The current CSV has 100 rows, 0 duplicate names, sport ids 1–10, and 208 venue-sport links, so either key works today; the ambiguity is still a future-maintenance trap because two implementers could choose different duplicate behavior.
- **Fix**: Specify one canonical key, preferably exact `(Name, Address)`, and add a duplicate-key check in the seeder smoke verification.
- **Decision**: FIXED — fixed differently; the plan now adds a stable integer `id` column to `data/warsaw-venues.csv`, maps it to `Venue.Id`, validates unique positive ids, and uses `Venue.Id` as the idempotency key.
