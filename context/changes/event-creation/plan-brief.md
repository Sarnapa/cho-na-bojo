# Event Creation — Plan Brief

> Full plan: `context/changes/event-creation/plan.md`

## What & Why

Implement roadmap slice S-03 so a logged-in organizer can create a titled event at a selected venue, choose a supported sport, set local start/end time, capacity, and auto-accept, and immediately inspect the saved event. This creates the first durable domain write that S-04 event discovery and the later approval/contact flow can build on.

## Starting Point

The map already retains the selected venue and supported sports, and its venue sheet contains a disabled Create event button. Authentication, JWT user-id extraction, EF Core/PostgreSQL, shared contracts/validation, typed MAUI API results, venue caching, and protected-request token refresh already exist; event storage and UI do not.

## Desired End State

Creation produces exactly one event owned by the authenticated user, with device-local input converted to UTC and all core constraints enforced in both application and database layers. The app shows an explicit waiting state, recovers without losing the draft, and opens a safe read-only detail screen from the create response.

## Key Decisions Made

| Decision | Choice | Why |
| --- | --- | --- |
| Event content | Required title (max 100), optional description (max 1000) | Keeps events identifiable without making creation unnecessarily slow |
| Time input | Date, start, and end; overnight allowed up to 24 elapsed hours | Supports evening play while bounding accidental long events |
| Time zone | Device-local input/display, UTC persistence | Matches the user's device while storing unambiguous instants |
| DST handling | Reject invalid and ambiguous local wall times | Avoids silently selecting the wrong instant |
| Start rule | Now or future, with small server clock-skew tolerance | Supports spontaneous games without accepting materially past starts |
| Scheduling conflicts | Overlapping events allowed | Events are meetups, not exclusive venue reservations |
| Retry behavior | Persisted client request id; exact retry returns the same event | Prevents duplicates after a lost response |
| Stale venue/sport | Refresh catalog, flag invalid selection, preserve the draft | Recovers from reference changes without discarding user input |
| Completion UX | Open a read-only event detail screen | Gives immediate confirmation without implementing S-04 listing |
| Verification | Agent-run builds, EF checks, and API probes; human DB/UI gates | No automated test infrastructure is added in this slice |
| Responsiveness | Five-second normal-response target with explicit waiting state | Makes slow creation understandable without unsafe timeout retries |

## Scope

**In scope:** shared event DTOs/rules; `SportsEvent` schema and migration; authenticated replay-safe create endpoint; MAUI form; device-local/UTC conversion; stale-catalog recovery; response-backed detail screen; Android and Windows build preservation.

**Out of scope:** event listing/join requests; participants; contact reveal; notifications; edit/cancel/leave/remove; lifecycle status/background jobs; overlap prevention; GET detail/deep links; process restoration; automated test projects; iOS.

## Architecture / Approach

`Map venue sheet → CreateEventPage → POST /api/events → SportsEvents → CreatedEventResponse → EventDetailPage`

Shared Contracts and Validation define the wire and pure rules. The server derives organizer identity from JWT, enforces the venue/sport pair through a composite foreign key, and stores a per-organizer request id for replay safety. MAUI retains the draft/request snapshot across uncertain failures and renders detail directly from the successful response.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared contract and validation | Stable DTOs, policy limits, and error keys | Client/server rule drift |
| 2. Persistence and migration | Constrained `SportsEvents` table and indexes | Incorrect FK/check semantics |
| 3. Create API | Authenticated 201/200 replay-safe creation | Race-safe idempotency |
| 4. MAUI create flow | Form, UTC conversion, waiting and recovery states | DST and uncertain-response handling |
| 5. Detail handoff | Safe response-backed detail and correct Back behavior | Navigation leaving a submitted form in stack |

**Prerequisites:** S-02 complete; development Supabase `AppDb` and session-mode `AppDbMigrations` access; Android Google APIs emulator for human verification.

**Estimated effort:** Approximately 4-6 implementation sessions across five phases, including migration and emulator gates.

## Open Risks & Assumptions

- The device time zone is treated as authoritative; invalid and ambiguous DST inputs are rejected because no offset-choice UI is in scope.
- A normal create should complete within five seconds, but Railway/Supabase variance may trigger the longer-wait state without failing the request.
- Replay safety depends on one canonical normalization path for UTC timestamps; a reused request id returns the stored event without payload comparison.
- Detail is response-backed only; after process death, persisted event discovery waits for S-04.
- Database/API operational checks require the existing development secrets and Supabase access.

## Success Criteria (Summary)

- A logged-in user creates one valid event from a selected venue and sees complete device-local detail.
- Invalid data, stale references, uncertain retries, and token refresh never create duplicates or lose the recoverable draft.
- Organizer ownership, supported venue/sport pairing, capacity, duration, and idempotency are protected by the database and API.
- No event response or detail surface exposes user contact information.
