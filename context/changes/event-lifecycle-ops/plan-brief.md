# Event Lifecycle Ops (S-07) — Plan Brief

> Full plan: `context/changes/event-lifecycle-ops/plan.md`

## What & Why

Roadmap slice S-07 gives events an ending. Today an event can be created and joined but never withdrawn, never exited and never finished — so an organizer whose plans change has no way to tell the people who showed up, and a participant who drops out silently occupies a slot nobody else can take. This slice adds cancel (FR-012), remove participant (FR-013), leave event (FR-014), each with a push, plus a background job that marks finished events closed.

## Starting Point

The transactional core is in good shape and the lifecycle concept is entirely absent. `TransitionJoinRequestAsync` (`server/Events/EventEndpoints.cs:642-860`) already does the hard part — `FOR UPDATE` event lock, ownership check inside the query, mutation and push-outbox insert in one commit — and the S-06 outbox/worker pipeline delivers durably. But `SportsEvent` has **no state column at all**, `EventJoinRequestStatus` stops at `Rejected=3` behind a `CHECK IN (1,2,3)`, `PushNotificationType` likewise stops at 3, and the outbox has only ever addressed one recipient per row. There are no cancel, remove or leave endpoints, and no test projects anywhere in the repository.

## Desired End State

An organizer taps **Cancel event**, confirms, and everyone who had asked to join or been accepted is notified within 30 seconds; the event drops out of the venue listing, moves into a *Past & cancelled* section on everyone's My events, and stops serving contact details. An organizer can **Remove** an accepted participant, freeing the slot and revoking contact access both ways. A participant can **Leave** — and, unlike someone who was removed, may ask to join again later. In the background, events past their estimated end time quietly become `Closed`.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Event state | `EventStatus` enum column (`Active=1, Cancelled=2, Closed=3`) + DB CHECK | One column expresses both cancellation and completion, and the two-layer guard follows the `lessons.md` enum rule. | Plan |
| Departure states | `Left=4` and `Removed=5`, CHECK widened to `IN (1,2,3,4,5)` | Who ended the participation determines whether it can resume, so the reason must be stored, not inferred. | Plan |
| Cancel cascade state | **Derived:** a third new status `Cancelled=6` | "Terminated by cancellation" is neither leaving nor being removed; a dedicated value keeps every request row self-describing without joining event status. | Plan |
| Re-request rights | `Left` may re-request (row revives to `Pending`); `Removed` may not | Leaving is a change of mind, removal is an organizer's judgement — the unique `(EventId, UserId)` index means the same row is reused either way. | Plan |
| Cancel push fan-out | N outbox rows in the same transaction, each keyed to that recipient's own join-request row | Every recipient already owns a join-request row, so the non-nullable FK is satisfied with no schema loosening and no new fan-out stage. | Plan |
| Notification types | Three new types; auto-close is **silent** | FR-012/013/014 mandate pushes for user-initiated actions only; notifying every participant of every finished event is noise. | Plan |
| Pending requests on cancel | Terminated and notified alongside accepted participants | Someone waiting on an answer needs to know the answer is now "never" just as much as an accepted participant does. | Plan |
| Contact visibility | Revoked immediately on leave, removal and cancellation | The PRD's hard guardrail ties contact access to *current* accepted participation, not to having once held it. | Plan |
| Route shape | Action-verb POSTs (`/cancel`, `/remove`, `/join-requests/mine/leave`) | Matches the existing `/accept` and `/reject` convention rather than introducing a second REST dialect. | Plan |
| Timing gate | Allowed until `EstimatedEndsAtUtc`; blocked once cancelled or closed | Same boundary FR-007 already uses for joining, so there is one time rule in the system, not two. | Plan |
| Auto-close mechanics | Hosted service running one idempotent set-based `UPDATE` every 60s | A status flip has no external side effect, so the push worker's `SKIP LOCKED` + lease machinery buys nothing. | Plan |
| UI placement | All three actions on `MyEventsPage`, reusing `ConfirmDialog` | Organizer management already lives there; a new page would fragment the flow for three buttons. | Plan |
| Past events | Separate collapsed *Past & cancelled* section with status badges | A participant must be able to tell "cancelled" from "still on" at a glance without the list being cluttered. | Plan |
| Verification | Manual only — no test project introduced | Consistent with every prior slice and the remaining after-hours budget. | Plan |
| Cut line | Auto-close is the last phase and the first cut | The read paths already enforce the time boundary, so losing it costs tidiness, not correctness. | Plan |

