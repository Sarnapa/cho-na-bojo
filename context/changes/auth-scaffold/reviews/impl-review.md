<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Auth Scaffold (F-02)

- **Plan**: context/changes/auth-scaffold/plan.md
- **Scope**: Full plan (Phases 1–3 of 3)
- **Date**: 2026-07-17
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 6 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Benign-retry grace window depends on volatile in-memory cache

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: server/Auth/RefreshTokenService.cs:83,106-121,150
- **Detail**: The plan's benign-retry design says a just-consumed token whose live child still exists should idempotently return the child's pair with **no revocation**. Because only the SHA-256 hash is stored, the implementation caches the raw child `TokenPair` in `IMemoryCache` (an unplanned mechanism — EXTRA) and gates the benign-retry branch on a cache hit (`_memoryCache.TryGetValue`). On a server restart or cache eviction, a legitimate in-window retry misses the cache, falls through to `RevokeFamilyAsync`, and revokes the user's **entire token family** — forcing a full re-login. This inverts the intended "benign" behavior into the most destructive one. Separately, holding a consumed token within the 20s window returns valid live tokens to any presenter — this is the plan's deliberate tradeoff ("final value is a product call") but the raw-token-in-memory store widens its blast radius.
- **Fix A ⭐ Recommended**: On a cache miss within the grace window with a live child, do not revoke — return a "retry with your stored pair" signal (e.g. 409/425) and leave the family intact.
  - Strength: Removes the family-revocation failure mode entirely and avoids persisting raw refresh tokens; the client already holds its issued pair.
  - Tradeoff: Adds a small client contract (handle the retry status by reusing its stored pair).
  - Confidence: MED — depends on the S-01 client honoring the retry contract, which isn't built yet.
  - Blind spot: The S-01 MAUI client's retry behavior is unimplemented, so the contract can't be verified end-to-end today.
- **Fix B**: Persist the idempotency record durably (child pair in a distributed/durable store keyed to the parent) instead of `IMemoryCache`.
  - Strength: Preserves the exact planned idempotent-replay UX across restarts.
  - Tradeoff: Stores raw refresh-token material at rest (or a distributed cache dependency), reintroducing a secret-at-rest surface the SHA-256 design deliberately removed.
  - Confidence: MED — correct but at odds with the "hash only, never raw" principle the rest of the service follows.
  - Blind spot: No durable cache is provisioned yet; adds infra scope.
- **Decision**: FIXED via Fix A (variant) — removed `IMemoryCache` entirely; a live child within the grace window now returns `RetryInProgress` → HTTP 409 (client retries with its stored pair) and the family is never revoked. Also removed `AddMemoryCache()` from Program.cs.

### F2 — Logout / family revocation runs without the transactional discipline used by rotation

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: server/Auth/RefreshTokenService.cs:163-180
- **Detail**: `RotateAsync` correctly wraps lookup→consume→insert in a `Serializable` transaction with `FOR UPDATE` row locks. The public `RevokeFamilyAsync(string, …)` (logout + reuse path) instead does an `AsNoTracking` lookup then a separate `ExecuteUpdateAsync` with no transaction or lock. A rotation racing with a logout can create a new replacement token *after* the revoke snapshot, leaving a live token in a family the user believes is revoked.
- **Decision**: FIXED — public `RevokeFamilyAsync(string,…)` now runs inside a `Serializable` transaction and locks the token row via `LockRefreshTokenByHashAsync` (`FOR UPDATE`) before `ExecuteUpdateAsync`, matching `RotateAsync`.

### F3 — Register duplicate-email race falls through to an unhandled DB exception

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: server/Auth/AuthEndpoints.cs:54-79
- **Detail**: Register does an `AnyAsync` pre-check then `SaveChangesAsync`. Two concurrent registrations for the same normalized email both pass the pre-check; the unique index correctly rejects the second insert, but the resulting `DbUpdateException` is not caught — the client gets a 500 instead of the intended `409` conflict.
- **Decision**: FIXED — `SaveChangesAsync` in Register is wrapped in try/catch; a `DbUpdateException` whose inner `PostgresException.SqlState` is the unique-violation code (`23505`) now returns the same generic 409 as the pre-check path.

### F4 — Login reveals user existence via early return before password verification

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: server/Auth/AuthEndpoints.cs:100-107
- **Detail**: For a non-existent email, login returns `401` immediately without running the password hasher; for an existing email it runs `PasswordHasher.VerifyHashedPassword`. The measurable timing difference is a user-enumeration oracle. The plan explicitly required "generic 401 on failure (no user-enumeration signal)".
- **Fix**: Perform a dummy hash verification against a constant fake hash when the user is missing so both paths take comparable time before returning the same generic 401.
  - Strength: Removes the timing oracle and satisfies the plan's stated no-enumeration requirement.
  - Tradeoff: One extra hash computation on missing-user logins.
  - Confidence: HIGH — well-established anti-enumeration pattern.
  - Blind spot: Register still returns an explicit 409 (see note); full enumeration hardening spans both endpoints.
