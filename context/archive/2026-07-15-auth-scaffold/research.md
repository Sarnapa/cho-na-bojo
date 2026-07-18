---
date: 2026-07-16T14:25:25+02:00
researcher: Sarnapa
git_commit: cc083db53c9c9a1069185b0ad679b501f48c85d2
branch: master
repository: Sarnapa/cho-na-bojo
topic: "auth-scaffold (F-02): codebase-integration + per-event authorization model"
tags: [research, codebase, auth, jwt, ef-core, maui, authorization, privacy]
status: complete
last_updated: 2026-07-16
last_updated_by: Sarnapa
---

# Research: auth-scaffold (F-02) — integration seams + per-event authorization model

**Date**: 2026-07-16T14:25:25+02:00
**Researcher**: Sarnapa
**Git Commit**: cc083db53c9c9a1069185b0ad679b501f48c85d2
**Branch**: master
**Repository**: Sarnapa/cho-na-bojo

## Research Question

For the `auth-scaffold` change (roadmap **F-02**), two things:
1. **Codebase-integration research** — how self-hosted email+password JWT auth plugs into the existing EF Core data layer, migrations, `Program.cs` wiring, and the MAUI client conventions.
2. **Per-event authorization model design** — the organizer/participant per-event role check and the contact-reveal-only-after-acceptance gate that later slices (S-05 request-to-join, S-07 approval) depend on, and how much of it F-02 must lay down now.

The provider decision (self-hosted vs Firebase) is already settled in `frame.md` — **self-hosted**. The JWT/hashing/refresh library choices are settled in `research-jwt-implementation.md`. This document is the *codebase-grounded* companion to those.

## Summary

The repo is at the **F-01 data-layer foundation** state: `server/` is a minimal API with EF Core + PostGIS reference tables (`Sports`, `Venues`, `VenueSports`) and `/health` + the template `/weatherforecast`; the MAUI client has a single typed `IApiService.CheckHealthAsync()`. **No auth code exists anywhere.**

F-02 slots in cleanly by mirroring existing conventions:
- **Data layer**: add `User` (+ `RefreshToken`) POCOs in `server/Data/Entities/`, register `DbSet` property-blocks and `OnModelCreating` config in `ChoNaBojoContext.cs`, then `dotnet ef migrations add` + `dotnet ef database update` — the app **never auto-migrates**.
- **API**: add `Microsoft.AspNetCore.Authentication.JwtBearer` + `Microsoft.IdentityModel.JsonWebTokens` + `Microsoft.AspNetCore.Identity` (hasher), wire `AddAuthentication().AddJwtBearer()` / `AddAuthorization()` in `Program.cs`, protect domain route groups with `.RequireAuthorization()`. Signing key/issuer/audience go in **user-secrets (dev)** / **Railway env (prod)** — never `appsettings.json`.
- **MAUI client** (consumed by S-01, not F-02 itself): the named `HttpClient "ChoNaBojoApi"` is the seam for an auth `DelegatingHandler`; the login gate belongs in `App.CreateWindow` / `AppShell`; there is **no** `SecureStorage`/`Preferences` usage today.

For **authorization**: `[Authorize]` (authentication) is the F-02 baseline and satisfies the PRD "unauthenticated user: no access" guardrail. **Per-event roles are resource state, not JWT claims** — the JWT must carry only a stable, server-generated `Users.Id` (UUID) in `sub`/`NameIdentifier`, never email or contact info. Organizer/participant checks and contact-reveal must query current DB state at request time. F-02 lays the *foundation* (stable user id, `Users` table, auth baseline, current-user helper); the `Event`/`EventParticipant` tables and resource handlers arrive with S-03/S-05/S-07 — but F-02 must not make choices (email-as-id, roles-in-JWT, mismatched refresh-token user-id type) that block them.

## Detailed Findings

### Area 1 — EF Core data-layer conventions to mirror

Current model: `Sport`, `Venue`, `VenueSport` POCOs in `server/Data/Entities/`, configured in `ChoNaBojoContext`.