## Scope

**In scope:** event status model and migration; three new join-request terminal states; read-path gating across venue listing, join, contacts and My events; three new push types with per-recipient fan-out; cancel / remove / leave endpoints; re-request-after-leave revival; client actions, badges and the past-events section; auto-close hosted service.

**Out of scope:** editing an existing event; un-cancelling; push on auto-close; organizer hand-over; any test infrastructure; bulk or admin cancellation tooling; data retention or purge; in-app notification history; per-event deep links; iOS; RF-1 (the deferred Uranium UI date/time migration).

## Architecture / Approach

```
cancel ─┐
remove ─┼─► FOR UPDATE lock on SportsEvents row
leave  ─┘        │
                 ├─ read roster (before rewrite)
                 ├─ rewrite EventJoinRequest.Status  ──┐
                 ├─ set SportsEvent.Status            ─┤ one commit
                 └─ insert 1..N PushOutbox rows       ─┘
                                  │
                    existing S-06 worker ──► FCM ──► device ──► My events refetch

auto-close worker (60s) ──► UPDATE ... SET Status=Closed
                            WHERE Status=Active AND EstimatedEndsAtUtc <= now
```

Two invariants carry the design: a row never needs a join to another table to know whether it is live, and every mutation takes the same event lock so a removal can never race an acceptance past the participant limit.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. State model | Both enums, CHECK constraints, migration, every read path gated | Widening a CHECK on a live table; the event/request status pair must be gated everywhere at once |
| 2. Push types & intents | Types 4–6, widened outbox CHECK, per-recipient intent factory | A payload body that leaks a name or title breaks the S-06 privacy rule |
| 3. Cancel endpoint | `POST /events/{id}/cancel` with roster cascade and fan-out | Roster must be read before the rewrite, or the fan-out notifies nobody |
| 4. Remove & leave | Two endpoints over one shared transactional helper | Reviving a `Left` row to `Pending` violates the `UpdatedUtc` CHECK unless reset to NULL |
| 5. Client — organizer | Cancel and remove on `MyEventsPage` with confirmations | Conflict reconciliation; double-tap issuing two mutations |
| 6. Client — participant | Leave, status badges, *Past & cancelled* section | "Finished" must derive from time too, since Phase 7 may not exist |
| 7. Auto-close worker | Idempotent 60s sweep + registration | Must never overwrite a `Cancelled` event — the `WHERE Status=Active` clause is load-bearing |

**Prerequisites:** S-06 complete (done); a dev database reachable for `dotnet ef database update`; a physical Android device plus a second device or emulator, and two accounts, for the notification matrix.

**Estimated effort:** ~7 sessions, one per phase. Phases 1 and 2 are small plumbing; 3, 4 and 6 are the largest. Phase 7 is independent of 3–6 and can be dropped entirely without affecting correctness.

## Open Risks & Assumptions

- **The derived `Cancelled=6` status is the one decision not put to you directly.** It adds a third enum value beyond the two agreed in questioning. The alternative — reusing `Rejected` for cascaded rows — is cheaper but semantically wrong and would wrongly mark people as declined. Flag it now if you disagree; it is cheapest to change before Phase 1.
- Adding `Status` to `OrganizedEventResponse` / `RequestedEventResponse` means the API must ship before the app build that reads it. An out-of-order deploy leaves the client unable to badge anything.
- Concurrency correctness (remove racing accept) rests on manual two-session testing, since there is no test project; a locking mistake would otherwise surface only in production.
- The 300-participant cancellation fan-out is bounded but untested at the limit — worth one deliberate test near the cap.
- Auto-close being cut is planned for, but if it is cut, `Status = Closed` never appears in production and the client's time-derived "Finished" becomes the only signal.

## Success Criteria (Summary)

- An organizer can cancel an event and every pending and accepted person is told within 30 seconds, with the event gone from the join surface and its contacts no longer served.
- An organizer can remove a participant and a participant can leave, each notifying the other side, freeing the slot, and revoking contact access — with someone who left able to return and someone removed not.
- No lifecycle notification ever carries a name, contact detail or event title, and a finished event reads as finished on My events whether or not the auto-close worker is running.
