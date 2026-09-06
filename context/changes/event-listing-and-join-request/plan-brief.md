# Event Listing and Join Request - Plan Brief

> Full plan: `context/changes/event-listing-and-join-request/plan.md`

## What & Why

Deliver roadmap slice S-04: from a selected venue, an authenticated user can discover non-expired events, narrow them by sport and availability, see fill state, and send a durable join request. This establishes the state S-05 needs to close the matchmaking loop without moving acceptance, auto-accept activation, notifications, or contact reveal into this slice.

## Starting Point

S-03 already stores events and supports authenticated creation from the map, but participant count is organizer-only and the venue sheet has no live event list. There is no join-request table, listing/join API, availability contract, or MAUI join state.

## Desired End State

The venue sheet displays ordered, contact-free event cards with complete loading, empty, error, full, ownership, and request states. A user can filter by overlapping availability and submit one idempotent request that remains Pending until S-05; stale server state is explained and refreshed.

## Key Decisions Made

| Decision | Choice | Why |
| --- | --- | --- |
| Availability matching | Any interval overlap | Includes every event the user could attend for part of the selected window |
| Full events | Visible, Join disabled | Preserves useful venue activity context without allowing an invalid action |
| Organizer-owned events | Visible as `Your event` | Keeps the list complete and makes ownership explicit |
| Auto-accept in S-04 | Persist Pending | Preserves S-05's single capacity-safe acceptance path |
| Duplicate submission | Return canonical request as success | Makes response-loss retry safe and prevents duplicate rows |
| Persisted status | Pending, Accepted, Rejected | Avoids a schema rewrite when S-05 adds approval |
| Filter UX | Presets plus custom range | Fast common choices with precise fallback |
| Stale join | Explain, then refresh | Reconciles the card with authoritative server state |
| Verification | Builds, EF checks, and API probes | Matches repository practice without adding first-time test infrastructure |
| Data model | One `EventJoinRequest` table | Accepted requests can become participant membership; no parallel table is needed |

## Scope

**In scope:**

- Protected event listing by venue, optional sport, and optional UTC availability window
- Non-expired filtering, accepted count, caller relationship state, and stable ordering
- Pending/accepted/rejected join-request persistence with database constraints and indexes
- Naturally idempotent join submission and typed stale-state conflicts
- Inline venue-sheet event cards, presets, custom range modal, refresh, and retry states

**Out of scope:**

- Approval/rejection actions, auto-accept activation, slot allocation, and contact reveal
- Push notifications and event lifecycle operations
- Pagination, event editing, event-detail fetch, iOS, dark theme, or new test projects

## Architecture / Approach

`MapPage` requests `GET /api/venues/{venueId}/events` through the typed API service. The server validates filters, queries indexed `SportsEvents`, counts accepted `EventJoinRequests`, and projects safe DTOs. `POST /api/events/{eventId}/join-requests` derives the requester from JWT claims, replays an existing event/requester row, or inserts one Pending row. The venue sheet owns list/filter state; a separate modal owns custom local time input and converts it safely to UTC.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared contracts | Safe DTOs, status enum, filters, and conflict codes | Contract leakage or ambiguous time bounds |
| 2. Persistence | Request table, checks, uniqueness, counts, and listing indexes | Schema choices must remain usable by S-05 |
| 3. APIs | Protected overlap-filtered listing and idempotent join | Dynamic state and duplicate races |
| 4. MAUI state | Typed calls, time conversion, cancellation, refresh, and join outcomes | Stale responses overwriting current venue state |
| 5. Venue-sheet UX | Accessible cards, filters, states, and Android flow | Bounded sheet layout: the sheet row must be star-sized and height-capped so the `CollectionView` measures correctly once the outer `ScrollView` is removed |

**Prerequisites:** S-03 is implemented and its `AddSportsEvents` migration is applied; development auth users and Supabase migration/runtime connections are available.
**Estimated effort:** Approximately 4-6 implementation sessions across five phases, plus manual Android and database gates.

## Open Risks & Assumptions

- Venue-level event volume remains small enough for an ordered, unpaginated MVP response; query plans are verified before acceptance.
- A rejected request is final for this MVP because `(EventId, RequesterUserId)` is unique; re-request policy can be added with an explicit S-05/S-07 state transition.
- Auto-accept events visibly remain Pending until S-05 activates the shared acceptance path.
- New custom-range controls use Uranium UI fields; the existing RF-1 create-form cleanup remains separate.
- The `Joined` and `Request rejected` card states are built forward-compatibly for S-05 but no S-04 code path produces them; they are verified by seeding `EventJoinRequests.Status` directly in Supabase.
- Phase 3 has no automated test harness; its behavioural criteria are a named probe matrix (P-01…P-07) run against a locally started API, or in Postman when the API cannot be started.

## Success Criteria (Summary)

- Authenticated users see correct non-expired venue events, fill state, caller state, and overlap-filter results without any contact data.
- A valid join creates exactly one Pending request, and duplicate or uncertain retries return that same request.
- Full/owned/stale states cannot create requests, and the Android venue sheet explains and refreshes changed server state.
