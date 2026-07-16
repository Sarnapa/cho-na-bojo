# Auth Scaffold (F-02) Implementation Plan

## Overview

Implement the Foundation-scoped **self-hosted email+password JWT authentication** on the ASP.NET Core API: a `Users` table (identity/credential + shareable contact, structurally separated), a rotating `RefreshTokens` table, `POST /auth/register|login|refresh|logout` endpoints, `PasswordHasher<T>` hashing, `JsonWebTokenHandler` access-token issuance, `Microsoft.AspNetCore.Authentication.JwtBearer` validation, and an `[Authorize]` baseline on all domain routes. This satisfies FR-001, FR-002, FR-011, and the PRD's "unauthenticated user: no access" privacy guardrail. The change is **API-only** — the register/login UI and `SecureStorage` session persistence land in S-01.

## Current State Analysis

The repo is at the **F-01 data-layer foundation** state:

- `server/Program.cs` is a top-level minimal API: Railway `PORT` binding (non-Development), `ForwardedHeaders` for the TLS-terminating proxy, `AddOpenApi()`, `AddDbContext<ChoNaBojoContext>` with `AppDb` + NetTopologySuite + seeding, `app.UseHttpsRedirection()`, a public `/health` endpoint, and the template `/weatherforecast` scaffolding.
- `server/Data/ChoNaBojoContext.cs` exposes `Sports`/`Venues`/`VenueSports` as **DbSet property blocks returning `Set<T>()`**; `OnModelCreating` calls `base` first, then `HasPostgresExtension("postgis")`, then per-entity `Entity<T>(entity => { ... })` config (HasKey / IsRequired / HasIndex.IsUnique / HasOne.WithMany.HasForeignKey.OnDelete). Files are **tab-indented, brace-on-new-line**.
- Entities in `server/Data/Entities/` (`Sport`, `Venue`, `VenueSport`) use namespace `ChoNaBojo.Server.Data.Entities`, public auto-properties, `= null!;` for required refs, `= [];` for collections, concise XML-docs.
- `server.csproj`: `net10.0`, `RootNamespace = ChoNaBojo.Server`, `Nullable` + `ImplicitUsings` enabled (hard rule: never disable), `UserSecretsId = 175139ae-…` already set. Packages: OpenApi 10.0.10, EFCore.Design 10.0.3, Npgsql(.NetTopologySuite) 10.0.3.
- `appsettings.Development.json` holds only logging + a **commented** `"//ConnectionStrings"` placeholder — real secrets live in user-secrets.
- **No auth code exists anywhere.** The MAUI client has only `IApiService.CheckHealthAsync()`.

Key operational conventions (load-bearing): the app **never auto-migrates** — schema is applied out-of-band via `dotnet ef database update`; runtime uses the **transaction pooler** `AppDb` (port 6543), migrations use the **session-mode** `AppDbMigrations` (port 5432).

## Desired End State

The API has:

- A `Users` table with a **DB-generated `uuid` `Id`**, unique normalized login email, `PasswordHash`, structurally separated nullable contact fields (`ContactPhone`, `ContactEmail`, `CommunicatorPlatform` + `CommunicatorHandle`), and a DB CHECK constraint requiring **≥1 complete contact method**.
- A `RefreshTokens` table (SHA-256 hash at rest, `FamilyId`, rotation/consumption/revocation columns) keyed to the same `Users.Id` type via a cascade FK.
- `POST /auth/register`, `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout` working end-to-end.
- Access JWTs (15 min TTL) carrying only a stable UUID in `sub`/`NameIdentifier` — never email, roles, or contact info; rotating refresh tokens (30 day TTL) with **`FamilyId` reuse detection** (replay of a consumed token revokes the whole family).
- The **authorized domain route-group seam** is established with `.RequireAuthorization()` (empty today — real domain routes land in S-03+); `/health` remains public; `/weatherforecast` removed; `GET /auth/me` is the retained protected probe exercising authz and the current-user helper.
- JWT signing key/issuer/audience read from user-secrets (dev) / Railway env (prod) — never committed.

