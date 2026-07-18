---
project: "ChoNaBojo"
version: 1
status: draft
created: 2026-06-13
updated: 2026-07-18
prd_version: 1
main_goal: speed
top_blocker: time
---

# Roadmap: ChoNaBojo

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline (server/, app/ChoNaBojoApp/, .github/workflows/, tech-stack.md, infrastructure.md).
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Recreational athletes want to play team sports in their neighborhood but can't gather enough friends to fill the roster. ChoNaBojo connects strangers around specific local sports venues — proximity, sport, and timing converge into a single "let's go play" action. The MVP is Android-only, scoped to a Warsaw-only venue database, delivered solo after-hours against a hard deadline of 2026-08-01.

## North star

**S-05: Organizer accepts a join request and both parties see contact info** — this is the slice where the matchmaking loop closes and the PRD's primary Success Criterion is proven against real users.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the core product hypothesis — placed as early as Prerequisites allow, because everything else only matters once this works. S-05 is not the first slice (it must build on S-01..S-04), but it is the first one that *closes the loop*: organizer → participant → acceptance → contact info.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| F-01 | data-layer-foundation | (foundation) Postgres (Supabase) wired up, EF Core / migrations configured, `Sports` lookup seeded with the predefined list, `Venues` table seeded with Warsaw data, and the `VenueSports` join populated | — | FR-003 (predefined sport list), Non-Goals §1 (Warsaw venue seed), NFR (privacy, perf), tech-stack `database: PostgreSQL` | done |
| F-02 | auth-scaffold | (foundation) Email+password register/login on the API, password hashing, JWT issue+validate, authorization middleware on protected routes | F-01 | FR-001, FR-002, NFR (privacy boundary), Access Control | done |
| S-01 | account-and-session | register an account with email, password, and at least one contact; log in and stay logged in across app restarts | F-02 | FR-001, FR-002, FR-011 (contact collection), US-01, US-02 | proposed |
| S-02 | map-venue-discovery | open a map centered on their location (with manual-address fallback), see sports venues, and optionally filter them by discipline | S-01 | FR-003, FR-004, NFR (map < 2s), US-01 | proposed |
| S-03 | event-creation | create an event at a selected venue with date, estimated end time, participant limit (≥ 2, ≤ 300), and optional auto-accept | S-02 | FR-005, US-02 | proposed |
| S-04 | event-listing-and-join-request | view the available events at a selected venue (with fill state, not past end time), optionally filter by time availability, and send a join request | S-03 | FR-006, FR-007, US-01 | proposed |
| S-05 | approval-and-contact-reveal | (as organizer) accept or reject a join request; on acceptance both parties see each other's contact info — **north star** | S-04 | FR-009, FR-011, US-01, US-02 | proposed |
| S-06 | push-notifications | receive a push notification when someone requests to join my event, and when my own request is accepted or rejected | S-05 | FR-008, FR-010, NFR (push < 30s) | proposed |
| S-07 | event-lifecycle-ops | cancel my own event (participants get a push), remove a participant (participant gets a push), leave an event as a participant (organizer gets a push); past-end events are auto-closed | S-06 | FR-012, FR-013, FR-014 | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. The canonical ordering still lives in the dependency graph below.

| Stream | Theme | Chain | Note |
|---|---|---|---|
| A | Matchmaking loop (must-have path) | `F-01` → `F-02` → `S-01` → `S-02` → `S-03` → `S-04` → `S-05` | Strict must-have-path bias from `main_goal: speed`; each step adds one user-visible capability needed before S-05 can close the loop. |
| B | Around the loop (notifications + lifecycle) | `S-06` → `S-07` | Joins Stream A at `S-05`; these must-haves (push + lifecycle) build on a working loop, so they land after it, not inside it. |

## Baseline

What's already in the codebase as of `2026-06-13` (auto-researched + user-confirmed). Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend (MAUI Android):** **partial** — `app/ChoNaBojoApp/` scaffolded with `MauiProgram`, `AppShell`, `MainPage`, and a typed `IApiService`/`ApiService` HttpClient configured for dev (`http://10.0.2.2:5100`) and prod (`https://cho-na-bojo-production.up.railway.app`). No domain screens (map, auth, events).
- **Backend / API:** **partial** — `server/Program.cs` minimal API with `AddOpenApi`, `/health`, Railway `PORT` binding, `UseForwardedHeaders`, `UseHttpsRedirection`. No domain endpoints, no authorization middleware.
- **Data:** **absent** — no EF Core, no migrations, no Postgres drivers. `tech-stack.md` declares PostgreSQL; `infrastructure.md` points at Supabase as the external database.
- **Auth:** **absent** — no Identity, no JWT, no middleware. `tech-stack.md` flags `has_auth: true` (planned email+password).
- **Deploy / infra:** **present** — `server/railway.toml`, `.github/workflows/android-deploy.yml` and `windows-deploy.yml`, Railway target ratified in `infrastructure.md`. Production base URL already wired into MAUI in `MauiProgram.cs`.
- **Observability:** **absent** — default console/debug logging in MAUI, nothing in the API. No Sentry/AppInsights/OTel.