- **Entity style**: namespace `ChoNaBojo.Server.Data.Entities`; public auto-properties `get; set;`; required reference types use `= null!;`; collections use `= [];`; concise domain XML-doc comments on class + key properties. `Sport.cs`, `Venue.cs`, `VenueSport.cs`. Files are **tab-indented, brace-on-new-line**.
- **DbSets** are exposed as **property blocks returning `Set<T>()`** (not expression-bodied) — see `ChoNaBojoContext.cs:13-35`. New `Users`/`RefreshTokens` DbSets should follow the same block style.
- **`OnModelCreating`** calls `base.OnModelCreating(modelBuilder)` first, then `modelBuilder.HasPostgresExtension("postgis")`, then per-entity `Entity<T>(entity => { ... })` blocks (`ChoNaBojoContext.cs:37-95`).
- **Keys/IDs**: seeded/stable-identity rows use `HasKey(...)` + `.Property(x => x.Id).ValueGeneratedNever()` with explicit ids (`Sport`/`Venue`, `ChoNaBojoContext.cs:47-50, 68-70`). This convention is for **imported/seeded** data. For `User.Id` the recommendation is a **DB-generated `Guid`/`uuid`** (do **not** copy `ValueGeneratedNever()` — that pattern is only for the seeded CSV identity).
- **Constraints**: required scalars `.IsRequired()`; uniqueness via `.HasIndex(x => x.Col).IsUnique()` (mirror for `User.LoginEmail`); relationships via `HasOne().WithMany().HasForeignKey().OnDelete(DeleteBehavior.Cascade)` (`ChoNaBojoContext.cs:82-94`) — pattern for `RefreshToken → User`.
- **Migration workflow / "never auto-migrate"**: the app must **not** call `Database.Migrate()`; schema is applied out-of-band via `dotnet ef database update` (documented in the context XML-doc, `ChoNaBojoContext.cs:6-10`, and enforced in the archived data-layer plan/review). Runtime uses the **transaction pooler** connection `ConnectionStrings:AppDb` (port 6543); migrations use the **session-mode** `ConnectionStrings:AppDbMigrations` (port 5432).
- **Lesson from the archived data-layer change**: the impl-review flagged plan→implementation **drift** (raw SQL inserts used for seeding instead of EF `Point` adds) — if auth entities deviate from the EF-first pattern, keep the plan/source-of-truth updated. (`context/archive/2026-07-12-data-layer-foundation/reviews/impl-review.md`).

### Area 2 — API wiring & config conventions

`server/Program.cs` is a top-level minimal-API `WebApplication`:
- Railway `PORT` binding for non-Development (`Program.cs:8-14`); `ForwardedHeadersOptions` + `app.UseForwardedHeaders()` for the TLS-terminating proxy (`Program.cs:16-22, 38-40`); `AddOpenApi()`/`MapOpenApi()` (dev only); `app.UseHttpsRedirection()`.
- `AddDbContext<ChoNaBojoContext>` uses `GetConnectionString("AppDb")` + `UseNetTopologySuite()` + `UseSeeding`/`UseAsyncSeeding` (`Program.cs:26-36`).
- Endpoints are `app.MapGet(...).WithName(...)` — e.g. `/health` (`Program.cs:47-48`). The template `/weatherforecast` (`Program.cs:50-67`) and its `WeatherForecast` record (`Program.cs:71-74`) are scaffolding that can be removed when real endpoints land.
- **Where auth wires in**: `builder.Services.AddAuthentication().AddJwtBearer(); builder.Services.AddAuthorization();` before `Build()`; auth endpoints as `app.MapPost("/auth/register", ...)` / `"/auth/login"`; protect domain routes with `.RequireAuthorization()` on route groups. On .NET 10 `WebApplication`, auth middleware auto-registers, but **middleware order matters** if CORS is later added (CORS before auth).
- **`server.csproj`**: `net10.0`, `RootNamespace = ChoNaBojo.Server`, `Nullable`/`ImplicitUsings` enabled (repo hard rule: do not disable), `UserSecretsId` already set (`175139ae-1f3e-4ee2-b72d-ea5a434f29a8`). Packages today: `Microsoft.AspNetCore.OpenApi` 10.0.10, `Microsoft.EntityFrameworkCore.Design` 10.0.3, `Npgsql.EntityFrameworkCore.PostgreSQL(.NetTopologySuite)` 10.0.3. F-02 adds the three auth packages from `research-jwt-implementation.md`.
- **Secrets**: `appsettings.json` holds only logging/`AllowedHosts`; `appsettings.Development.json` shows a **commented** `"//ConnectionStrings"` placeholder documenting `AppDb` + `AppDbMigrations` — real values live in user-secrets. JWT signing key/issuer/audience must follow the same rule: **user-secrets (dev) / Railway env (prod), never committed** (repo hard rule + PRD privacy boundary).