- **Decision**: PENDING

### F5 — Anonymous auth endpoints have no rate limiting or lockout

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: server/Auth/AuthEndpoints.cs:13-27, server/Program.cs
- **Detail**: `/auth/register|login|refresh` are anonymous with no throttling or backoff, making them brute-force / credential-stuffing targets. Not called out in the plan's scope, but it is a live security surface for a launched auth system.
- **Fix**: Add ASP.NET Core rate limiting (per IP/email) on the auth group, or record it as an explicit tracked follow-up if intentionally deferred for the MVP.
  - Strength: Cheap built-in middleware materially raises the cost of online guessing.
  - Tradeoff: Needs tuned limits to avoid blocking legitimate mobile retries.
  - Confidence: MED — appropriate for the boundary, but may be a deliberate post-MVP deferral given the 3-week scope.
  - Blind spot: Product's risk appetite for MVP brute-force exposure isn't stated.
- **Decision**: PENDING

### F6 — Authorized route-group seam is discarded, so nothing can inherit it

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: server/Program.cs:102-103
- **Detail**: The plan wanted an authorized domain route-group seam "so future protected routes inherit authz by grouping." The implementation writes `_ = app.MapGroup("/api").RequireAuthorization();` — the group is assigned to a discard, so there is no reusable handle; future endpoints mapped on `app` won't inherit it and a developer must recreate the group. Harmless today (no domain routes exist), but the seam as written can't fulfill its stated purpose.
- **Fix**: Keep the group in a named variable (or expose it via a small helper) so S-03+ endpoints map onto it, and document it as the protected seam.
- **Decision**: PENDING

### F7 — Planned Microsoft.AspNetCore.Identity package not added

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: server/server.csproj
- **Detail**: Phase 2 §1 lists three packages; `Microsoft.AspNetCore.Authentication.JwtBearer` and `Microsoft.IdentityModel.JsonWebTokens` are present but `Microsoft.AspNetCore.Identity` is not. It builds because `PasswordHasher<T>` resolves transitively via `Microsoft.Extensions.Identity.Core`. Functionally fine; a plan/implementation mismatch only.
- **Fix**: Either add the package explicitly for an intentional direct dependency, or update the plan to note the transitive resolution.
- **Decision**: PENDING

### F8 — CommunicatorPlatform accepts undefined enum integers

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: server/Auth/AuthEndpoints.cs:185-190, server/Data/Entities/CommunicatorPlatform.cs:6-10
- **Detail**: Validation only checks `CommunicatorPlatform.HasValue`; any integer (e.g. 99) binds and would be persisted since there is no enum CHECK constraint. The stored value would not map to a defined platform.
- **Fix**: Add an `Enum.IsDefined(request.CommunicatorPlatform.Value)` validation check (and optionally a DB CHECK constraint on the column).
- **Decision**: PENDING

### F9 — RefreshTokens.TokenHash index is non-unique despite single-row lookups

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: server/Data/ChoNaBojoContext.cs:174, server/Migrations/20260716215146_AddAuthTables.cs:71-74
- **Detail**: `IX_RefreshTokens_TokenHash` is a non-unique index but lookups use `SingleOrDefault`/`FOR UPDATE`. Collisions on a 256-bit hash are impractical, but a unique index would enforce the invariant the code assumes.
- **Fix**: Mark the `TokenHash` index unique (`HasIndex(r => r.TokenHash).IsUnique()`) in a follow-up migration.
- **Decision**: PENDING

### F10 — Minor doc/normalization polish

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: server/Data/Entities/User.cs:8-38, server/Auth/AuthEndpoints.cs:51-52
- **Detail**: (a) `User.Id`, timestamps, and `RefreshTokens` lack the concise XML-doc summaries the entity convention uses elsewhere. (b) Register normalizes the login email via `NormalizeOptionalText(...)` then `NormalizeLoginEmail(...)`, while login calls `NormalizeLoginEmail` directly; the plan wanted one canonical normalizer path. Output is identical (trim-then-upper), so this is cosmetic, but the double-call diverges from the "single normalizer, called identically" intent.
- **Fix**: Add the missing XML-doc summaries and call `NormalizeLoginEmail` directly on the raw login email in register to match login.
- **Decision**: PENDING
