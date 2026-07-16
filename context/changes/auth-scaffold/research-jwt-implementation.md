---
change_id: auth-scaffold
doc: research
created: 2026-07-16
updated: 2026-07-16
sources: exa web search + Microsoft Learn (.NET 10)
---

# Research: F-02 Auth Scaffold — JWT, password hashing, refresh tokens

> Roadmap ref: `context/foundation/roadmap.md` → **F-02: auth-scaffold**.
> Goal of F-02: `Users` table, `POST /auth/register` + `POST /auth/login` (email+password, at least one contact), password hashing, JWT issue+validate, `[Authorize]` middleware on all domain routes. No UI (UI lands in S-01).
> Stack (verified from `server/server.csproj`): **.NET 10**, minimal APIs, **EF Core 10** (`Microsoft.EntityFrameworkCore.Design` 10.0.3), **Npgsql** 10.0.3, `Nullable` + `ImplicitUsings` enabled, `RootNamespace = ChoNaBojo.Server`, UserSecretsId already configured.

## Decisions (locked)

- **Password hashing: built-in `Microsoft.AspNetCore.Identity` `PasswordHasher<T>`.** (User decision, 2026-07-16.)
  Rationale: lowest risk of misuse, auto-salt, versioned/upgradeable, verification handled for us; no need to adopt the full ASP.NET Core Identity system — just the hasher. Best fit for a 3-week solo after-hours MVP where the privacy boundary is a launch gate. BCrypt/Argon2 remain viable alternatives but add a dependency + manual tuning for no MVP-level benefit.

## 1. JWT validation (middleware side) — `Microsoft.AspNetCore.Authentication.JwtBearer`

Official, canonical package. On .NET 10 minimal APIs:

```csharp
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();
```

- Options load from configuration under `Authentication:Schemes:Bearer` (issuer, audience, signing key).
- With `WebApplication`, calling `AddAuthentication`/`AddAuthorization` auto-registers the middleware — no explicit `UseAuthentication()`/`UseAuthorization()` needed **unless** middleware order matters (e.g. CORS must run first, which our `Program.cs` may need).
- Protect domain routes with `.RequireAuthorization()` on route groups → satisfies the roadmap's "`[Authorize]` middleware protecting all domain routes" and the PRD privacy guardrail ("Unauthenticated user: no access").
- `dotnet user-jwts` is useful for **dev-only** token testing, NOT a production issuer.

Sources:
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0

## 2. JWT issuance (token minting) — `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler`)

Use the **new** `JsonWebTokenHandler`, NOT the legacy `System.IdentityModel.Tokens.Jwt` / `JwtSecurityTokenHandler`.

- Microsoft/AzureAD explicitly steer new code to `JsonWebTokenHandler`: **~30% perf improvement** (up to 100% in some scenarios), better async/resilience.
- Create tokens via `handler.CreateToken(SecurityTokenDescriptor)`.
- Most older tutorials still show `JwtSecurityToken` + `WriteToken` — functional but not the recommended path for greenfield code.

Sources:
- https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/securitytoken-events?view=aspnetcore-10.0
- https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/2270
- https://stackoverflow.com/questions/60455167/why-we-have-two-classes-for-jwt-tokens-jwtsecuritytokenhandler-vs-jsonwebtokenha

## 3. Password hashing — chosen: built-in `PasswordHasher<T>`

`Microsoft.AspNetCore.Identity` ships `PasswordHasher<TUser>`: PBKDF2, auto-salt, versioned (can upgrade algorithm/iterations later), secure verification. Usable standalone without the full Identity stack.

```csharp
var hasher = new PasswordHasher<User>();
string hash = hasher.HashPassword(user, plainPassword);
var result = hasher.VerifyHashedPassword(user, hash, plainPassword);
// result == PasswordVerificationResult.Success | SuccessRehashNeeded | Failed
```

Handle `SuccessRehashNeeded` → re-hash and persist on next successful login (future-proofs parameter bumps).

Best-practice checklist (applies regardless of algorithm):
- Never store plain text; never use fast hashes (MD5/SHA1) for passwords.
- Always salted + adaptive.
- Rehash-on-login when parameters are upgraded.

Alternatives considered (not chosen): `BCrypt.Net-Next` (WorkFactor 12–13, single knob, CPU-bound), `Isopoh.Cryptography.Argon2` / `Konscious.Security.Cryptography` (Argon2id, memory-hard, strongest but more tuning).

Sources:
- https://www.c-sharpcorner.com/article/secure-password-hashing-in-net-best-practices-for-modern-applications/
- https://dev.to/imzihad21/bcrypt-vs-argon2-password-hashing-in-net-a-practical-deep-dive-54co

## 4. Refresh tokens — production-shaped custom flow

We roll our own (no full Identity system). Recommended shape:

- **Short-lived access JWT** (5–15 min) + **long-lived opaque refresh token** (7–30 days).
- Store refresh tokens **server-side, only the SHA-256 hash at rest** → new `RefreshTokens` table in `ChoNaBojoContext` (fits EF Core 10 / Npgsql).
- **Rotate on every exchange**: mark old row consumed, issue a new pair.
- **Reuse detection via `FamilyId`**: all tokens from one login share a family; if a consumed token is replayed (theft), revoke the entire family.
- Generate raw token with `RandomNumberGenerator.Fill` (64 bytes / 512 bits).

Suggested entity (adapt to `ChoNaBojo.Server` conventions):

```csharp
public class RefreshToken
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string TokenHash { get; set; } // SHA-256 of opaque token
    public Guid FamilyId { get; set; }             // shared across one login chain
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? ConsumedUtc { get; set; }      // set when rotated
    public DateTime? RevokedUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? CreatedByIp { get; set; }
}
```

Note: password reset & OAuth are Parked (roadmap §Parked) — F-02 keeps the contract minimal. Refresh-token TTL/rotation depth can be scoped in `/10x-plan`.

Source:
- https://startdebugging.net/2026/04/how-to-implement-refresh-tokens-in-aspnetcore-identity/

## Recommended package set for F-02

```
Microsoft.AspNetCore.Authentication.JwtBearer   # validation / middleware
Microsoft.IdentityModel.JsonWebTokens           # issuance — JsonWebTokenHandler
Microsoft.AspNetCore.Identity                    # password hashing — PasswordHasher<T> (CHOSEN)
```

## Config / secrets

- JWT signing key + issuer + audience → **user-secrets (dev)** (`UserSecretsId` already set in `server.csproj`) and **Railway env vars (prod)**.
- NEVER in `appsettings.json` (repo hard rule + PRD privacy boundary).
- Use a ≥256-bit symmetric key for `HmacSha256`.

## Open items to resolve in `/10x-plan`

- Access-token TTL and refresh-token TTL values.
- Whether to ship refresh-token reuse detection (`FamilyId`) in F-02 or defer to a follow-up.
- Contact-info capture shape (`phone` / `email` / `messenger`, ≥1 required) on the `Users` table — how it maps to the register DTO.
- Per-event role check design (organizer vs participant) — reused by S-05/S-07, so design solidly once (per roadmap Risk note).