### Area 3 — MAUI client integration points (consumed by S-01)

F-02 is API-only; these are the seams S-01 will touch.
- **HttpClient seam**: `MauiProgram.cs:18-31` registers named client `"ChoNaBojoApi"` — DEBUG `http://10.0.2.2:5100` (Android emulator) / `http://localhost:5100` (other), Release `https://cho-na-bojo-production.up.railway.app`. This named client is the exact insertion point for an auth `DelegatingHandler` (attach `Authorization: Bearer`, handle 401→refresh).
- **Typed-service pattern**: `IApiService` (`IApiService.cs:3-5`) currently exposes only `Task<bool> CheckHealthAsync()`; `ApiService` (`ApiService.cs`) injects `IHttpClientFactory`, resolves the named client, calls `GetAsync`, returns `IsSuccessStatusCode`, and catches `HttpRequestException`. Register/login/refresh methods should follow this shape (extending to DTO/error results as needed).
- **DI + conventions**: `AddSingleton<IApiService, ApiService>()`, `AddTransient<MainPage>()` (`MauiProgram.cs:30-31`); namespaces `ChoNaBojo.App` / `ChoNaBojo.App.Services`; tab indentation.
- **Session persistence / login gate**: **none exists today** — no `SecureStorage`, `Preferences`, or token code. Startup goes straight to shell: `App.xaml.cs:10-12` returns `new Window(new AppShell())`; `AppShell.xaml.cs` is an empty bootstrap. The login gate belongs in `App.CreateWindow(...)` or `AppShell` routing, deciding the first page from persisted session state (refresh token in `SecureStorage`).
- **Platform**: `ChoNaBojoApp.csproj` always targets `net10.0-android` (Windows only conditionally on Windows build hosts); **no iOS target** — consistent with the Android-only hard rule.

### Area 4 — Per-event authorization model design

**(a) JWT = authentication only.** The access JWT carries: `sub` = canonical immutable `Users.Id`; `NameIdentifier`/`nameid` mapped to the same value; standard `iss`/`aud`/`exp`/`nbf`/`iat`/`jti`. The `sub` value must be a **server-generated stable UUID**, serialized as string — **never email** (email can change and is itself potential contact info). Do **not** put per-event roles, event ids, contact info, or email into the JWT. `[Authorize]` proves only "authenticated" and satisfies PRD "unauthenticated user: no access". This aligns with the locked JwtBearer-validation + `JsonWebTokenHandler`-issuance + refresh-token + `PasswordHasher<T>` decisions in `research-jwt-implementation.md`.

**(b) `Users` table shape for the privacy boundary.** Separate **identity/credential** from **contact**:
- `Id uuid PK`, `LoginEmail`/`NormalizedLoginEmail` (required, unique), `PasswordHash` (required), audit timestamps.
- `ContactPhone`, `ContactEmail`, `ContactMessenger` — all nullable, with a **DB check constraint: at least one non-empty** (FR-011: "at least one required, not all").
- `LoginEmail` (credential) is distinct from `ContactEmail` (shareable) even if the values match; login email is never auto-exposed as contact. Default DTOs / auth responses / listings must **exclude** contact fields.