**Verification**: `dotnet build` clean; migration applies; register → login → call a protected route with the bearer token (200) and without it (401); refresh rotates the token pair; replaying a consumed refresh token revokes the family; logout revokes the family.

### Key Discoveries:

- DbSet property-block style to mirror: `ChoNaBojoContext.cs:13-35`.
- `OnModelCreating` config style (HasKey / IsRequired / HasIndex.IsUnique / relationship cascade): `ChoNaBojoContext.cs:37-95`.
- `User.Id` must be **DB-generated `Guid`/`uuid`** — do **not** copy the seeded `ValueGeneratedNever()` pattern (that is only for imported CSV identity rows).
- On .NET 10 `WebApplication`, `AddAuthentication().AddJwtBearer()` + `AddAuthorization()` **auto-register** the middleware; explicit `UseAuthentication`/`UseAuthorization` is only needed when order matters (e.g. CORS first). The MAUI Android client is **not a browser**, so **CORS is not required** — no ordering constraint here.
- JwtBearer options *can* auto-bind from configuration under `Authentication:Schemes:Bearer`, but this plan does **not** use that path. The **single source of truth** is the custom `JwtOptions` section (Phase 2 §2), and `AddJwtBearer` is configured **explicitly** from it (Phase 2 §7) so issuance and validation provably read the same signing key/issuer/audience. Do not also populate `Authentication:Schemes:Bearer` — one section only.
- `dotnet user-jwts` is a **dev-only** token tester, not a production issuer.
- Secrets pattern: `appsettings.Development.json` uses a commented `"//ConnectionStrings"` block; JWT config follows the same rule (user-secrets/Railway env only).

## What We're NOT Doing

- **No MAUI client changes** — no `DelegatingHandler`, no `SecureStorage`, no login gate. That is S-01.
- **No event/participant tables or per-event authorization handlers** — `SportsEvent` / `EventParticipant` and organizer/participant checks arrive in S-03/S-05/S-07. F-02 only lays the *minimal* foundation (stable UUID user id, `[Authorize]` baseline, current-user-id helper).
- **No global Organizer/Participant roles** — roles are per-event resource state, never JWT claims or account-level roles.
- **No password reset** (Parked) and **no OAuth** (out of MVP scope).
- **No contact-reveal endpoints** — the reveal path lands in S-05. F-02 only ensures contact fields are structurally separated and excluded from default responses.
- **No Firebase Auth** — settled in `frame.md` (residency + lock-in + one-maintainer SDK).
- **No refresh-token pruning job** — consumed/expired/revoked `RefreshTokens` rows are never deleted in F-02 (fine at MVP volume). Defer a pruning job (`DELETE WHERE ExpiresUtc < now`) to the tech-stack's planned background-jobs work; noted here so it isn't silently forgotten. No F-02 action.

## Implementation Approach

Build bottom-up, mirroring the F-01 layering (data model → infrastructure/services → API endpoints):

1. Add the `User` and `RefreshToken` entities + a `CommunicatorPlatform` enum, configure them in `ChoNaBojoContext` following the existing conventions, and produce a migration applied out-of-band.
2. Add the three first-party auth packages, bind JWT config from user-secrets, and build the auth infrastructure: a token service (issue via `JsonWebTokenHandler`), password hashing via `PasswordHasher<User>` (with rehash-on-login), a rotating refresh-token service (SHA-256 at rest + `FamilyId` reuse detection), and a current-user-id helper. Wire authentication/authorization in `Program.cs`.
3. Expose the four `/auth/*` endpoints with request/response DTOs and validation (≥1 contact), apply `.RequireAuthorization()` to domain route groups, remove `/weatherforecast`, and verify the full flow.

## Critical Implementation Details

