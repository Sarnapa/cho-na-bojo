# Auth Scaffold (F-02) — Plan Brief

> Full plan: `context/changes/auth-scaffold/plan.md`
> Frame brief: `context/changes/auth-scaffold/frame.md`
> Research: `context/changes/auth-scaffold/research.md`, `context/changes/auth-scaffold/research-jwt-implementation.md`

## What & Why

Implement the Foundation-scoped self-hosted email+password JWT auth on the ASP.NET Core API. Firebase Auth was considered and deliberately dropped (EU residency loss, permanent vendor lock-in, a one-maintainer MAUI SDK, and no help with the per-event authorization launch gate). This unblocks S-01 (register/login UI) and every protected route the PRD's "unauthenticated: no access" guardrail requires.

## Starting Point

The repo is at the F-01 data-layer state: `server/` is a minimal API with EF Core + PostGIS (`Sports`/`Venues`/`VenueSports`), a public `/health`, and template `/weatherforecast`. No auth code exists anywhere; the MAUI client has only `CheckHealthAsync()`.

## Desired End State

The API can register and log in users (email+password with ≥1 shareable contact), issue short-lived access JWTs plus rotating refresh tokens, refresh and log out, and reject unauthenticated requests to domain routes with 401. Contact info is structurally separated in the `Users` table and never leaked by default — laying the privacy foundation the S-05 contact-reveal gate depends on.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Auth provider | Self-hosted JWT (no Firebase) | Residency + lock-in + one-maintainer SDK outweigh ~150 fewer lines. | Frame |
| Libraries | JwtBearer (validate) + JsonWebTokenHandler (issue) + PasswordHasher<T> (hash) | First-party, official, matches the agent-friendly convention gates. | Research |
| Refresh-token flow | Full rotation + `FamilyId` reuse detection | Replay of a consumed token revokes the family — the load-bearing self-hosting cost. | Plan |
| Token lifetimes | Access 15 min / Refresh 30 days | Balances validation load vs. re-login friction for a mobile app. | Plan |
| Contact model | phone / contactEmail / (communicatorPlatform + communicatorHandle); ≥1 required | `CommunicatorPlatform` enum (Messenger/Instagram/WhatsApp) + handle; enforced in DTO validation *and* a DB CHECK. | Plan |
| Per-event authz in F-02 | Minimal foundation only | Stable UUID `Users.Id` + `[Authorize]` baseline + current-user-id helper; event tables arrive S-03/S-05. | Plan |
| Endpoint surface | register + login + refresh + logout | Full session lifecycle server-side; MAUI UI consumes them in S-01. | Plan |

## Scope

**In scope:** `Users` + `RefreshTokens` tables + migration; `PasswordHasher<T>` hashing; `JsonWebTokenHandler` access-token issuance; rotating refresh tokens with `FamilyId` reuse detection; `/auth/register|login|refresh|logout`; `[Authorize]` baseline on domain routes; current-user-id helper; JWT config via user-secrets/Railway env.

**Out of scope:** MAUI client changes (S-01); event/participant tables + per-event handlers + contact-reveal (S-03/S-05/S-07); global roles; password reset (Parked); OAuth; Firebase Auth.

## Architecture / Approach

Bottom-up, mirroring the F-01 layering: **data model** (`User`, `RefreshToken`, `CommunicatorPlatform` enum → `OnModelCreating` config → out-of-band migration) → **infrastructure** (packages, user-secrets JWT config, token/password/refresh services, current-user helper, `Program.cs` wiring) → **endpoints** (`/auth/*` DTOs + validation, `.RequireAuthorization()` on domain routes). The JWT carries only a stable UUID `sub`/`NameIdentifier` — never email, roles, or contact — so authentication and per-event authorization stay cleanly separated.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Data model & migration | `Users` + `RefreshTokens` tables, ≥1-contact CHECK, out-of-band migration | CHECK-constraint expression + DB-generated UUID id (not the seeded `ValueGeneratedNever` pattern) |
| 2. Auth infrastructure | Packages, user-secrets JWT config, token/password/refresh services, current-user helper, `Program.cs` wiring | Refresh rotation + `FamilyId` reuse detection correctness; config must fail fast, not silently default |
| 3. Endpoints & authz baseline | `/auth/register|login|refresh|logout`, `[Authorize]` on domain routes, `/weatherforecast` removed | Contact-field leakage in responses; user-enumeration signal on login failure |

**Prerequisites:** F-01 (done); dev user-secrets set (JWT signing key ≥256-bit, issuer, audience, `AppDb`/`AppDbMigrations` connection strings); EF tooling for out-of-band migration.
**Estimated effort:** ~2–3 sessions across 3 phases (solo, after-hours).

## Open Risks & Assumptions

- Privacy boundary is a launch gate: any response leaking contact fields, or roles/contact leaking into the JWT, would undermine the S-05 guardrail — mitigated by structural separation + JWT-is-auth-only.
- Refresh-token reuse detection must revoke the whole family on replay of a consumed token; a bug here weakens theft protection.
- No automated test project exists; F-02 relies on manual endpoint verification (documented) — refresh rotation/reuse is the riskiest untested path.
- Assumes CORS is unnecessary (MAUI Android client is not a browser); revisit only if a browser client is added.

## Success Criteria (Summary)

- A user can register (with ≥1 contact) and log in, and the API returns a usable access + refresh token pair.
- Protected routes return 401 without a valid bearer token and 200 with one; `/health` stays public.
- Refresh rotates the token pair, replaying a consumed refresh token revokes the family, and logout revokes the family.