**(c) Future per-event role model.** Roles are **per-event, never global**. Future entities relating to the existing stable `Venue`/`Sport` ids:
- `SportsEvent` (avoid the reserved-ish name `Event`): `Id uuid`, `VenueId int` FK, `SportId int` FK, `OrganizerUserId uuid` FK, `StartsAtUtc`, `EstimatedEndsAtUtc`, `ParticipantLimit`, `AutoAccept bool`, `Status` (active/cancelled/closed). Organizer is derived from `OrganizerUserId`.
- `EventParticipant`: `Id uuid`, `EventId`, `UserId`, `Status` (`Pending`/`Accepted`/`Rejected`/`Left`/`Removed`), request/decision timestamps, `DecidedByUserId`; one row per `(EventId, UserId)`.
- Authorization predicates (read **current DB state**, not claims): organizer = `SportsEvents.Any(e => e.Id == eventId && e.OrganizerUserId == callerId)`; accepted participant = `EventParticipants.Any(p => p.EventId == eventId && p.UserId == callerId && p.Status == Accepted)`. Implement via ASP.NET Core resource-based authorization (`IAuthorizationService` + requirements/handlers) or **centralized, reused** explicit query checks in minimal-API handlers.

**(d) Contact-reveal rule** (the launch gate). Default event/user DTOs exclude contact fields. A **dedicated reveal path** returns contact info only when the acceptance relationship holds: an accepted participant may see the organizer's contact; the organizer may see an accepted participant's contact. `Pending`/`Rejected`/`Left`/`Removed` must never reveal contact, and lifecycle changes must stop revealing immediately. Acceptance closing the loop (contact reveal) *is* the product's north-star success criterion.

**(e) F-02 scope vs deferred.**
- **In F-02 now**: `Users` table (stable `Id`), `LoginEmail` + `PasswordHash` (`PasswordHasher<T>`), contact fields with ≥1 constraint, JWT issue/validate with stable id in `sub`/`NameIdentifier`, `RefreshToken` table keyed to the **same** `User.Id` type, `[Authorize]` baseline on protected routes, a documented **current-user-id extraction helper**.
- **Deferred**: `SportsEvent` (S-03), `EventParticipant` (request-to-join), organizer/participant resource handlers (S-05/S-07), contact-reveal endpoints (approval slice).
- **F-02 must NOT**: use email as user id; put contact/roles in the JWT; add global Organizer/Participant roles; make the refresh-token user-id type differ from `Users.Id`; return contact fields in default DTOs.

## Code References

