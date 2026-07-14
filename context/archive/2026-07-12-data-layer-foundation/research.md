---
date: 2026-07-12T22:58:00+02:00
researcher: Copilot CLI
git_commit: 33c8d7834aa185bc91fd7d4825caf552ea44ce51
branch: master
repository: Sarnapa/cho-na-bojo
topic: "F-01 data-layer-foundation — readiness to plan & Npgsql/EF Core compatibility with the codebase"
tags: [research, codebase, data-layer, npgsql, ef-core, supabase, migrations, seeding, F-01]
status: complete
last_updated: 2026-07-12
last_updated_by: Copilot CLI
---

# Research: F-01 data-layer-foundation — planning readiness & Npgsql compatibility

**Date**: 2026-07-12T22:58:00+02:00
**Researcher**: Copilot CLI
**Git Commit**: 33c8d7834aa185bc91fd7d4825caf552ea44ce51
**Branch**: master
**Repository**: Sarnapa/cho-na-bojo

## Research Question

Review the codebase and decide whether we have full knowledge to plan **F-01
(data-layer-foundation)** given the external research already stored in
`research-libraries.md` and `npgsql-docs.md`. Specifically: is **Npgsql
(`Npgsql.EntityFrameworkCore.PostgreSQL`) compatible with the current codebase**, and is F-01
ready for `/10x-plan`?

## Summary

**Verdict: Npgsql is fully compatible, and F-01 is ready to plan.** The external research is
accurate, current, and directly applicable — with one small version correction noted below.

