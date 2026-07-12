---
change_id: data-layer-foundation
doc_type: external-research
title: Library research — data layer (Supabase Postgres + EF Core migrations + seeding)
status: draft
created: 2026-07-12
updated: 2026-07-12
roadmap_ref: F-01
sources: exa.ai web search
---

## Scope

External research for **F-01 (data-layer-foundation)**: which libraries stand up the
data layer — Postgres connection to Supabase, migration tooling, and reference-data
seeding — that are compatible with the ratified stack (`context/foundation/tech-stack.md`:
.NET 10 / ASP.NET Core Web API, PostgreSQL, Supabase per `infrastructure.md`).

## Recommended stack (summary)

```
Npgsql.EntityFrameworkCore.PostgreSQL   10.0.2   (EF Core provider for Postgres)
Microsoft.EntityFrameworkCore.Design    10.x     (design-time, migrations)
dotnet-ef                               10.x     (CLI tool)
```

- **Migrations:** EF Core Migrations (no third-party migration library needed).
- **Seeding:** EF Core `HasData()` in `OnModelCreating` (or `UseSeeding`/`UseAsyncSeeding` for runtime seeding).
- **Supabase connectivity:** connection string only (no extra package) — via the Supavisor pooler.

## 1. PostgreSQL provider (required core)

- **`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2** — the official open-source EF Core
  provider for PostgreSQL. v10.0 released **2025-11-22**, synced to **EF Core 10 (LTS,
  supported until Nov 2028)**. Pulls in the underlying `Npgsql` ADO.NET driver.
- Wire-up (minimal API / DI):
  ```csharp
  builder.Services.AddDbContextPool<AppDbContext>(opt =>
      opt.UseNpgsql(builder.Configuration.GetConnectionString("AppDb")));
  ```
- Note: EF Core 10 requires the **.NET 10 SDK/runtime** — aligns with the tech-stack.

Sources:
- https://www.nuget.org/packages/npgsql.entityframeworkcore.postgresql/
- https://www.npgsql.org/efcore/release-notes/10.0.html
- https://github.com/npgsql/efcore.pg/releases/tag/v10.0.0
- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew

## 2. Migration tooling — options considered

| Option | Fit for F-01 | Notes |
|---|---|---|
| **EF Core Migrations** (`Microsoft.EntityFrameworkCore.Design` + `dotnet-ef`) | ✅ **Recommended** | Fully integrated with the Npgsql provider already being added — zero extra dependency. Model-first: `dotnet ef migrations add`, `dotnet ef database update`. Matches roadmap's "EF Core / migrations configured". |
| **FluentMigrator** | Alternative | Better for raw-SQL control or a standalone migration console (CI-friendly). Adds a second tool not tied to EF Core — overkill for 3 reference tables. |
| DbUp / Liquibase | Not chosen | Script/changelog-based; heavier than needed for MVP reference tables. |

**Decision for F-01:** EF Core Migrations — least tooling, tied to the provider already required, matches the roadmap's implied approach.

Sources:
- https://stackoverflow.com/questions/49023174/benefits-of-fluent-migrator-over-ef-migrations
- https://techpals.eu/2025/03/03/comparing-ef-core-dbup-fluentmigrator-and-liquibase/

## 3. Reference-data seeding

- **EF Core `HasData()`** in `OnModelCreating` — bakes the 10-sport lookup, Warsaw venues,
  and `VenueSports` join rows into a migration. Ideal for static reference data with stable
  ids (matches roadmap's id-keyed sport lookup requirement).
- **`UseSeeding` / `UseAsyncSeeding`** (EF Core 9+) — runtime seeding hooks; a fit if the
  Warsaw venue list is large and preferred as an external data file over migration-embedded data.

Source: EF Core provider docs (Npgsql EF Core provider getting-started; Microsoft EF Core seeding docs).

## 4. Supabase connectivity (critical config, not a library)

No extra NuGet package — Npgsql connects with a standard connection string. Two documented
gotchas that map directly to the roadmap's flagged F-01 risk (Supabase RLS / connection
string / pooler on Railway):

- **Use the Supavisor pooler hostname** (`...pooler.supabase.com`), NOT the direct
  `db.<project-ref>.supabase.co` host. Direct hosts are **IPv6-only** across all tiers;
  the pooler is **dual-stack** — this is the fix for Railway / GitHub Actions "no route to
  host" (IPv4-only runners).
- **Ports:** session mode = **5432**, transaction mode = **6543** (session mode on 6543 was
  deprecated 2025-02-28). Run **migrations against session mode (5432)**; the app runtime can
  use **transaction mode (6543)**. Transaction pooling drops session-scoped features
  (LISTEN, session `SET`, session advisory locks) — avoid relying on them.
- **Secrets:** keep the connection string in user-secrets / environment variables, never in
  `appsettings.json` (per repository hard rules).

Sources:
- https://supabase.com/docs/guides/database/connecting-to-postgres
- https://dotnetevangelist.net/blog/using-supabase-postgresql-with-aspnet-core-and-entity-framework-core/
- https://www.weweb.io/blog/supabase-connection-string-guide-ports-pooling

## Open follow-ups

- Confirm the exact EF Core 10 patch of `Microsoft.EntityFrameworkCore.Design` / `dotnet-ef`
  to pin alongside provider `10.0.2`.
- Decide seeding mechanism for Warsaw venues (`HasData` vs data-file + `UseAsyncSeeding`)
  once the venue dataset size is known.
- Verify Supabase RLS posture for API-only access (service role vs pooled app role).
