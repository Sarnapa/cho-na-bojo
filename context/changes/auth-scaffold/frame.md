# Frame Brief: Firebase Authentication vs. self-hosted auth (auth-scaffold)

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

The Foundation (roadmap F-02 + tech-stack) specifies email+password auth with
self-hosted JWT issue/validate, BCrypt/Argon2 hashing, and `[Authorize]`
middleware on the ASP.NET Core API. It names **no** auth provider. Firebase
appears in the stack only as **FCM** (push notifications). Supabase Postgres is
the database. Nothing auth-related exists in the codebase yet (`server/Program.cs`
is a minimal API with EF Core + `/health` only; the MAUI client has a typed
`HttpClient` and no auth).

## Initial Framing (preserved)

- **User's stated cause or approach**: Considering Firebase Authentication;
  unsure because the Foundation never mentions it. Wondering if it's "reasonable
  or overkill."
- **User's proposed direction**: Possibly adopt Firebase Auth for auth-scaffold
  instead of hand-rolling auth on the API.
- **Pre-dispatch narrowing**: Leading motive is a combination of **consolidation**
  ("Firebase is already planned for FCM, so reusing it for auth feels natural")
  and **code reduction** ("reduce the amount of auth code I write & maintain solo
  under the deadline"). Not a security-offload or future-OAuth motive.

## Dimension Map

The "Firebase Auth: reasonable or overkill?" question originates across:

1. **Identity issuance/validation** — Foundation prescribes self-signed JWT (API
   issues + validates). Firebase would issue ID tokens; API verifies them.  ← initial framing
2. **Credential storage vs. the Users table** — the privacy boundary REQUIRES a
   `Users` table in Postgres (contact info + per-event roles) either way.
3. **Actual auth-code burden** — how large is the Foundation-scoped auth really,
   and does Firebase meaningfully shrink it? (the "reduce code" motive)
4. **Consolidation premise** — does already having a Firebase project for FCM
   lower Firebase Auth's marginal cost? (the "FCM is already there" motive)
5. **Persistent session (S-01)** — Firebase client SDK auto-refreshes tokens;
   self-hosted needs a refresh strategy.
6. **Integration/operational cost on MAUI-Android + EU** — client SDK quality,
   data residency, vendor lock-in, 3-week timeline risk.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| D3/D5 — Firebase materially reduces auth **code** | Research: ~50–55 lines (Firebase) vs ~200–250 lines (self-hosted); the delta is almost entirely the refresh-token/session-persistence infrastructure Firebase's Android SDK handles automatically. | STRONG (for code volume) |
| D4 — Consolidation premise holds | Research: FCM already pays for the Firebase project, `google-services.json`, SHA-1, service account, and `FirebaseAdmin`; Auth adds only `Plugin.Firebase.Auth` NuGet + a Console toggle + ~35 lines. | STRONG |
| D2 — `firebase_uid`↔Users creates a two-source-of-truth burden | Research: a `Users` row + `firebase_uid` FK is mandatory regardless; email lives in both Firebase and Postgres; fragile register-time bootstrap; auth data still not co-located. | STRONG |
| D6 — EU data-residency loss | Research: Firebase Auth offers **no** region selection; auth data (emails, hashes) processed on US infra under GDPR SCCs. Material for a Warsaw-only, privacy-first app. | STRONG |
| D6 — Sole viable client SDK is a one-person community plugin | Research: `Plugin.Firebase.Auth` v5.0.1 is the only production-viable MAUI-Android option; single maintainer, recent breaking changes (v5 error model). Alternatives stale/incomplete. | STRONG |
| Firebase reduces the **launch-gate** (per-event authorization) risk | Per-event role check + contact-reveal-only-after-acceptance lives in the API + Postgres regardless; Firebase Auth is pure authentication and does not touch it. | NONE (no help) |

## Narrowing Signals

- **Pre-dispatch**: motive is consolidation + code reduction, not security or OAuth.
- **Post-investigation (decisive)**: asked where the user lands on the real
  tradeoff (US auth-data + permanent lock-in vs. ~150 fewer lines). User was
  persuaded that (a) Firebase Auth adds little for per-event authorization and
  (b) a `firebase_uid` must be stored in Postgres anyway — and chose to **drop**
  Firebase Auth. This narrows the decision away from the D3/D5 "less code" pull
  toward the D2/D6 residency + lock-in + convention concerns.

## Cross-System Convention

The project's own tech-stack.md was chosen against four quality gates
(`typed`, `from_official_starter`, `conventions`, `docs_current`) precisely so an
AI agent can navigate "convention-based scaffolding predictably." Self-hosted auth
uses Microsoft's first-party `Microsoft.AspNetCore.Authentication.JwtBearer` +
`BCrypt.Net-Next` — official, well-documented, stable. Firebase Auth on MAUI would
depend on a single-maintainer community binding with recent breaking changes,
cutting against `from_official_starter` and `docs_current`. Convention favors the
Foundation's original self-hosted direction.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: implement the Foundation-scoped
> self-hosted email+password auth (Users table, register/login, password hashing,
> JWT issue+validate, `[Authorize]` middleware) — and treat the **persistent-session /
> refresh-token strategy** as the deliberate, load-bearing design decision, because
> that is the one piece Firebase would have handled for free and is the most
> underestimated cost of self-hosting.

The initial framing raised a real alternative (Firebase Auth), but the
investigation confirms the Foundation's original direction. The framing was
sharpened, not overturned: the decision was never "overkill vs. not" on effort —
Firebase Auth is genuinely *less* code — it is a **privacy/residency + vendor
lock-in** tradeoff, and Firebase Auth does not reduce the launch-gate risk
(per-event authorization) at all. For a Warsaw-only, privacy-first app that must
store `firebase_uid` in Postgres regardless, the residency loss, permanent
Firebase coupling, and reliance on a one-person SDK outweigh the code savings.
User confirmed: **proceed self-hosted.**

## Confidence

**HIGH** — strong, converging evidence on every dimension; matches the project's
own convention/quality-gate rationale; decisive user narrowing signal
(explicitly dropped Firebase Auth after seeing the per-event-authz and
`firebase_uid` arguments).

## What Changes for /10x-plan

Plan auth-scaffold as originally scoped (self-hosted email+password JWT on
ASP.NET Core), with **no Firebase Auth**. Give explicit attention to two things
the investigation flagged as the real cost/risk: (1) the session-persistence /
refresh-token strategy for "stay logged in across app restarts" (short-lived JWT
+ refresh token + rotation, or a justified long-lived-token decision — this is
where self-hosting spends its code), and (2) the per-event authorization model
(organizer vs. participant, contact-reveal-only-after-acceptance), which is the
launch gate and is unaffected by the auth-provider choice.

## References

- Source files: `server/Program.cs`, `server/server.csproj`,
  `server/Data/ChoNaBojoContext.cs`, `app/ChoNaBojoApp/MauiProgram.cs`,
  `app/ChoNaBojoApp/Services/ApiService.cs`
- Foundation: `context/foundation/roadmap.md` (F-02), `context/foundation/tech-stack.md`,
  `context/foundation/shape-notes.md` (auth scope-down), `context/foundation/prd.md`
  (FR-001/002/011, Access Control, privacy NFR), `context/foundation/infrastructure.md`
- Investigation: research sub-agent "firebase-auth-maui-reality" (Firebase Auth on
  .NET MAUI Android + ASP.NET Core reality check; SDK options, token verification,
  consolidation, code-burden, EU residency)