## Foundations

### F-01: Data layer — Supabase Postgres connection + migrations + seeded sports & Warsaw venues

- **Outcome:** (foundation) The backend has a working Postgres connection (Supabase per `infrastructure.md`), migration tooling (EF Core or Npgsql + Fluent Migrator) configured, and one initial migration that creates and seeds the reference data needed before any user-facing slice runs:
  - `Sports` — the predefined list of supported sport disciplines (FR-003), seeded once with: **football, basketball, volleyball, tennis, running, cycling, rollerblading, gym, street workout, swimming** (10 sports). Downstream code references rows by stable id/code, never by free-text label.
  - `Venues` — Warsaw venues seeded (manual seed per Non-Goals PRD §1).
  - `VenueSports` — many-to-many join populated from the seed so each venue declares which sports it supports (used by the S-02 filter and the S-03 sport picker at a venue).
- **Change ID:** data-layer-foundation
- **PRD refs:** FR-003 ("optionally filter venues and events by sport discipline from a predefined list"), Non-Goals §1 ("predefined venue database … manually uploaded … Warsaw"), NFR (privacy boundary requires durable storage), tech-stack `database: PostgreSQL`.
- **Unlocks:** S-02 (map needs `Venues` + `Sports` to render the filter UI), S-03 (event creation reads sports supported by the chosen venue), S-04 (event listing inherits the sport reference), F-02 (`Users` table sits on the same migration tooling), and every later S-NN. Also closes the unknowns "where do venues come from" and "what's the predefined sport list" — answer for both: seeded migrations, with the sport list locked at 10 entries above.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced first because nothing else can run without the DB; minimal enabler — only the three reference tables and the runtime connection. Domain tables (`Users`, `Events`, `JoinRequests`) ship in the slices that actually use them (progressive disclosure). The sport lookup is intentionally id-keyed so renaming a label later doesn't cascade through events/venues; the 10-sport list is also iterable post-seed via a follow-up migration if the MVP scope shifts. Real risk: configuring Supabase RLS / connection string / pooler on Railway requires touching three systems at once.
- **Status:** done

### F-02: Auth scaffold — User, password hash, JWT issue+validate, middleware

- **Outcome:** (foundation) The API has a `Users` table, `POST /auth/register` and `POST /auth/login` endpoints accepting email+password (plus at least one contact: phone/email/messenger), password hashing (BCrypt/Argon2), JWT issuance with appropriate TTL and refresh, and `[Authorize]` middleware protecting all domain routes. No UI here — UI lands in S-01.
- **Change ID:** auth-scaffold
- **PRD refs:** FR-001, FR-002, FR-011 (contact collected at registration), Access Control ("Login required (email and password for MVP)", "Unauthenticated user: no access"), NFR (privacy boundary).
- **Unlocks:** S-01 (the register/login UI calls these endpoints), S-02..S-07 (every protected route per Access Control), the per-event role check (organizer vs. participant) used in S-05.
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** "Invest deeply" area per Step 5 — the privacy boundary ("contact info never visible to unapproved users") is a launch gate; a slip here kills the product. Contract is minimal (issuer + validator + register/login), no fancy password reset (Parked) and no OAuth (out of MVP scope), so it doesn't bloat — but the per-event role check has to be designed solidly once, because S-05 and S-07 both rely on it.
- **Status:** done

## Slices

### S-01: Registration, login, and persistent in-app session

- **Outcome:** The user registers an account with email and password, providing at least one contact (phone, email, or messenger handle; user picks which to share), logs in, and the session survives an app restart (token stored in MAUI `SecureStorage`).
- **Change ID:** account-and-session
- **PRD refs:** FR-001, FR-002, FR-011 (the "collect contact at registration" part), US-01 (logged-in prereq), US-02 (logged-in prereq).
- **Prerequisites:** F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** First user-visible slice — if register/login doesn't work, nothing downstream can be validated. Session persistence (`SecureStorage`) has one Android 10+ gotcha (must be on `MainThread.IsMainThread == true` at startup), but it's a known path.
- **Status:** proposed

### S-02: Map of sports venues with discipline filter

