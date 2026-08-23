# S-01: Registration, Login, and Persistent Session — Plan Brief

> Full plan: `context/changes/account-and-session/plan.md`

## What & Why

Deliver the first user-visible slice of ChoNaBojo: a MAUI Android user can register (email + password + at least one shareable contact), log in, stay logged in across app restarts, and log out. The F-02 auth API is already complete, so this slice is entirely client-side. It also lays down the MVVM structure, session plumbing, navigation gating, and reusable design-system styles that every later slice (S-02+) depends on.

## Starting Point

The MAUI app is scaffolded (UraniumUI 3.0, a named `"ChoNaBojoApi"` HttpClient) but has only `CheckHealthAsync()`, a single template counter `MainPage`, and design tokens without control styles. No MVVM toolkit, no `SecureStorage` use, no auth screens, no token/refresh handling. The server already exposes `/auth/register|login|refresh|logout` + `/auth/me`, with contracts and a framework-neutral `AuthValidation` in the shared projects (already referenced by the app).

## Desired End State

A logged-out user only ever sees Login (privacy boundary). Register auto-logs them in and lands on a minimal Home. Relaunching keeps them logged in (tokens restored from `SecureStorage`, refreshed transparently). Logout returns to Login and revokes the refresh family. An invalid/expired refresh auto-signs them out with an explaining snackbar. Every screen is built from UraniumUI Material controls against the design tokens — no hardcoded values, 8pt grid, MD3 pill buttons, ≥48 touch targets.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Screen structure | CommunityToolkit.Mvvm (source-gen VMs) | Least boilerplate, testable, MAUI standard that scales to S-02+. | Plan |
| Navigation gating | Swap window root (Auth flow ↔ App Shell) | Clean separation; no app pages linger in the back-stack — best privacy story. | Plan |
| Token attach + expiry | DelegatingHandler on the named client, pre-flight expiry check + single-flight refresh on 401 | Transparent to every service; one place enforces the 15-min expiry (proactive) and recovers from a 401 (reactive). | Plan |
| Cold-start routing | Optimistic restore (enter app if a refresh token exists) | Fastest startup, no blocking network at launch, works offline until a call fails. | Plan |
| Contact collection | All three fields optional + live "≥1 required" rule | Matches shared `AuthValidation` exactly; user freely shares one or several methods. | Plan |
| After register | Auto-login (store the tokens the server already returns) | Free (server returns a pair on register); no credential re-entry. | Plan |
| Post-login landing | Minimal design-system Home placeholder (replaces counter) | On-brand landing + a home for logout; replaced by the S-02 map later. | Plan |
| Logout | In scope (revoke family, clear storage, root-swap) | Completes the persist→restore→end lifecycle this slice owns. | Plan |
| Design-system styles | Reusable `material:TextField`/`MaterialButton` styles in `Styles.xaml` | Enforces ui-guidelines centrally; reused by every later slice. | Plan |
| Errors | Reuse shared `AuthValidation` inline + map server 400 keys; 401/409 snackbar | Single rule set (no drift); instant UX; server stays authoritative. | Plan |
| Session-expiry UX | Auto-sign-out to Login + explaining snackbar | Safe default honoring the privacy boundary; one choke point. | Plan |
| Testing | Manual-only, documented emulator steps (like F-02) | Fastest, consistent with the repo; no test infra exists yet. | Plan |

## Scope

**In scope:** Register + Login screens, ≥1-contact collection, `SecureStorage` token persistence, optimistic-restore startup, transparent bearer + single-flight refresh, auth↔app root gating, minimal Home, logout, session-expiry sign-out, reusable Material control styles.

**Out of scope:** Any server/API change; map/venue/event UI (S-02+); password reset, email verification, OAuth, "remember me"; automated tests; iOS; Dark theme; post-registration profile editing.

## Architecture / Approach

Bottom-up, mirroring F-01/F-02. **Foundation** (token store over `SecureStorage` → session service → typed API client with mapped errors → bearer/refresh `DelegatingHandler` → root-swap navigation service → DI → control styles) with no visible screens. Then **Login + Auth flow + Home + startup routing**, then **Register + auto-login**, then **logout + expiry**. MVVM via CommunityToolkit.Mvvm; Snackbar/confirm via CommunityToolkit.Maui; screens consume design tokens + new control styles exclusively.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Foundation + styles | Token store, session, API client, refresh handler, root-swap, DI, control styles | Single-flight refresh vs server reuse-detection; `SecureStorage` main-thread startup gotcha |
| 2. Login + gating | Login screen, Auth flow, Home placeholder, optimistic-restore routing | Root-swap must clear back-stack; offline-at-launch handling |
| 3. Register | Register screen, contact-collection UX, auto-login | Paired platform+handle + "≥1 contact" mapping to fields |
| 4. Logout + expiry | Logout (revoke+clear+root-swap), auto-sign-out on refresh failure | Idempotent single sign-out under concurrent failures |

**Prerequisites:** F-02 (done); a running API (dev `http://10.0.2.2:5100`); Android emulator.
**Estimated effort:** ~3–4 sessions across 4 phases (solo, after-hours).

## Open Risks & Assumptions

- The refresh handler is the riskiest code: single-flight + `409 RetryInProgress`/`401` handling must not loop or trip the server's family-reuse revocation; verified manually only.
- `SecureStorage` first access on Android 10+ must be on the main thread; failures route to Login rather than crash.
- CommunityToolkit.Maui must resolve against `MauiVersion` 10.0.71 (net10) — pick a compatible version.
- Manual-only verification means refresh/session/gating (the riskiest paths) are checked by hand; documented steps mitigate.

## Success Criteria (Summary)

- A user can register (with ≥1 contact) → auto-login → Home, and log in later with the same account.
- The session survives an app restart and can be ended via logout; an invalid refresh auto-signs the user out with an explanation.
- No app content is ever shown to an unauthenticated user, and every screen conforms to `ui-guidelines.md`.