- **Identity stability is a rippling decision.** The `Users.Id` (server-generated UUID, serialized as string in the JWT `sub`/`NameIdentifier`) is the single anchor shared by the JWT, the `RefreshTokens.UserId` FK, and every future event/participant FK (S-03/S-05/S-07). Choosing email-as-id or a mismatched refresh-token user-id type would force churn later — the FK type must equal `Users.Id`'s type.
- **JWT is authentication only.** Never place email, contact info, or per-event roles in the token. `[Authorize]` proves only "authenticated". This is defense-in-depth for the launch-gate privacy boundary.
- **Contact separation is structural, not just endpoint-level.** `LoginEmail` (credential) is distinct from `ContactEmail` (shareable) even if values match; default auth responses and DTOs must exclude contact fields so the future reveal path (S-05) is the only code that emits them.
- **Refresh-token reuse detection.** Store only the SHA-256 hash at rest; rotate on every exchange (mark old row consumed, issue new pair sharing the `FamilyId`) inside a **single DB transaction**; if a **consumed** token is presented again, first apply a short **benign-retry grace window** (a just-consumed token whose live child still exists is a legitimate mobile retry — return that child's pair idempotently, no revocation); only a genuine out-of-window/child-already-consumed replay is treated as theft and revokes the entire family.
- **Migrations are out-of-band.** Do not call `Database.Migrate()`. Apply with `dotnet ef database update` using the session-mode `AppDbMigrations` string; runtime keeps using the pooled `AppDb`.

---

## Phase 1: Data model & migration

### Overview

Add the `User` and `RefreshToken` entities plus the `CommunicatorPlatform` enum, configure them in `ChoNaBojoContext`, and generate + apply the migration out-of-band.

### Changes Required:

#### 1. CommunicatorPlatform enum

**File**: `server/Data/Entities/CommunicatorPlatform.cs`

**Intent**: Constrain the optional communicator contact to the three supported messaging apps so a handle always names its platform.

**Contract**: `public enum CommunicatorPlatform { Messenger = 1, Instagram = 2, WhatsApp = 3 }` in namespace `ChoNaBojo.Server.Data.Entities`. Stored as `int` (explicit values) so DB rows are stable if names change.

#### 2. User entity

**File**: `server/Data/Entities/User.cs`

**Intent**: The account/identity record. Separates credential fields (login email + password hash) from shareable contact fields, with the communicator contact expressed as a platform + handle pair.

**Contract**: class `User` in `ChoNaBojo.Server.Data.Entities` with: `Guid Id`; `string LoginEmail`/`string NormalizedLoginEmail` (required — `NormalizedLoginEmail` is always produced by the single `NormalizeLoginEmail` helper defined in Phase 3 §3: `Trim().ToUpperInvariant()`); `string PasswordHash` (required); nullable `string? ContactPhone`, `string? ContactEmail`, `CommunicatorPlatform? CommunicatorPlatform`, `string? CommunicatorHandle`; `DateTime CreatedUtc`, `DateTime UpdatedUtc`; `ICollection<RefreshToken> RefreshTokens = [];`. Follow entity conventions (`= null!;` for required refs, XML-docs, tab indent). The invariant "a communicator handle requires a platform and vice versa" and "≥1 contact method" are enforced in `OnModelCreating` (below) + DTO validation (Phase 3).

#### 3. RefreshToken entity

**File**: `server/Data/Entities/RefreshToken.cs`

**Intent**: Server-side rotating refresh-token record; only the SHA-256 hash is stored, with rotation/family/revocation metadata for reuse detection.

**Contract**: class `RefreshToken` in `ChoNaBojo.Server.Data.Entities` with: `Guid Id`; `Guid UserId` (FK — **same type as `User.Id`**); `User User = null!;` nav; `string TokenHash` (required, SHA-256); `Guid FamilyId`; `DateTime CreatedUtc`, `DateTime ExpiresUtc`; nullable `DateTime? ConsumedUtc`, `DateTime? RevokedUtc`; `Guid? ReplacedByTokenId`; nullable `string? CreatedByIp`.

#### 4. DbContext configuration

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Register the two new DbSets and configure keys, constraints, uniqueness, the ≥1-contact CHECK, and the `RefreshToken → User` cascade FK — mirroring the existing style.

**Contract**: Add `DbSet<User> Users` and `DbSet<RefreshToken> RefreshTokens` as property blocks returning `Set<T>()`. In `OnModelCreating`, add `Entity<User>` and `Entity<RefreshToken>` blocks:
- `User`: `HasKey(u => u.Id)` with **DB-generated** id (do NOT use `ValueGeneratedNever()`); `NormalizedLoginEmail` required + `HasIndex(...).IsUnique()`; `LoginEmail`/`PasswordHash` required; `CommunicatorHandle` reasonable max length. A table-level CHECK constraint (`ToTable(t => t.HasCheckConstraint(...))`) enforcing **≥1 contact**: `ContactPhone` non-empty OR `ContactEmail` non-empty OR (`CommunicatorPlatform` IS NOT NULL AND `CommunicatorHandle` non-empty) — and communicator platform/handle co-presence. Also update the class XML-doc summary to mention the new tables.
- `RefreshToken`: `HasKey(r => r.Id)`; `TokenHash` required; `HasIndex(r => r.TokenHash)` (lookup on exchange); `HasIndex(r => r.FamilyId)`; `HasOne(r => r.User).WithMany(u => u.RefreshTokens).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade)`.

#### 5. Migration

**File**: `server/Migrations/*` (generated)

**Intent**: Produce and apply the schema change out-of-band per the "never auto-migrate" rule.

**Contract**: `dotnet ef migrations add AddAuthTables` (from `server/`), then apply with `dotnet ef database update` using the session-mode `AppDbMigrations` connection string. The generated migration must contain the `Users` + `RefreshTokens` tables, the unique index on `NormalizedLoginEmail`, and the CHECK constraint.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Migration generates without model errors: `dotnet ef migrations add AddAuthTables --project server`
- Migration applies cleanly against the dev DB: `dotnet ef database update --project server` (session-mode `AppDbMigrations`)

#### Manual Verification:

- `Users` and `RefreshTokens` tables exist in the DB with the expected columns, the unique index on the normalized login email, and the ≥1-contact CHECK constraint.
- Attempting to insert a user with no contact method is rejected by the DB.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the schema looks correct in the database before proceeding.

---

## Phase 2: Auth infrastructure (packages, config, services, wiring)

### Overview

Add the auth packages, bind JWT config from user-secrets, build the token/password/refresh services and the current-user-id helper, and wire authentication + authorization in `Program.cs`.

### Changes Required:

#### 1. Packages

**File**: `server/server.csproj`

**Intent**: Add the three first-party auth packages from the locked research.

**Contract**: `dotnet add server package` for `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.IdentityModel.JsonWebTokens`, and `Microsoft.AspNetCore.Identity` (versions aligned to net10.0). Keep `Nullable`/`ImplicitUsings` enabled.

#### 2. JWT options + config binding

**Files**: `server/Auth/JwtOptions.cs`, user-secrets

**Intent**: Strongly-typed JWT settings (issuer, audience, signing key, access TTL, refresh TTL) sourced from configuration; real values live in user-secrets/Railway env.

**Contract**: `JwtOptions` POCO (`Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes = 15`, `RefreshTokenDays = 30`) bound from a config section. Provide dev values via `dotnet user-secrets set` (≥256-bit signing key for HmacSha256). JwtBearer validation binds to the same issuer/audience/key. **Never** add these to `appsettings.json`.

#### 3. Password hashing service

**File**: `server/Auth/PasswordService.cs` (or equivalent)

**Intent**: Wrap `PasswordHasher<User>` for hashing at register and verification at login, handling `SuccessRehashNeeded`.

**Contract**: methods `string Hash(User user, string password)` and a verify method returning success / needs-rehash / failed. On `SuccessRehashNeeded` during login, re-hash and persist. Registered in DI (`AddSingleton`/`AddScoped` per convention).

#### 4. Token service (access JWT issuance)

**File**: `server/Auth/TokenService.cs`

**Intent**: Mint short-lived access JWTs via `JsonWebTokenHandler` carrying only the stable user id.

**Contract**: `string CreateAccessToken(User user)` using `handler.CreateToken(SecurityTokenDescriptor)` with `sub` and `NameIdentifier` = `user.Id.ToString()`, `iss`/`aud`/`exp` (now + `AccessTokenMinutes`)/`nbf`/`iat`/`jti`, signed HmacSha256 with the configured key. **No** email/roles/contact claims.

#### 5. Refresh-token service (rotation + reuse detection)

**File**: `server/Auth/RefreshTokenService.cs`

**Intent**: Issue, rotate, and revoke opaque refresh tokens with `FamilyId` reuse detection; only the SHA-256 hash is persisted.

**Contract**:
- Issue: generate 64 random bytes (`RandomNumberGenerator.Fill`), return the raw opaque token to the caller, persist a `RefreshToken` row storing its SHA-256 hash, a new `FamilyId` (on login) or the inherited family (on rotation), `ExpiresUtc = now + RefreshTokenDays`.
- Exchange/rotate: wrap the whole lookup→consume→insert in a **single DB transaction** (serializable/`SELECT ... FOR UPDATE` on the row) so a concurrent double-submit cannot consume twice or issue two families. Look up by hash; if not found/expired/revoked → reject. If already **consumed**, apply the **benign-retry grace window** before treating it as theft: if the row was consumed within a short grace window (e.g. ~10–30s — final value is a product call) **and** its `ReplacedByTokenId` child row is still live (not consumed/revoked/expired), treat this as a legitimate client retry and return that child's already-issued pair (idempotent replay) — do **not** revoke the family. Only if the consumed token is outside the grace window, or its child has already been consumed/revoked, treat it as **reuse detected**: revoke the entire `FamilyId` and reject (401). Otherwise (token not yet consumed) mark consumed, set `ReplacedByTokenId`, issue a new pair in the same family.
- Revoke-family: used by logout and genuine reuse detection.

#### 6. Current-user-id helper

**File**: `server/Auth/CurrentUser.cs` (extension on `ClaimsPrincipal` / `HttpContext`)

**Intent**: One documented, reused way to extract the caller's `Guid` user id — the single seam every future protected handler (S-05/S-07) uses.

**Contract**: e.g. `Guid GetUserId(this ClaimsPrincipal principal)` reading `sub`/`NameIdentifier` and parsing to `Guid` (throws/returns null-safe on malformed). XML-doc it as the canonical accessor.

#### 7. Program.cs wiring

**File**: `server/Program.cs`

**Intent**: Register authentication + authorization and the auth services before `Build()`.

**Contract**: `builder.Services.AddAuthentication().AddJwtBearer(...)` bound explicitly to `JwtOptions` (the single config source — do not rely on the `Authentication:Schemes:Bearer` auto-bind path) validating issuer/audience/lifetime/signing key against the same values used for issuance, `builder.Services.AddAuthorization()`, and DI registrations for the password/token/refresh services + `JwtOptions`. No explicit `UseAuthentication`/`UseAuthorization` needed (auto-registered; no CORS ordering concern for the mobile client).

### Success Criteria:

#### Automated Verification:

- Solution builds with the new packages and services: `dotnet build solutions/ChoNaBojo.slnx`
- App starts without DI/config errors: `dotnet run --project server` reaches listening state (with dev user-secrets set)

#### Manual Verification:

- With user-secrets configured, the app boots; with the JWT signing key missing, startup fails fast (config is required, not silently defaulted).

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding to endpoints.

---

## Phase 3: Endpoints & authorization baseline

### Overview

Expose `POST /auth/register|login|refresh|logout` with DTOs + validation, apply `[Authorize]` to domain routes, remove the template endpoint, and verify the full flow.

### Changes Required:

#### 1. Auth DTOs

**File**: `server/Auth/AuthDtos.cs` (or per-endpoint records)

**Intent**: Request/response contracts that never leak credential or unexpected contact data.

**Contract**: `RegisterRequest` (loginEmail, password, optional contactPhone, contactEmail, communicatorPlatform, communicatorHandle); `LoginRequest` (loginEmail, password); `RefreshRequest` (refreshToken); `AuthResponse` (accessToken, refreshToken, accessTokenExpiresUtc). Responses exclude `PasswordHash` and do not echo contact fields beyond what the caller submitted.

#### 2. Register/login/refresh/logout endpoints

**File**: `server/Program.cs` (or a mapped `MapAuthEndpoints` extension in `server/Auth/`)

**Intent**: The four auth operations, following the `app.MapPost(...).WithName(...)` minimal-API style.

**Contract**:
- `POST /auth/register`: validate ≥1 complete contact + password policy; reject duplicate normalized login email (409/400); hash password; create `User`; return an `AuthResponse` (issue access + refresh pair, new family).
- `POST /auth/login`: verify credentials (rehash-on-`SuccessRehashNeeded`); on success issue a new access+refresh pair (new family); generic 401 on failure (no user-enumeration signal).
- `POST /auth/refresh`: exchange via the refresh-token service (transactional rotation + grace-window reuse detection); a benign in-window retry idempotently returns the already-issued pair; 401 on invalid/expired/genuinely-reused (out-of-window replay revokes the family).
- `POST /auth/logout`: revoke the presented token's family; 204.
- All four are **anonymous** (they issue/validate their own tokens).

#### 3. Validation

**File**: alongside the endpoints / a small validator

**Intent**: Enforce FR-011 ≥1-contact and communicator platform/handle co-presence at the application layer (400 with a clear message), complementing the DB CHECK.

**Contract**: reject when none of {phone, contactEmail, (communicatorPlatform + communicatorHandle)} is present, or when exactly one of communicatorPlatform/communicatorHandle is present. **Password policy**: min length **8**, max length **128** (guards against pathological inputs to the hasher); no forced complexity classes for the MVP. **Email shape**: basic non-empty + single-`@` shape check on `loginEmail`. **Email normalization**: a single canonical helper — `NormalizeLoginEmail(string) => value.Trim().ToUpperInvariant()` (define it once, e.g. a static method on `User` or an `AuthNormalization` helper) — is the **only** normalizer, called identically at register (to write `NormalizedLoginEmail` and check duplicates), at login (to look the user up), and to populate the value the unique index covers. Register/login must never inline their own trimming/casing.

#### 4. Authorization baseline + cleanup

**File**: `server/Program.cs`

**Intent**: Enforce the PRD "unauthenticated: no access" guardrail on domain routes while keeping health-check public; remove template scaffolding.

**Contract**: establish the **authorized domain route-group seam** (a `.RequireAuthorization()` group that is **empty today** — the real domain endpoints arrive in S-03+) so future protected routes inherit authz by grouping. `/health` stays anonymous. Remove `/weatherforecast` and the `WeatherForecast` record. Since only `/health` + `/auth/*` exist today, add `GET /auth/me` (returns the current user id via the helper, `.RequireAuthorization()`) as the probe that proves the `[Authorize]` baseline works and exercises the current-user helper. **`/auth/me` is retained** beyond F-02 — it returns only the caller's own id, so it is harmless and useful to the S-01 client for verifying its bearer wiring; it is not a temporary throwaway.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- App runs: `dotnet run --project server` serves on http://localhost:5100

#### Manual Verification:

- `POST /auth/register` with ≥1 contact creates a user and returns a token pair; with **no** contact returns 400.
- `POST /auth/login` with correct credentials returns a token pair; wrong password returns 401.
- The protected probe (`/auth/me`) returns 401 without a bearer token and 200 (with the correct user id) with a valid access token.
- `POST /auth/refresh` rotates the pair; replaying a **consumed** refresh token returns 401 **and** revokes the family (a subsequent refresh with the family's latest token also fails).
- `POST /auth/logout` revokes the family; subsequent refresh fails.
- Duplicate registration (same login email) is rejected.

**Implementation Note**: After completing this phase and all automated verification passes, pause for final manual confirmation that the full register → login → protected-call → refresh → logout flow behaves correctly.

---

## Testing Strategy

### Unit Tests:

- No test project exists yet; F-02 does not introduce one (out of scope). Verification is via the automated build/run + manual endpoint checks above.
- If a test project is added later, prioritize: refresh-token rotation + reuse detection (family revocation), password verify + rehash-on-login, and the ≥1-contact validator.

### Integration Tests:

- Manual end-to-end via HTTP client (see Manual Testing Steps) for the MVP.

### Manual Testing Steps:

1. Set dev user-secrets: JWT signing key (≥256-bit), issuer, audience, and `AppDb`/`AppDbMigrations` connection strings.
2. `dotnet ef database update --project server`, then `dotnet run --project server`.
3. `POST /auth/register` with only a `communicatorPlatform` + `communicatorHandle` → expect 200 + token pair. Repeat with no contact → expect 400.
4. `POST /auth/login` → capture access + refresh tokens.
5. `GET /auth/me` with `Authorization: Bearer <access>` → 200 with the user id; without header → 401.
6. `POST /auth/refresh` with the refresh token → new pair. Replay the **old (consumed)** refresh token → 401; then try the newest token → also 401 (family revoked).
7. Fresh login → `POST /auth/logout` → 204; subsequent refresh with that family → 401.

## Performance Considerations

- Refresh-token lookup is by SHA-256 hash — indexed. Family revocation is a single indexed `UPDATE` over `FamilyId`.
- Access-token TTL of 15 min bounds validation load; JWT validation is stateless (no DB hit) — only `/auth/*` touch the DB.
- `JsonWebTokenHandler` is the recommended (≈30% faster) issuance path per research.

## Migration Notes

- Single additive migration (`AddAuthTables`); no existing data to migrate (fresh tables).
- Applied out-of-band via `dotnet ef database update` on the session-mode `AppDbMigrations` string; runtime keeps the pooled `AppDb`. The app never calls `Database.Migrate()`.

## References

- Frame brief: `context/changes/auth-scaffold/frame.md` (provider decision — self-hosted)
- Library/security research: `context/changes/auth-scaffold/research-jwt-implementation.md`
- Codebase-integration + per-event authz research: `context/changes/auth-scaffold/research.md`
- Roadmap F-02: `context/foundation/roadmap.md`
- Conventions to mirror: `server/Data/ChoNaBojoContext.cs:13-95`, `server/Data/Entities/Sport.cs`, `server/Program.cs:26-48`
- Prior data-layer change (conventions + never-auto-migrate rule): `context/archive/2026-07-12-data-layer-foundation/`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Data model & migration

#### Automated

- [x] 1.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — 1307776
- [x] 1.2 Migration generates without model errors: `dotnet ef migrations add AddAuthTables --project server` — 1307776
- [x] 1.3 Migration applies cleanly against the dev DB: `dotnet ef database update --project server` — 1307776

#### Manual

- [x] 1.4 `Users` and `RefreshTokens` tables exist with expected columns, unique login-email index, and ≥1-contact CHECK constraint — 1307776
- [x] 1.5 Inserting a user with no contact method is rejected by the DB — 1307776

### Phase 2: Auth infrastructure (packages, config, services, wiring)

#### Automated

- [x] 2.1 Solution builds with the new packages and services: `dotnet build solutions/ChoNaBojo.slnx` — 5a51c3f
- [x] 2.2 App starts without DI/config errors: `dotnet run --project server` reaches listening state — 5a51c3f

#### Manual

- [x] 2.3 App boots with user-secrets set; startup fails fast when the JWT signing key is missing — 5a51c3f

### Phase 3: Endpoints & authorization baseline

#### Automated

- [ ] 3.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [ ] 3.2 App runs: `dotnet run --project server` serves on http://localhost:5100

#### Manual

- [ ] 3.3 Register with ≥1 contact returns a token pair; register with no contact returns 400
- [ ] 3.4 Login with correct credentials returns a token pair; wrong password returns 401
- [ ] 3.5 Protected probe returns 401 without a bearer token and 200 (correct user id) with a valid access token
- [ ] 3.6 Refresh rotates the pair; replaying a consumed refresh token returns 401 and revokes the family
- [ ] 3.7 Logout revokes the family; subsequent refresh fails
- [ ] 3.8 Duplicate registration (same login email) is rejected
