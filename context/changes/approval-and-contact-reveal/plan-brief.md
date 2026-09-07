# Approval and Contact Reveal (S-05) — Plan Brief

> Full plan: `context/changes/approval-and-contact-reveal/plan.md`

## What & Why

The organizer accepts or rejects join requests, and on acceptance both parties see each other's contact details. This is the roadmap's **north star** — the first slice where the matchmaking loop actually closes and the PRD's primary success criterion ("a user creates an event and at least one other user joins and gets accepted") becomes provable. It is also the highest privacy-risk slice: one authorization slip breaks the product's main guardrail.

## Starting Point

S-04 shipped join requests, but they only ever land as `Pending` — there is no accept path, and `SportsEvent.AutoAccept` (persisted in S-03, rendered in the UI) does nothing. `User` carries four contact columns behind a "at least one method" DB constraint, but **no endpoint reads them**. The app has exactly one Shell tab (MapPage), so an organizer has no way to find requests, and push doesn't arrive until S-06. Capacity is checked without a lock; the roadmap explicitly deferred the last-spot race to this slice.

## Desired End State

A "My events" tab lists the events you organize (with a pending-request badge) and the events you've requested. The organizer opens an event, accepts or rejects each request, and acceptance transactionally claims a participant slot. Accepted pairs — and only those pairs — see each other's phone, email, and messenger handle through a single dedicated reveal endpoint. Auto-accept events skip the organizer step entirely and reveal contacts on request.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Organizer inbox | New "My events" Shell tab | Without push, a pull surface the organizer can find without re-navigating the map is the only viable channel. |
| Participant surface | "My requests" section in the same tab | One new navigation concept instead of two, and contacts stay reachable outside a venue modal. |
| Reveal scope | Pairwise (organizer ↔ each accepted participant) | Exactly PRD FR-011; a full roster would let one accepted user harvest every participant's contacts. |
| Auto-accept | Resolved inline at request time, same transaction as the slot claim | One transactional claim path shared with manual accept — two capacity code paths is the defect this prevents. |
| Last-spot race | Transaction + `FOR UPDATE` row lock on the event, then recount | Correct under Postgres with no schema change and no retry loop; contention is nil at MVP scale. |
| Pending queue when full | Left `Pending`; further accepts return `event_full` | No destructive churn, and the queue survives for when S-07 adds "leave event". |
| Re-request after rejection | Final for this event | The only anti-harassment mechanism in an MVP with no blocking; a time-gated re-submission option is noted as future work, not built. |
| Failure shape | 404 for "not found **or** not yours", 409 for state conflicts — both `EventConflictResponse` | A non-organizer cannot probe request IDs for existence, and reusing one DTO satisfies the one-shape-per-status rule with no new type. |
| Contact transport | Per-event reveal endpoint `GET /api/events/{id}/contacts` | One authorization choke point *and* one round trip; contacts stay out of every list payload, so a careless future projection can't leak them. |
| Contact DTO | Nullable-field record mirroring the `User` opt-in columns | Reuses the existing `CommunicatorPlatform` enum, adding no new enum that would need the both-layers guard. |
| `Title`/`Description` leak | Closed here — contact-pattern guard in shared validation | Incoherent to ship acceptance-gated reveal while the same feature keeps an unguarded self-publish path; `lessons.md` names this slice as owner. |
| Testing | No test projects; agent-automatable HTTP/build checks + manual | Per the project's course concept — unit tests arrive in a later module. |

## Scope

**In scope:** accept/reject endpoints with transactional slot claim; auto-accept resolution; my-events + request-queue read model; per-event contact reveal endpoint; "My events" Shell tab with organizer actions; locked/revealed contact component; free-text contact guard on event creation.

**Out of scope:** push notifications (S-06); cancel event / remove participant / leave event / auto-close job (S-07); re-request after rejection; auto-rejecting the queue when full; full-roster contact sharing; any new request status; test projects; in-app messaging, ratings, reporting, blocking.

## Architecture / Approach

Server-side, a single private slot-claim helper owns every transition to `Accepted`: it opens a transaction, takes a `FOR UPDATE` lock on the `SportsEvents` row, recounts accepted requests under the lock, and writes `Status` + `UpdatedUtc`. Both the organizer's accept endpoint and the auto-accept branch of the existing join-request endpoint call it — there is no second capacity check anywhere. Contact disclosure is concentrated in one route that derives the caller's relationship to the event from the JWT subject in the same query that fetches the contacts; everyone else gets a 404 indistinguishable from a nonexistent event. Client-side, the new tab follows the established immutable-view-data + typed-`*Result` patterns, and contact values bind exclusively through a view-model gate flag that is false unless the request is `Accepted`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Free-text contact guard | Contact patterns rejected in event title/description | False positives on legitimate text ("court #2", "18:00-19:30") |
| 2. Server: accept/reject + auto-accept | Working state machine, race-free slot claim, live `AutoAccept` | Getting the lock/recount ordering wrong leaves the last-spot race open |
| 3. Server: read model + contact reveal | My-events lists, request queue, the one reveal endpoint | An authorization slip here is the slice's worst-case failure |
| 4. Client: "My events" tab + accept/reject | Second Shell tab, organizer approval loop end to end | Adding a tab bar may disturb the map's bottom-sheet/modal lifecycle |
| 5. Client: contact reveal | Locked/revealed component, brand icons, tap actions | Binding a raw contact field anywhere bypasses the gate |

**Prerequisites:** S-04 done (join requests exist); local Supabase dev database reachable for `dotnet ef database update`; six seeded test accounts for the automated authorization matrix (using a dedicated event with participant limit ≥ 3), with at least three differing contact-method combinations; an Android emulator plus a second head (device or Windows) to exercise two accounts simultaneously.

**Estimated effort:** ~5 sessions, one per phase; Phases 3 and 4 are the largest.

## Open Risks & Assumptions

- **Requester identity has no good display value.** Registration collects no name, so the organizer's queue must label requesters without leaking an ungated contact channel; the plan uses a server-derived non-contact ordinal, which is functional but impersonal.
- **The contact-pattern guard is heuristic.** Narrow patterns keep false positives low but a determined organizer can still evade it (spelled-out digits, spacing tricks). It raises the floor; it is not a proof.
- **Adding a second `ShellContent` changes navigation from single-content to tabbed**, and `MapPage`/`VenueEventsPage` currently assume the former — the modal push/pop lifecycle needs re-verification, not just a visual check.
- **No automated regression net.** With no test projects, the Phase 3 authorization matrix is enforced only by the HTTP checks run during that phase; a later slice could silently widen the reveal.
- **Assumed:** `FOR UPDATE` behaves normally through the Supabase transaction-mode pooler (port 6543) for a single-statement lock inside an explicit transaction.

## Success Criteria (Summary)

- Two real accounts complete request → accept → mutual contact reveal, end to end, on device.
- A third uninvolved account — and a pending or rejected requester — can reach neither party's contacts through any endpoint.
- An auto-accept event admits a requester and reveals contacts with no organizer interaction, and an event at its limit never exceeds it.