- The server targets **`net10.0`** ([server.csproj:4](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/server.csproj#L4)) and the installed SDK is **10.0.300** (verified locally; `9.0.301` is also present). EF Core 10 / Npgsql provider 10.0.x require the .NET 10 SDK+runtime — **satisfied**.
- `Npgsql.EntityFrameworkCore.PostgreSQL` latest stable is **10.0.3** (NuGet, released ~2026-07-10) — the research pins **10.0.2**; both are EF Core 10 LTS and net10.0-compatible. **Recommend pinning `10.0.3`** (and matching `Microsoft.EntityFrameworkCore.Design` 10.0.x + `dotnet-ef` 10.0.x).
- No conflicting data-layer packages exist today — the server references only `Microsoft.AspNetCore.OpenApi 10.0.8` ([server.csproj:10](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/server.csproj#L10)). Adding the Npgsql provider is additive, no version clash.
- The API is minimal-API style with a plain `WebApplication.CreateBuilder` host ([Program.cs:3](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/Program.cs#L3)) — `AddDbContext`/`AddNpgsql`/`AddDbContextPool` DI wiring drops straight in alongside the existing `AddOpenApi()` call.
- `dotnet-ef` is **not currently installed** (verified: `dotnet ef --version` fails). This is a plan prerequisite, not a blocker.
- **One open decision remains before planning is fully unblocked:** the actual Warsaw venue dataset (`Venues` + `VenueSports` rows) does not exist in the repo yet. The 10-sport `Sports` list is fully specified; the venue seed content is not.

## Detailed Findings

### Current server project (the compatibility target)

- **TFM & language settings** — `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `RootNamespace=ChoNaBojo.Server` ([server.csproj:3-8](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/server.csproj#L3-L8)). Matches tech-stack (`.NET 10`) and AGENTS.md hard rules (do not disable nullable/implicit usings). Npgsql provider 10.0.x and EF Core 10 are built for exactly this TFM → **compatible**.
- **Only package today** — `Microsoft.AspNetCore.OpenApi 10.0.8` ([server.csproj:10](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/server.csproj#L10)). No EF Core, no Npgsql, no other ORM/migration tooling — confirms the roadmap "Data: absent" baseline and means **zero dependency conflict** when adding the provider.
- **Host & DI shape** — standard `WebApplication.CreateBuilder(args)` ([Program.cs:3](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/Program.cs#L3)); services registered via `builder.Services.AddOpenApi()` ([Program.cs:22](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/Program.cs#L22)). The research's `AddDbContextPool<AppDbContext>(opt => opt.UseNpgsql(...))` / `AddNpgsql<T>(...)` wiring is a direct fit here.
- **Config sources** — `appsettings.json` holds only Logging/AllowedHosts, no connection strings ([appsettings.json](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/appsettings.json)); `appsettings.Development.json` likewise ([appsettings.Development.json](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/appsettings.Development.json)). Consistent with the hard rule "connection strings in user-secrets / env vars, never committed" — the plan must use `builder.Configuration.GetConnectionString(...)` fed from user-secrets (dev) and Railway env vars (prod).

### SDK / tooling reality (verified locally)

- `dotnet --version` → **10.0.300**; installed SDKs: `9.0.301`, `10.0.300`. .NET 10 requirement for EF Core 10 is met.
- `dotnet ef --version` → **not found**. `dotnet-ef` must be installed (`dotnet tool install --global dotnet-ef --version 10.*` or a local tool manifest) as a plan step. Not a compatibility problem.

### Package availability & version accuracy (verified against NuGet)

- `Npgsql.EntityFrameworkCore.PostgreSQL` — latest stable **10.0.3** (NuGet registration index, commit timestamp ~2026-07-10). The research doc references **10.0.2**; still valid but **superseded by 10.0.3**. Both target net10.0 / EF Core 10 LTS.
- Recommendation: pin the trio together — provider `10.0.3`, `Microsoft.EntityFrameworkCore.Design` `10.0.x`, `dotnet-ef` `10.0.x` — to keep design-time and runtime aligned (this closes the research's own "Open follow-up: confirm exact EF Core 10 patch").

### Supabase connectivity (config, not a code dependency)

- `infrastructure.md` confirms the design intent: Railway host + **external Supabase** database (Railway Postgres explicitly skipped). The Npgsql-side gotchas in the research map cleanly to `infrastructure.md`'s risk register:
  - Use the **Supavisor pooler host** (dual-stack), not the IPv6-only direct `db.<ref>.supabase.co` — this is the concrete fix for the roadmap's flagged "pooler on Railway" risk and `infrastructure.md`'s "no dedicated outbound IP" note.
  - **Migrations → session mode (5432); app runtime → transaction mode (6543)**; `SSL Mode=Require`.
  - No extra NuGet package needed for Supabase — connection string only. **Compatible.**
- `railway.toml` shows healthcheck `/health` and `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` ([railway.toml](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/railway.toml)); nothing here conflicts with adding a DB layer.

### Seeding approach vs. roadmap requirements

- The `npgsql-docs.md` guidance (model-managed `HasData` in `OnModelCreating`, explicit stable ids, explicit FKs, join-entity seeding via `VenueSport` Option A) satisfies the roadmap's three-table requirement and the "id-keyed, never free-text label" rule for `Sports`.
- The **10 `Sports` rows are fully specified** (roadmap + `npgsql-docs.md` §6c match exactly: football, basketball, volleyball, tennis, running, cycling, rollerblading, gym, street workout, swimming).
- **`Venues` / `VenueSports` seed content is NOT yet in the repo** — only the mechanism is decided. The actual Warsaw venue list (names, lat/lng, address, which sports each supports) must be supplied before or during planning. This is the one substantive knowledge gap.

## Code References

- `server/server.csproj:3-10` — `net10.0` TFM, nullable/implicit usings, sole package `Microsoft.AspNetCore.OpenApi 10.0.8`.
- `server/Program.cs:3` — `WebApplication.CreateBuilder(args)` host root (DbContext DI insertion point).
- `server/Program.cs:22` — `builder.Services.AddOpenApi()` (existing service-registration pattern to mirror).
- `server/Program.cs:24` — `var app = builder.Build();` (boundary between service registration and pipeline).
- `server/appsettings.json` / `server/appsettings.Development.json` — no connection strings (must use user-secrets/env vars).
- `server/railway.toml` — Railway build/deploy config; `/health` healthcheck.

## Architecture Insights

- **Additive, low-risk integration.** F-01 adds a DbContext + one initial migration to an otherwise domain-empty API. No existing code to refactor; the sample `/weatherforecast` endpoint and `/health` are untouched.
- **Minimal-API + DI** is the established convention ([Program.cs](https://github.com/Sarnapa/cho-na-bojo/blob/33c8d7834aa185bc91fd7d4825caf552ea44ce51/server/Program.cs)) — the plan should register the DbContext in the same `builder.Services` block and keep the connection string in configuration, not code.
- **Reference-data-only scope is correct.** Roadmap explicitly defers `Users`/`Events`/`JoinRequests` to later slices (progressive disclosure); the plan should create exactly `Sports`, `Venues`, `VenueSports` and nothing more.
- **Stable-id contract.** Both roadmap and `npgsql-docs.md` §6b insist on explicit, permanently-locked `Sports` ids (changing a seeded PK = delete+insert under `HasData`). The plan must treat the 10 ids as a frozen contract.

## Historical Context (from prior changes)

- `context/changes/data-layer-foundation/change.md` — F-01 identity file; scope, PRD refs (FR-003, Non-Goals §1), prerequisites (none), unlocks (S-02, S-03, S-04, F-02+). Status advanced `new → preparing` by this research.
- `context/changes/data-layer-foundation/research-libraries.md` — external library research (Exa): recommends EF Core Migrations + `HasData` seeding + Supavisor pooler connectivity; pins provider 10.0.2.
- `context/changes/data-layer-foundation/npgsql-docs.md` — Context7 Npgsql/EF Core reference: DbContext wiring, SSL/pooling connection string, IDENTITY keys, `HasData` limitations, many-to-many join seeding.
- `context/foundation/roadmap.md` — F-01 definition, unlock chain, flagged risk (Supabase RLS/connection string/pooler on Railway).
- `context/foundation/infrastructure.md` — Railway + external Supabase decision; outbound-IP / pooler risk notes that reinforce the Supavisor guidance.

## Related Research

- `context/changes/data-layer-foundation/research-libraries.md` (external — libraries).
- `context/changes/data-layer-foundation/npgsql-docs.md` (external — Npgsql/EF Core API reference).
- No prior internal `research.md` under `context/changes/**` or `context/archive/**` for this topic.

## Open Questions

1. **Warsaw venue dataset (blocker for a complete seed):** what are the actual `Venues` rows (name, latitude, longitude, address) and each venue's supported sports (`VenueSports`)? The mechanism (`HasData`) is decided; the content is missing. Decide `HasData`-embedded vs. external data file + `UseAsyncSeeding` once the dataset size is known (research §Open follow-ups).
2. **Package pin:** adopt provider **10.0.3** (latest) rather than the research's 10.0.2? Recommended yes; confirm matching `Microsoft.EntityFrameworkCore.Design` / `dotnet-ef` 10.0.x patch.
3. **Supabase RLS posture for an API-only app:** service role vs. a pooled app role (research §Open follow-ups; `infrastructure.md` env-var setup lists `SUPABASE_KEY`). Affects the connection string / role the migrations and runtime use.
4. **`dotnet-ef` install strategy:** global tool vs. local tool manifest (`.config/dotnet-tools.json`) checked into the repo for reproducible CI migrations.

## Conclusion

Yes — we have effectively full knowledge to plan F-01. **Npgsql is compatible** with the
net10.0 codebase and installed 10.0.300 SDK, integrates additively into the existing
minimal-API/DI host, and has no conflicting packages. Proceed to `/10x-plan
data-layer-foundation`, resolving the four open questions in-plan — with the **Warsaw venue
dataset** being the only item that adds genuinely new information rather than a
confirm-and-pin decision.
