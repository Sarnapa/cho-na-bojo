<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Auth Scaffold (F-02)

- **Plan**: context/changes/auth-scaffold/plan.md
- **Mode**: Deep
- **Date**: 2026-07-16
- **Verdict**: REVISE → SOUND (all findings triaged & fixed 2026-07-16)
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING → PASS (F1, F5 fixed) |
| Plan Completeness | WARNING → PASS (F2, F3, F4 fixed) |

## Grounding

6/6 paths ✓ (ChoNaBojoContext.cs, Program.cs, Sport.cs, VenueSport.cs, launchSettings.json, Migrations/), symbols ✓ (ValueGeneratedNever, HasPostgresExtension, DbSet property-block style, port 5100), Progress↔Phase mechanical contract ✓, brief↔plan ✓. New auth files (User.cs, RefreshToken.cs, Auth/*) do not exist yet — expected, they are to be created.

## Findings

### F1 — Refresh rotation will false-positive on concurrent client retries

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 §5 (RefreshTokenService), Phase 3 §2 (/auth/refresh)
- **Detail**: The exchange logic ("if already consumed → reuse detected → revoke the whole family") treats any replay of a consumed token as theft. A mobile client on a flaky network commonly fires the same refresh twice (retry after timeout, resume from background, two screens refreshing at once). The first request consumes the token; the second — a legitimate retry with the same token — trips reuse detection and revokes the entire family, silently logging the real user out. This is the exact "riskiest untested path" the brief names. The plan has no grace window, idempotency, or concurrency guard, and doesn't specify that lookup+consume+insert must be a single transaction (a race can consume twice or issue two families).
- **Fix A ⭐ Recommended**: Add a short reuse grace window + make rotation transactional
  - Strength: Keeps theft detection but tolerates benign double-submit — if a just-consumed token is replayed within a small window AND its ReplacedByTokenId child is still live/unconsumed, return that child's pair (or 401 without family revocation) instead of revoking the family. Wrap lookup→consume→insert in one DB transaction.
  - Tradeoff: Slightly more logic + a defined grace window to reason about.
  - Confidence: HIGH — standard rotating-refresh mitigation for mobile clients.
  - Blind spot: Exact window length is a product call; not yet chosen.
- **Fix B**: Document as accepted risk with a client-side single-flight guard
  - Strength: No server change; push dedupe to the S-01 MAUI client (serialize refreshes through one in-flight call).
  - Tradeoff: Server stays fragile to any non-single-flight caller; the guarantee lives in a client that doesn't exist yet (S-01).
  - Confidence: MEDIUM — works only if every future client cooperates.
  - Blind spot: No client exists in F-02 to prove the mitigation.
- **Decision**: FIXED via Fix A (grace window + transactional rotation)

### F2 — Password policy and email normalization are unspecified

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 §3 (Validation); Phase 1 §2 (User entity)
- **Detail**: Two contracts the implementer must guess at: (1) "validate ... password policy" and "Basic email/password shape validation" never state the actual rule (min length? max? complexity?). (2) `NormalizedLoginEmail` backs the unique index and duplicate-registration check, but the normalization function is undefined (lowercase vs. ToUpperInvariant, trim, culture). Get it wrong and "Bob@x.com" vs "bob@x.com" either both register or collide inconsistently between the C# check and the DB index.
- **Fix**: State concrete rules in the plan: e.g. password min length (≥8) with a defined max; normalize login email via Trim + ToUpperInvariant (or lower) applied identically at register, login, and index — name the one helper both paths call.
- **Decision**: FIXED (password 8–128, `NormalizeLoginEmail` = Trim + ToUpperInvariant single helper)

### F3 — Two possible sources of truth for JWT config

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Key Discoveries + Phase 2 §2/§7
- **Detail**: Key Discoveries notes JwtBearer auto-binds from `Authentication:Schemes:Bearer`, but Phase 2 chooses a custom `JwtOptions` section + explicit AddJwtBearer(options => ...). Both are valid, but mixing the two narratives could lead an implementer to split the signing key across two config sections, so validation silently reads a different key than issuance.
- **Fix**: State plainly that JwtOptions (custom section) is the single source and AddJwtBearer is configured explicitly from it; don't rely on the auto-bind path.
- **Decision**: FIXED (JwtOptions is the single source; auto-bind path explicitly ruled out)

### F4 — "[Authorize] baseline on domain routes" has no domain routes; temp probe has no removal owner

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Overview / Desired End State; Phase 3 §4
- **Detail**: After /weatherforecast is removed there are zero domain routes, so ".RequireAuthorization() on domain route groups" is a no-op today — the only thing exercising authz is the "temporary" /auth/me probe, which the plan never says who removes or when. Not a correctness gap (the plan acknowledges the probe), but the framing can send the implementer hunting for routes that don't exist.
- **Fix**: Reword to "establish the authorized route-group seam (empty today) and add /auth/me as the probe"; state whether /auth/me stays (it only returns the caller's own id — harmless) or is removed in S-01.
- **Decision**: FIXED (seam framed as empty-today; /auth/me retained as harmless probe)

### F5 — No pruning for expired/consumed refresh tokens

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 §3 / Migration Notes
- **Detail**: RefreshTokens rows are never deleted; consumed/expired/revoked rows accumulate indefinitely. Fine for the MVP's low volume, but the tech-stack already plans background jobs — worth a one-line note so it isn't silently forgotten.
- **Fix**: Add a note deferring a pruning job (delete where ExpiresUtc < now) to the background-jobs work; no F-02 action required.
- **Decision**: FIXED (pruning job deferred to background-jobs work; noted in "What We're NOT Doing")
