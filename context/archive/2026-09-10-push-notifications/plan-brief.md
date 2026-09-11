# Push Notifications (S-06) — Plan Brief

> Full plan: `context/changes/push-notifications/plan.md`
> Research: `context/changes/push-notifications/research.md`
> Upstream API notes: `context/changes/push-notifications/xamarin-firebase-messaging-context7.md`

## What & Why

Roadmap slice S-06 closes the feedback loop on matchmaking: an organizer learns within 30 seconds that someone wants to join their event (FR-008), and a requester learns within 30 seconds that they were accepted or rejected (FR-010). Without push, both sides must poll the app to discover state changes, which makes coordinating a game around a specific time slot unreliable — the very thing the product exists to solve.

## Starting Point

The domain triggers already exist and are unusually well-placed: join, accept, reject, and auto-accept all converge on one transactional method, `TransitionJoinRequestAsync` (`server/Events/EventEndpoints.cs:641-810`), which already holds a row lock and commits once. Nothing push-related exists anywhere else — no Firebase dependency on either side, no notification permission, and no `MainActivity` intent handling.

## Desired End State

An organizer with the app installed gets a heads-up notification within 30 seconds of a join request; a requester gets one on accept or reject. Tapping any notification opens the app on **My events**, refreshed from the server. The notification body never contains a name, contact detail, or event title. Logging out stops that device receiving the previous account's notifications, and a user with two devices is notified on both.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Client SDK | `Xamarin.Firebase.Messaging` 125.1.1.1, direct binding | Android-only MVP gets no value from a cross-platform plugin that still wraps the deprecated token lifecycle. | Research |
| Registration API | FID-first with legacy-token fallback behind one abstraction | The binding's FID surface is unverified, so the destination stays an opaque `DeviceRegistrationId` everywhere except the gateway — a fallback is a one-line change. | Plan |
| Installation identity | One row per app installation, not a field on `User` | A user can hold several devices, and a device can be reassigned between accounts. | Research |
| Delivery mechanism | Transactional outbox + hosted worker | Sending inside the HTTP transaction risks notifying about rolled-back work; sending after commit without durability loses notifications on process exit. | Research |
| Payload | Notification + data, generic text and opaque ids only | A push must be a signal to refetch, never a transport for protected state — this is what keeps push outside the contact-reveal boundary. | Research |
| Firebase environments | One project for the MVP | Solo developer, 3-week after-hours budget, no staging environment exists today. | Plan |
| `google-services.json` | Committed to the repo | Client config, not a credential; keeps fresh clones building with no CI decode step. | Plan |
| Auto-accept policy | Organizer-only push | The requester already received `Accepted` synchronously in the HTTP response. | Plan |
| Retry budget | TTL 30 min, max 5 attempts, then dead-letter | Survives a transient FCM outage while preventing an hour-old "you were accepted" arriving after the event. | Plan |
| Logout unlink | Carried on the existing `POST /auth/logout` call, keyed on the registration id | That call already authenticates with the refresh token, so no access token, extra HTTP client, or `DELETE` endpoint is needed; the two `revokeServer: false` sign-out paths have no credential at all and are knowingly out of scope. | Plan |
| `minSdk` | Raised 21 → 29 in this slice | Aligns packaging with the PRD's Android 10+ claim and removes compatibility branches that would otherwise be written now and deleted later. | Plan |

## Scope

**In scope:** three notification types (join requested, accepted, rejected); installation registration/unlink API and lifecycle; transactional outbox with per-installation delivery, backoff and dead-lettering; Firebase Admin gateway and hosted worker; Android channel, permission, foreground handling, dedup and tap routing; Railway configuration.

**Out of scope:** S-07 lifecycle notifications (cancel, remove, leave); in-app notification history; deep-linking to a specific event detail page; iOS; a second Firebase project; Postgres-backed or device-level automated tests; notification preferences; topic subscriptions; exactly-once delivery guarantees.

## Architecture / Approach

```
join/accept/reject  ──┐
                      │ (same EF transaction, same commit)
                      ▼
              PushOutbox row ──► worker claims (FOR UPDATE SKIP LOCKED, 2s poll)
                                        │
                                        ├─ snapshot recipient's active installations
                                        ▼
                                 PushDelivery rows ──► IPushGateway ──► FCM
                                        │                                 │
                                 backoff / dead-letter              Android device
                                 disable dead installs                    │
                                                                   notification →
                                                                   tap → My events
                                                                   → refetch from API
```

The outbox insert is the only push work inside the domain transaction; everything else runs in a separate loop. A Firebase outage degrades to "no notification" and can never fail or roll back a join, accept, or reject.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Firebase setup & binding spike | Firebase project, Android binding, manifest, `minSdk` 29, registration id logged on a device | The binding may not expose the FID API — this phase exists to find out before anything depends on it |
| 2. Installation persistence & API | `PushInstallations` table, `PUT /api/me/push-installations`, logout-time unlink on `POST /api/auth/logout` | Account-switch reassignment must be atomic or duplicate rows appear |
| 3. Client registration lifecycle | Registration upload after auth, account switching, registration id carried on logout | `SessionService` may not depend on `IApiService`; unlink rides the existing refresh-token logout call |
| 4. Transactional outbox | Outbox + delivery tables, intent written inside the existing transaction | Replay paths must not enqueue — four early-return branches to respect |
| 5. Gateway & delivery worker | Firebase Admin send, retry classification, backoff, dead-letter, stale-install cleanup | Retry policy is easy to get wrong in a way that only shows under real failures |
| 6. Android delivery & tap routing | Channel, permission prompt, foreground handling, dedup, cold/warm intent routing | Channel importance is immutable after first creation; cold-start routing must not precede session restore |
| 7. Deployment & SLO measurement | Railway sealed credentials, latency measurement, device matrix, docs | The 30s NFR needs real measurement, not a "feels fast" judgement |

**Prerequisites:** S-05 complete (done); a Google account able to create a Firebase project with FCM HTTP v1 enabled; a physical Android device or an emulator image with Google Play services; Railway access to set sealed variables.

**Estimated effort:** ~7 sessions, one per phase, with Phases 5 and 6 the largest. Phases 1–2 are independent; 3 needs 1 and 2 (change 2 edits `ChoNaBojoMessagingService.cs`, which Phase 1 creates); 4 is independent of 1–3; 5 needs 4; 6 needs 1 and 5; 7 needs everything.

## Open Risks & Assumptions

- The `Xamarin.Firebase.Messaging` binding may not generate the FID registration surface. Mitigated by making Phase 1 a spike and keeping the destination opaque everywhere except the gateway — but it remains the single riskiest unknown.
- Concurrency behaviour (`FOR UPDATE SKIP LOCKED` claiming, transactional atomicity) depends on the manual verification matrix, so a claiming bug could otherwise surface only in production.
- The 30-second SLO is only meaningful for online devices with permission granted and the channel enabled; offline, force-stopped, and Doze/OEM-restricted devices are excluded by definition and cannot be guaranteed.
- A single Firebase project means a local development send can reach a real user's device — mitigated by discipline, not by architecture.
- Railway's API service must stay continuously running; a sleeping or scale-to-zero service cannot meet the SLO.

## Success Criteria (Summary)

- An organizer is notified within 30 seconds of a join request, and a requester within 30 seconds of an accept or reject — measured, not estimated.
- No notification ever displays a name, contact detail, or event title, and tapping one lands on server-refreshed My events only after the session is restored.
- A logged-out device stops receiving the previous account's notifications, and a user with two devices is notified on both.