- **Outcome:** A logged-in user opens the map screen centered on their location (with a manual-address fallback when permissions are denied), sees venues from the database within the current viewport, pans/zooms (the viewport is the proximity boundary per Business Logic), and optionally enables a sport filter populated from the `Sports` lookup — by default, all sports are visible.
- **Change ID:** map-venue-discovery
- **PRD refs:** FR-003, FR-004, NFR ("Map loads and responds to pan/zoom within 2 seconds"), US-01 (the "open the map, tap a nearby venue" part), Business Logic.
- **Prerequisites:** S-01, the seeded `Venues` / `Sports` / `VenueSports` tables from F-01.
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Which map provider for MAUI Android (Microsoft.Maui.Controls.Maps uses Google Maps on Android and requires an API key; Mapbox.Maui requires a separate account) — Owner: user. Block: no (the decision lands in `/10x-plan map-venue-discovery` and doesn't block the sequence).
- **Risk:** NFR map < 2s on a mid-range Android + marker rendering for many venues is the typical bottleneck; the answer is a viewport-bounded query on the API (capped N markers). The discipline filter is optional → a minimal addition to the query.
- **Status:** proposed

### S-03: Creating an event at a selected venue

- **Outcome:** A logged-in user opens "Create event" from the map/venue list: picks a sport from those that the chosen venue supports (read from `VenueSports`), date, estimated end time, participant limit (≥ 2, ≤ 300 per Polish gathering rules), and optionally toggles auto-accept; the event is saved (with a foreign key to `Sports`) and visible at the venue.
- **Change ID:** event-creation
- **PRD refs:** FR-005, FR-003 (event inherits a sport from the predefined list), US-02 (the "create an event" part).
- **Prerequisites:** S-02 (picker via the map).
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** First domain area with business constraints (min 2, max 300, auto-accept as an organizer preference). Validation must live on the API, not just in the UI; the validation contract will be reused by S-04 (read) and S-05 (approve).
- **Status:** proposed

### S-04: Event listing at a venue + availability filter + send a join request

- **Outcome:** The user picks a venue on the map and sees a list of current/upcoming events (only non-expired per FR-007 — past estimated end times are hidden on read), with a fill counter (taken vs. limit), optionally filtered by time availability; they can send a request to join a chosen event.
- **Change ID:** event-listing-and-join-request
- **PRD refs:** FR-006, FR-007, US-01 (the "request to join" part), Business Logic ("not past end time, spots open"), Success Criteria (Guardrail: "event data not accessible to unauthenticated users" — enforces `[Authorize]`).
- **Prerequisites:** S-03 (events must exist).
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** FR-007 enforcement on read is sufficient for the MVP (the auto-close background job arrives in S-07 and only tidies DB state). Race: two users send a request for the last spot — under auto-accept, one of them should get "no spots". That's resolved in S-05 (transactional accept); the request itself does not allocate a slot.
- **Status:** proposed

### S-05: Accepting a join request and revealing contact info — the loop closes (north star)

- **Outcome:** The organizer sees a list of pending join requests for their event and can accept or reject each one; on acceptance, both parties (organizer + participant) see each other's contacts (the ones the other side opted to share in S-01). In this slice, notification is in-app indicator/pull (push arrives in S-06 — does not block hypothesis validation).
- **Change ID:** approval-and-contact-reveal
- **PRD refs:** FR-009, FR-011 (the "reveal on acceptance" part), US-01 (closes), US-02 (closes), Success Criteria ("at least one other user joins and gets accepted — proving the matchmaking loop works"), Guardrail ("contact info only revealed after explicit organizer acceptance").
- **Prerequisites:** S-04
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** **This is the highest privacy-risk slice** — a single authorization slip (e.g., an endpoint returning contact info to anyone other than the accepted participant or organizer) breaks the PRD's main guardrail. The per-event role check from F-02 has to be hard-enforced here. Auto-accept (from S-03) flows through the same acceptance path, just without organizer interaction.
- **Status:** proposed

### S-06: Push notifications around the matchmaking loop

- **Outcome:** The organizer receives a push within 30s when someone requests to join their event (FR-008); the participant receives a push within 30s when the organizer accepts or rejects their request (FR-010). The slice covers FCM setup (device-token registration as an extension of the S-01-style flow, send service from the API).
- **Change ID:** push-notifications
- **PRD refs:** FR-008, FR-010, NFR ("push notifications arrive within 30 seconds").
- **Prerequisites:** S-05 (both join request and accept/reject events must already exist as triggers).
- **Parallel with:** S-07 (both depend on S-05 and have no dependency between each other — they can be delivered in parallel if capacity allows).
- **Blockers:** —
- **Unknowns:**
  - Whether the FCM Server Key / Service Account JSON is configured in the Firebase Console + Railway env vars — Owner: user. Block: no (out of skill scope; lands in `/10x-plan push-notifications`).
- **Risk:** FCM is a standard path, but device-token registration on MAUI Android requires `Plugin.Firebase` or direct `Firebase.Messaging` under `Platforms/Android/`. Validating the NFR (push < 30s) requires external measurement — don't trust a "feels fast" gut check.
- **Status:** proposed

### S-07: Event lifecycle — cancel, remove participant, leave event, auto-close

- **Outcome:** The organizer can cancel their event (all participants get a push per FR-012); the organizer can remove an accepted participant (the participant gets a push per FR-013); a participant can leave an event after acceptance (the organizer gets a push per FR-014); a background job (per `tech-stack.md has_background_jobs: true`) closes events whose estimated end time has passed — tidies state, even though FR-007 enforcement already runs on read from S-04.
- **Change ID:** event-lifecycle-ops
- **PRD refs:** FR-012, FR-013, FR-014.
- **Prerequisites:** S-06 (push notifications are the channel for all three actions).
- **Parallel with:** S-06 (see S-06 Parallel with).
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Three actions × event/participant states (pending/accepted/rejected/removed/left) — easy to make inconsistent. Needs a single, simple state machine on the API. The background job needs `IHostedService` on Railway — per `infrastructure.md` Railway supports this natively, so no architectural workaround.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| F-01 | data-layer-foundation | Foundation: Supabase Postgres + migrations + seeded sports & Warsaw venues | yes | Done — implementation review complete (`context/changes/data-layer-foundation/reviews/impl-review.md`, verdict APPROVED); issue #1 closed |
| F-02 | auth-scaffold | Foundation: Email+password auth scaffold (User, JWT issuer/validator, middleware) | yes | Ready — F-01 done; ready for `/10x-new` → `/10x-plan` |
| S-01 | account-and-session | User can register, log in, and stay logged in across app restarts | no | Waiting on F-02 |
| S-02 | map-venue-discovery | User can browse a map of nearby venues with optional sport filter | no | Waiting on S-01 |
| S-03 | event-creation | Organizer can create an event at a selected venue | no | Waiting on S-02 |
| S-04 | event-listing-and-join-request | User can view venue events with availability filter and request to join | no | Waiting on S-03 |
| S-05 | approval-and-contact-reveal | Organizer accepts/rejects a join request; contact info revealed on accept (north star) | no | Waiting on S-04 — north star |
| S-06 | push-notifications | Push notifications across the join → accept/reject loop | no | Waiting on S-05 |
| S-07 | event-lifecycle-ops | Cancel event, remove participant, leave event, auto-close past events | no | Waiting on S-06 |

## Open Roadmap Questions

(The PRD had no Open Questions, and the framing interview did not surface any cross-cutting items. Per-slice unknowns — map provider in S-02, FCM credentials in S-06 — are local and resolved inside `/10x-plan`.)

— none —

## Parked

- **iOS support** — Why parked: PRD §Non-Goals — single-platform focus halves the dev/test scope.
- **User-contributed venues / venues outside Warsaw** — Why parked: PRD §Non-Goals — venue curation is a separate product concern; the database is predefined and Warsaw-only in the MVP.
- **Inviting specific users to an event** — Why parked: PRD §Non-Goals — the product's value is meeting new people, not coordinating with an existing social graph.
- **User rating / reputation system** — Why parked: PRD §Non-Goals — significant complexity; deferred until post-MVP scale.
- **In-app messaging** — Why parked: PRD §Non-Goals — building a messenger is a product unto itself; contact reveal is enough for the MVP.
- **Password reset / account recovery** — Why parked: derived from Step 5 (`main_goal: speed`) — F-02 keeps the auth contract minimal; password reset does not block matchmaking-loop validation.
- **OAuth / passwordless login** — Why parked: shape-notes scope-down decision ("auth simplified to email+password only").
- **Full telemetry / Sentry / structured logging** — Why parked: derived from Step 5 (`main_goal: speed` + observability `absent` in baseline) — default console+debug logging is enough for MVP validation; full observability waits for a production signal.
- **Capacity warnings / rating system / reporting / specific-user invites** — Why parked: shape-notes §"Forward: scale features" — these are post-MVP features for 100x scale.

## Done

(Empty on first generation. `/10x-archive` will append an entry here — and flip the matching item's `Status` to `done` — when a change whose `Change ID` matches the item is archived.)

- **F-01: (foundation) The backend has a working Postgres connection (Supabase per `infrastructure.md`), migration tooling (EF Core or Npgsql + Fluent Migrator) configured, and one initial migration that creates and seeds the reference data needed before any user-facing slice runs:** — Archived 2026-07-14 → `context/archive/2026-07-12-data-layer-foundation/`. Lesson: —.
- **F-02: (foundation) The API has a `Users` table, `POST /auth/register` and `POST /auth/login` endpoints accepting email+password (plus at least one contact: phone/email/messenger), password hashing (BCrypt/Argon2), JWT issuance with appropriate TTL and refresh, and `[Authorize]` middleware protecting all domain routes. No UI here — UI lands in S-01.** — Archived 2026-07-18 → `context/archive/2026-07-15-auth-scaffold/`. Lesson: —.