- [server/Program.cs#L26-L36](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/Program.cs#L26-L36) — `AddDbContext` + `AppDb` connection string + NetTopologySuite/seeding wiring
- [server/Program.cs#L44-L48](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/Program.cs#L44-L48) — minimal-API pipeline + `/health` (auth middleware + protected route groups slot in here)
- [server/Data/ChoNaBojoContext.cs#L13-L35](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/Data/ChoNaBojoContext.cs#L13-L35) — DbSet property-block style to mirror for `Users`/`RefreshTokens`
- [server/Data/ChoNaBojoContext.cs#L37-L95](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/Data/ChoNaBojoContext.cs#L37-L95) — `OnModelCreating` config style (HasKey/IsRequired/HasIndex.IsUnique/relationships)
- [server/Data/Entities/Sport.cs](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/Data/Entities/Sport.cs) — entity POCO style (`= null!;`, `= [];`, XML docs, tabs)
- [server/server.csproj](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/server.csproj) — net10.0, RootNamespace, Nullable/ImplicitUsings, UserSecretsId, package set
- [server/appsettings.Development.json](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/server/appsettings.Development.json) — commented `AppDb`/`AppDbMigrations` placeholder (secrets pattern for JWT config)
- [app/ChoNaBojoApp/MauiProgram.cs#L18-L31](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/app/ChoNaBojoApp/MauiProgram.cs#L18-L31) — named `HttpClient "ChoNaBojoApi"` (DelegatingHandler seam) + DI lifetimes
- [app/ChoNaBojoApp/Services/ApiService.cs](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/app/ChoNaBojoApp/Services/ApiService.cs) — typed-service pattern for future register/login/refresh
- [app/ChoNaBojoApp/App.xaml.cs#L10-L12](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/app/ChoNaBojoApp/App.xaml.cs#L10-L12) — startup `new Window(new AppShell())` (login-gate insertion point)
- [context/foundation/prd.md](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/context/foundation/prd.md) — Access Control (per-event roles), FR-001/002/011, privacy guardrails
- [context/foundation/roadmap.md](https://github.com/Sarnapa/cho-na-bojo/blob/cc083db53c9c9a1069185b0ad679b501f48c85d2/context/foundation/roadmap.md) — F-02 detail (L80-90) and S-05 privacy-gate risk (L144-153)

## Architecture Insights

- **Convention-first**: the stack was chosen against agent-friendliness gates (typed, official-starter, conventions, docs-current). Auth uses **first-party** Microsoft packages (JwtBearer + JsonWebTokens + Identity hasher), consistent with that rationale — reinforcing the `frame.md` decision to reject the community Firebase MAUI binding.
- **Out-of-band migrations**: the "app never auto-migrates" rule + dual connection strings (pooler for runtime, session for migrations) is a load-bearing operational convention; auth tables must follow it.
- **Separation of authentication vs authorization**: `[Authorize]` is the authenticated-only gate (F-02); per-event authorization is **resource-based** and must read live DB state. Encoding roles in the JWT would be a design trap the PRD's per-event model forbids.
- **Privacy as a data-shape decision, not just an endpoint check**: contact fields are structurally separated in `Users` and excluded from default DTOs, so the reveal gate is the *only* path that emits them — defense in depth for the launch-gate guardrail.
- **Identity stability**: a server-generated UUID `Users.Id` (not email) is the single anchor shared by the JWT `sub`, the refresh-token table, and all future event/participant FKs — choosing it wrong in F-02 would ripple through S-03/S-05/S-07.

## Historical Context (from prior changes)

- `context/changes/auth-scaffold/frame.md` — decision to build **self-hosted** email+password JWT (no Firebase Auth); flags session-persistence/refresh-token strategy and the per-event authorization model as the real load-bearing costs.
- `context/changes/auth-scaffold/research-jwt-implementation.md` — locked library decisions: JwtBearer (validate), `JsonWebTokenHandler` (issue), `PasswordHasher<T>` (hash), custom rotating refresh-token table with `FamilyId` reuse detection; config via user-secrets/Railway env.
- `context/archive/2026-07-12-data-layer-foundation/` (research.md, plan.md, reviews/impl-review.md) — established the EF Core + PostGIS conventions, the "never auto-migrate" rule, and the dual connection-string setup this change mirrors; impl-review noted plan→code seeding drift as a lesson.

## Related Research

- `context/changes/auth-scaffold/research-jwt-implementation.md` (library/security research — companion to this codebase-integration doc)
- `context/archive/2026-07-12-data-layer-foundation/research.md` and `research-libraries.md`

## Open Questions

- **Access/refresh TTLs**: concrete values (access 5–15 min; refresh 7–30 days) — resolve in `/10x-plan`.
- **Refresh-token reuse detection (`FamilyId`) in F-02 or deferred?** — the frame flags this as the underestimated cost; decide scope in `/10x-plan`.
- **Register DTO shape**: how `phone`/`contactEmail`/`messenger` (≥1 required) map from the request body to the `Users` columns and where the ≥1 rule is enforced (DTO validation + DB check constraint).
- **Current-user helper**: exact shape of the `sub`/`NameIdentifier` → `Guid` extraction helper reused by every protected handler.
- **`SportsEvent` vs `Event` naming** and whether F-02 should pre-declare the FK-target user-id type in any shared contract to avoid later churn.
- **Middleware ordering**: whether CORS is needed for the MAUI Android client (emulator uses `10.0.2.2`), which would dictate `UseCors` before auth.
