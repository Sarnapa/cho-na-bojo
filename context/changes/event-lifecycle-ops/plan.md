# Event Lifecycle Ops (S-07) Implementation Plan

## Overview

Roadmap slice S-07 adds the three lifecycle actions the matchmaking loop is missing, plus the background tidy-up that keeps event state honest:

- **FR-012** — an organizer cancels their event; every outstanding requester and accepted participant receives a push.
- **FR-013** — an organizer removes an accepted participant; that participant receives a push.
- **FR-014** — a participant leaves an event they were accepted into; the organizer receives a push.
- **Auto-close** — a hosted service flips events whose estimated end time has passed to a terminal `Closed` state, so "finished" is a persisted fact rather than a value recomputed on every read.

Until this slice lands, an event is immortal: it can be created and joined, but never withdrawn, never exited, and never finished.

## Current State Analysis

The domain has a well-shaped transactional core and no lifecycle concept at all.

**What exists and works:**

- `TransitionJoinRequestAsync` (`server/Events/EventEndpoints.cs:642-860`) is the single state machine for join, accept and reject. It opens a transaction, row-locks the event with a raw `SELECT 1 FROM "SportsEvents" WHERE "Id" = {eventId} FOR UPDATE` (`:659-661`), authorizes the organizer by matching `SportsEvent.OrganizerUserId == actorUserId` inside the query (`:719`), mutates the request, inserts the push outbox row, and commits once (`:797-818`). This is the template every new lifecycle action follows.
- The push pipeline is durable and decoupled: `PushOutboxItem` rows are written inside the domain transaction, `PushOutboxProcessor.ClaimItemAsync` (`server/Push/PushOutboxProcessor.cs:82-133`) claims them with `FOR UPDATE SKIP LOCKED` plus a 2-minute lease, and `PushDeliveryWorker` polls every 2 seconds (`server/Push/PushDeliveryWorker.cs:8`, registered `server/Program.cs:125`). Fan-out to a user's devices already happens in `LoadOrCreateDeliveriesAsync` (`:137-171`).
- `GET /events/{id}/contacts` (`server/Events/EventEndpoints.cs:514-589`) is explicitly the only endpoint permitted to read shareable contact columns, gated on `Status == Accepted`.
- `ConfirmDialog` + `IFeedbackService.ShowConfirmAsync` is the established destructive-confirmation path on the client, already used for reject (`app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs:676`).

**What is missing:**

- `SportsEvent` has **no state column of any kind** (`server/Data/ChoNaBojoContext.cs:213-292`). There is no cancelled, no closed, no soft delete.
- `EventJoinRequestStatus` is `Pending=1 / Accepted=2 / Rejected=3` only, guarded by `CK_EventJoinRequests_Status IN (1,2,3)` (`server/Data/ChoNaBojoContext.cs:299`). Nothing expresses "left" or "removed".
- `PushNotificationType` is `1..3`, guarded by `CK_PushOutbox_Type IN (1,2,3)`, and `PushPayloadFactory.Create` throws on any type outside that set (`server/Push/PushPayloadFactory.cs:43-54`).
- `PushIntentFactory.Create` only ever produces a single recipient and only understands join / accept / reject (`server/Push/PushIntentFactory.cs`).
- No cancel, remove or leave endpoint exists. No hosted service other than `PushDeliveryWorker`.
- There are **no test projects anywhere in the repository** — the five `.csproj` files are the app, the server, and the three shared libraries.

### Key Discoveries:

- The unique index `(SportsEventId, RequesterUserId)` on `EventJoinRequests` (`server/Data/ChoNaBojoContext.cs:314`) means one user holds at most one request row per event forever. Re-requesting after leaving therefore has to **reuse** the existing row, not insert a second one.
- `CK_EventJoinRequests_StatusUpdatedUtc` (`server/Data/ChoNaBojoContext.cs:302-311`) encodes `Pending ⇒ UpdatedUtc IS NULL, otherwise NOT NULL`. Flipping a `Left` row back to `Pending` must therefore **reset `UpdatedUtc` to NULL** or the write is rejected by the database.
- `PushOutboxItem.EventJoinRequestId` is non-nullable, but every cancellation recipient is the owner of a join-request row, so per-recipient outbox rows satisfy the FK with no schema loosening.
- The outbox `EventKey` format `join-request:{requestId}:{type}:{recipientUserId}` (`server/Push/PushIntentFactory.cs:91-95`) is unique across the new lifecycle notification types because type and recipient are part of the key. A revived `Left` request emits `JoinRequestCreated` for the same request, type and recipient, so that specific path needs a per-attempt discriminator.
- Capacity is computed as `1 + acceptedCount` against `ParticipantLimit` (`server/Events/EventEndpoints.cs:886-902`). Because `Left`, `Removed` and `Cancelled` are all simply "not `Accepted`", a departure frees a slot with no arithmetic change.
- `ParticipantLimit` caps accepted participants at 300 but does not cap pending requests. Cancellation fan-out is therefore linear in the full pending-plus-accepted roster and is not strictly bounded; adding an outstanding-request cap is deferred in `context/foundation/todo.md`.
- Railway runs the API as a long-lived container, so `IHostedService` works natively with no external scheduler (`context/foundation/infrastructure.md:17,38`).
- The database is never migrated automatically by the app; `dotnet ef database update` is a deliberate deployment step.

## Desired End State

An organizer opens **My events**, taps **Cancel event**, confirms, and the event moves into a collapsed *History* section marked `Cancelled`. Every person who had asked to join or been accepted receives a push within 30 seconds, and their copy of the event moves into the same section. Nobody can join it, and the contact details it had unlocked are no longer served by the API.

An organizer viewing their request queue can **Remove** an accepted participant; that participant is notified, their slot returns to the pool, and neither side can read the other's contact details any more. A participant viewing an event they were accepted into can **Leave**; the organizer is notified, and the participant may ask to join again later if they change their mind — someone who was *removed* may not.

Meanwhile a background loop quietly marks finished events `Closed`, so "this event is over" is a stored fact rather than a comparison repeated on every read.

**Verification of the end state:** on a physical Android device with two accounts, execute the full manual matrix in the Testing Strategy section — cancel with a mixed pending/accepted roster, remove, leave, re-request after leave, blocked re-request after removal, and the auto-close transition observed in the database.

## What We're NOT Doing

- **No editing an existing event.** Changing time, venue, limit or auto-accept is out of scope; cancel-and-recreate is the MVP answer.
- **No un-cancelling.** `Cancelled` and `Closed` are terminal; there is no restore path.
- **No push for auto-close.** FR-012/013/014 mandate notifications for the three user-initiated actions only. A silent close avoids waking every participant of every finished event.
- **No organizer hand-over.** An organizer cannot transfer their event to someone else before cancelling.
- **No test infrastructure.** Per the Q14 decision this slice keeps the repository's existing verification model (build, migration apply, scripted manual matrix). No xUnit project is introduced.
- **No bulk or admin tooling** for cancelling events, and no retention/purge job for old rows.
- **No outstanding-request cap.** Pending requests remain unlimited in this slice; the cancellation fan-out risk and a future locked-path cap are tracked in `context/foundation/todo.md`.
- **No in-app notification history** and no deep link to a specific event — push taps continue to route to My events, as S-06 established.
- **No change to the reject flow**, the contact-reveal endpoint's shape, or the venue map.
- **RF-1** (the Uranium UI date/time field migration in `context/foundation/review-fixes.md`) stays deferred; it is unrelated to lifecycle.

## Implementation Approach

Three principles drive the sequencing.

**1. The database is the authority, and every state is self-describing.** The roadmap flags "three actions × five states — easy to make inconsistent" as this slice's primary risk. The counter-measure is that a row never requires a join to another table to know whether it is live. `SportsEvent.Status` says whether the event is alive; `EventJoinRequest.Status` says whether that person's participation is alive. This is why cancellation rewrites its request rows to a dedicated `Cancelled` status instead of leaving them `Accepted` under a dead event — an `Accepted` row must always mean "this person is currently in this game".

**2. Every mutation reuses the proven transactional shape.** Lock the event row `FOR UPDATE`, re-read state under the lock, authorize by ownership inside the query, mutate, enqueue outbox rows, commit once. This is exactly what `TransitionJoinRequestAsync` does, and it is what makes the cancel fan-out atomic: either the event is cancelled and all N notifications are queued, or neither happened.

**3. Schema and read-path gating land before any new endpoint.** Phase 1 adds the states and immediately teaches every existing read path about them, so there is never an interval where a `Cancelled` value exists that the venue listing or contacts endpoint would ignore.

Phases 1–2 are schema and plumbing; 3–4 are the API; 5–6 are the client; 7 is the background job, deliberately last because it is the designated cut (the read paths already enforce FR-007 on time, so losing it costs tidiness, not correctness).

## Critical Implementation Details

**The `Pending ⇒ UpdatedUtc IS NULL` constraint bites on re-request.** `CK_EventJoinRequests_StatusUpdatedUtc` rejects a `Pending` row that carries an `UpdatedUtc`. When a `Left` row is flipped back to `Pending` by a re-request, `UpdatedUtc` must be set back to `NULL` in the same write, otherwise the transaction fails with a constraint violation that surfaces as a 500.

**`CreatedUtc` identifies the current request attempt.** Reviving a `Left` row resets `CreatedUtc` to the new attempt time and `UpdatedUtc` to `NULL`. The revived `JoinRequestCreated` outbox key includes that same `CreatedUtc.UtcTicks`, so a legitimate new attempt cannot collide with the original notification while duplicate retries still replay the now-`Pending` row without enqueuing again.

**Ordering inside the cancel transaction.** The accepted-and-pending roster must be read *after* the `FOR UPDATE` lock is taken and *before* the status rewrite, because the rewrite is what makes those rows no longer match "pending or accepted". Read the roster into memory first, then rewrite, then build outbox rows from the in-memory roster.

**The event lock is what makes remove and leave safe against a concurrent accept.** Without taking the same `FOR UPDATE` lock that `TransitionJoinRequestAsync` takes, a removal racing an acceptance can produce an accepted count above the limit. Every new mutation must acquire the same lock on the same table, or the existing capacity guarantee is silently weakened.

**The `Closed` status must not be the only thing gating time.** The auto-close loop lags by up to its poll interval, and it is the first thing cut under time pressure. Every read path keeps its `EstimatedEndsAtUtc > now` comparison in addition to the status check, and the client derives "finished" from `Status == Closed OR EstimatedEndsAtUtc <= now`.

## Phase 1: Event and Join-Request State Model

### Overview

Introduce both state machines, guard them at the application and database layers per the `lessons.md` enum rule, and teach every existing read path about them. No new endpoint in this phase — the schema exists and is respected before anything can write to it.

### Changes Required:

#### 1. Cross-boundary enums

**File**: `shared/ChoNaBojo.Contracts/Enums/EventStatus.cs` *(new)*

**Intent**: Define the event lifecycle states so both the API and the MAUI client share one vocabulary.

**Contract**: `public enum EventStatus { Active = 1, Cancelled = 2, Closed = 3 }`. Integer values are load-bearing — they are persisted and appear in the DB CHECK constraint.

**File**: `shared/ChoNaBojo.Contracts/Enums/EventJoinRequestStatus.cs`

**Intent**: Add the three terminal states a participation can reach after acceptance, keeping each one self-describing about *why* it ended.

**Contract**: append `Left = 4, Removed = 5, Cancelled = 6` to the existing `Pending = 1, Accepted = 2, Rejected = 3`. `Left` is participant-initiated and re-requestable; `Removed` is organizer-initiated and terminal; `Cancelled` is a consequence of the event dying. Do not renumber existing members.

#### 2. Event entity and configuration

**File**: `server/Data/Entities/SportsEvent.cs`

**Intent**: Persist the event's lifecycle state alongside the instant it last changed, mirroring how `EventJoinRequest` pairs `Status` with `UpdatedUtc`.

**Contract**: add `public EventStatus Status { get; set; }` and `public DateTime? StatusChangedUtc { get; set; }`. `StatusChangedUtc` is `NULL` exactly while `Status == Active`.

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Configure the new column, guard both enums at the database layer, and widen the join-request status constraint.

**Contract**: in the `SportsEvent` configuration (around `:213-292`) — `Status` stored as `integer` with a default of `1`; `StatusChangedUtc` as `timestamp with time zone`; a `CK_SportsEvents_Status` check restricting the column to `IN (1,2,3)`; and a `CK_SportsEvents_StatusChangedUtc` check mirroring the join-request pattern at `:302-311`, asserting `Status = 1 AND "StatusChangedUtc" IS NULL OR Status <> 1 AND "StatusChangedUtc" IS NOT NULL`. In the `EventJoinRequest` configuration, widen `CK_EventJoinRequests_Status` from `IN (1,2,3)` to `IN (1,2,3,4,5,6)`. Add an index on `(Status, EstimatedEndsAtUtc)` to serve the auto-close sweep in Phase 7.

#### 3. Migration

**File**: `server/Migrations/<timestamp>_AddEventLifecycleStates.cs` *(generated)*

**Intent**: Apply the schema delta, backfilling every existing event as `Active`.

**Contract**: generated with `dotnet ef migrations add AddEventLifecycleStates --project server`. Confirm the generated `AddColumn<int>` for `Status` carries `defaultValue: 1` so existing rows backfill without a separate `Sql()` statement, and that the widened join-request check is emitted as a drop-and-recreate of `CK_EventJoinRequests_Status`.

#### 4. Read-path gating

**Files**: `server/Events/EventEndpoints.cs`, `server/Push/PushIntentFactory.cs`

**Intent**: Make every existing read and write path respect the new states before any code can produce them.

**Contract**: four call sites.
- `GetVenueEventsAsync` (`:188`) — add `.Where(sportsEvent => sportsEvent.Status == EventStatus.Active)` alongside the existing `EstimatedEndsAtUtc > nowUtc` filter at `:222`. Cancelled and closed events leave the join surface entirely.
- `GetJoinabilityFailureUnderLockAsync` (`:886-902`) — return the new `event_cancelled` conflict when the locked event is `Cancelled`, and the existing `event_ended` when it is `Closed` or past its end time. This covers both the create-request and the accept path, since both route through it.
- `GetEventContactsAsync` (`:514`) — require `sportsEvent.Status == EventStatus.Active` for the entitlement to resolve. A cancelled event serves no contacts to anyone, which is the Q8 revocation decision. The existing `Status == Accepted` filters already exclude `Left`, `Removed` and `Cancelled` requests with no change.
- `GetMyEventsAsync` (`:369`) — project `Status` into both row shapes and pass it through to the response DTOs. This path deliberately does **not** filter, because My events is the personal history view.

#### 5. Response contracts

**Files**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs`, `app/ChoNaBojoApp/Services/ApiService.cs`

**Intent**: Carry event status to the client so it can group and badge events without inferring state from timestamps alone.

**Contract**: append `EventStatus EventStatus` to both `OrganizedEventResponse` and `RequestedEventResponse`. Use the same `EventStatus` member name in `OrganizedEventViewData` and `RequestedEventViewData`, reserving the existing `RequestedEventResponse.Status` and `RequestedEventViewData.Status` members for join-request lifecycle state. Append the response member as the last positional member so existing construction sites fail loudly at compile time rather than silently binding to the wrong argument. Extend `IsInvalidOrganizedEvent` and `IsInvalidRequestedEvent`, used by `ApiService.GetMyEventsAsync`, to reject `EventStatus` values for which `Enum.IsDefined` is false; this includes the zero produced when an older API omits the property. Invalid lifecycle values return the existing `MyEventsResult.Unknown()` outcome, so the view model shows its failure state rather than treating unknown data as active. `EventListItemResponse` is unchanged — the venue listing only ever contains active events now.

#### 6. Conflict codes

**File**: `shared/ChoNaBojo.Contracts/Consts/EventConflictCodes.cs`

**Intent**: Name the new failure modes with stable machine-readable codes, per the one-shape-per-status-code rule in `lessons.md`.

**Contract**: add `EventCancelled = "event_cancelled"` and `ParticipantNotAccepted = "participant_not_accepted"`. Both are returned inside the existing `EventConflictResponse` shape with HTTP 409. No new response shape is introduced anywhere in this slice.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Migration is generated with no model-vs-snapshot drift: `dotnet ef migrations add AddEventLifecycleStates --project server`
- Migration applies cleanly against the dev database: `dotnet ef database update --project server`
- EF reports no model/snapshot drift: `dotnet ef migrations has-pending-model-changes --project server`

#### Manual Verification:

- Inserting a `SportsEvents` row with `Status = 4` is rejected by `CK_SportsEvents_Status`
- Setting `Status = 2` while leaving `StatusChangedUtc` NULL is rejected by `CK_SportsEvents_StatusChangedUtc`
- Updating an `EventJoinRequests` row to `Status = 6` succeeds, and to `Status = 7` is rejected
- All pre-existing events read back as `Status = 1` with `StatusChangedUtc` NULL
- Venue listing, join, accept, reject and contacts all still behave exactly as before on active events
- A `/me/events` payload with missing, zero or unknown `EventStatus` maps to `MyEventsResult.Unknown()` and renders no lifecycle actions

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Lifecycle Push Types and Intents

### Overview

Extend the push pipeline to carry the three new notification types and to emit an intent per recipient, so Phase 3 can fan a single cancellation out to a whole roster.

### Changes Required:

#### 1. Notification type enum and constraint

**File**: `shared/ChoNaBojo.Contracts/Enums/PushNotificationType.cs`

**Intent**: Name the three new notifications.

**Contract**: append `EventCancelled = 4, ParticipantRemoved = 5, ParticipantLeft = 6`.

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Keep the database guard in step with the enum, per the `lessons.md` two-layer rule.

**Contract**: widen `CK_PushOutbox_Type` from `IN (1,2,3)` to `IN (1,2,3,4,5,6)`.

**File**: `server/Migrations/<timestamp>_AddLifecyclePushTypes.cs` *(generated)*

**Intent**: Apply the widened constraint as its own migration, so Phase 1 and Phase 2 remain independently deployable.

**Contract**: `dotnet ef migrations add AddLifecyclePushTypes --project server`.

#### 2. Payload bodies

**File**: `server/Push/PushPayloadFactory.cs`

**Intent**: Give the three new types notification bodies that carry no protected state.

**Contract**: extend the `body` switch at `:43-54` with the three new cases. Bodies stay generic and name no person, contact detail or event title — the S-06 rule that a push is a signal to refetch, never a transport for protected data. Suggested text: `"An event you joined was cancelled."`, `"You were removed from an event."`, `"A participant left your event."`. The existing empty-identifier guard at `:33-38` needs no change, because every lifecycle outbox row carries a real `EventJoinRequestId`.

#### 3. Lifecycle intents

**File**: `server/Push/PushIntentFactory.cs`

**Intent**: Add a second entry point that maps a completed lifecycle transition to one intent descriptor per recipient, without disturbing the existing join/accept/reject path.

**Contract**: a new `CreateLifecycleIntents` returning `IReadOnlyList<PushIntentDescriptor>`, taking the `SportsEvent`, the affected join-request rows and the actor id. Reuse the existing `EventKey` format `join-request:{requestId}:{(int)type}:{recipientUserId}` verbatim — type and recipient are already part of the key, so uniqueness holds across the new types with no format change. Recipient mapping: `EventCancelled` → each affected request's `RequesterUserId`; `ParticipantRemoved` → the removed `RequesterUserId`; `ParticipantLeft` → the event's `OrganizerUserId`. Keep the existing `Enum.IsDefined` and actor-authorization assertions — they are the last line of defence against a mis-addressed notification.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Migration applies cleanly: `dotnet ef database update --project server`

#### Manual Verification:

- Manually inserting a `PushOutbox` row with `Type = 6` and a valid join-request id is accepted; `Type = 7` is rejected by `CK_PushOutbox_Type`
- That manually inserted row is picked up by `PushDeliveryWorker` and delivered to a device, displaying the generic body with no name, contact or title in it
- Existing join, accept and reject notifications are unchanged

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: Cancel Event Endpoint

### Overview

The organizer-facing cancellation: one transaction that flips the event to `Cancelled`, terminates every outstanding pending and accepted request, and queues a notification for each of those people.

### Changes Required:

#### 1. Endpoint registration and handler

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Add the cancellation route and its transactional handler, following the shape of `TransitionJoinRequestAsync`.

**Contract**: register `POST /events/{eventId:guid}/cancel` in `MapEventEndpoints` (`:27-61`), inside the already-authorized `/api` group. The handler:
1. Opens a transaction and locks the event with the same raw `SELECT 1 FROM "SportsEvents" WHERE "Id" = {eventId} FOR UPDATE` used at `:659-661`.
2. Loads the event filtered by `OrganizerUserId == callerUserId`; a miss returns 404 `event_not_found` — a non-organizer must not be able to distinguish "not yours" from "does not exist".
3. If already `Cancelled`, commits and returns **200** with the existing state. Cancellation is idempotent, matching the replay convention `CreateEventAsync` already uses at `:79,174`.
4. If `Closed`, or `EstimatedEndsAtUtc <= now`, returns 409 `event_ended` — the Q10 boundary.
5. Reads the roster of requests whose status is `Pending` or `Accepted` **before** rewriting them (see Critical Implementation Details).
6. Sets each of those rows to `EventJoinRequestStatus.Cancelled` with `UpdatedUtc = now`, and the event to `Status = Cancelled`, `StatusChangedUtc = now`.
7. Builds one outbox row per roster member via `PushIntentFactory.CreateLifecycleIntents`, adds them, saves, and commits once.

**Returns**: `CancelEventResponse`. **Errors**: 404 `event_not_found`; 409 `event_ended`.

#### 2. Response DTO

**File**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs`

**Intent**: Give the client enough to update its card in place and to tell the organizer how many people were told.

**Contract**: `CancelEventResponse(Guid EventId, EventStatus Status, DateTimeOffset StatusChangedUtc, int NotifiedParticipantCount)`. `NotifiedParticipantCount` is the roster size from step 5 — the count of people notified, which for an idempotent replay is `0`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- `POST /api/events/{id}/cancel` as the organizer of an active event returns 200 and a body whose `Status` is `Cancelled`
- Repeating the identical call returns 200 with `NotifiedParticipantCount` of 0 and no new `PushOutbox` rows
- The same call from a non-organizer account returns 404 `event_not_found`
- Cancelling an event whose `EstimatedEndsAtUtc` is in the past returns 409 `event_ended`

#### Manual Verification:

- With a roster of one pending and one accepted requester, cancelling produces exactly two `PushOutbox` rows and both devices display the cancellation notification within 30 seconds
- After cancellation, both join-request rows read `Status = 6` with a non-null `UpdatedUtc`, and the event reads `Status = 2` with a non-null `StatusChangedUtc`
- The cancelled event no longer appears in the venue listing for any account
- `GET /api/events/{id}/contacts` returns 404 for both the organizer and the previously accepted participant
- Requesting to join the cancelled event returns 409 `event_cancelled`

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: Remove Participant and Leave Event Endpoints

### Overview

The two single-participant transitions. They are mirror images — same lock, same outbox write, opposite actor — so they share one helper and differ only in who is authorized and who gets told.

### Changes Required:

#### 1. Shared lifecycle transition helper

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Factor the common transaction once so remove and leave cannot drift apart in their locking or validation behaviour.

**Contract**: a private `TransitionParticipationAsync(dbContext, eventId, actorUserId, Guid? requestId, EventJoinRequestStatus targetStatus, ct)` returning a result discriminating success from each failure. It acquires the same `FOR UPDATE` event lock, resolves the target request under that lock, validates, mutates, enqueues one outbox row and commits. The lock is not optional — it is what keeps a removal from racing an acceptance past the participant limit.

Validation common to both: the event must be `Active` (409 `event_cancelled` if `Cancelled`, 409 `event_ended` if `Closed` or past end); the target request must currently be `Accepted` (409 `participant_not_accepted` otherwise, including the already-departed replay).

#### 2. Remove-participant endpoint

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Let an organizer eject an accepted participant.

**Contract**: `POST /events/{eventId:guid}/join-requests/{requestId:guid}/remove`, matching the verb-suffix convention of the existing `/accept` and `/reject` routes at `:27-61`. Authorization resolves the request with the organizer-match filter used at `:719`; a miss returns 404 `request_not_found`, so a non-organizer learns nothing. Target status `Removed`, notification `ParticipantRemoved` addressed to the requester. Returns `JoinRequestResponse` — the same shape accept and reject already return.

#### 3. Leave-event endpoint

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Let an accepted participant exit.

**Contract**: `POST /events/{eventId:guid}/join-requests/mine/leave`. The literal `mine` segment avoids any route ambiguity with the `{requestId:guid}` pattern and means the caller never passes an identifier they could tamper with. The request is resolved by `RequesterUserId == callerUserId`; a miss returns 404 `request_not_found`, which also covers an organizer calling it (an organizer holds no request row). Target status `Left`, notification `ParticipantLeft` addressed to the organizer. Returns `JoinRequestResponse`.

#### 4. Re-request after leaving

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Honour the Q4 decision — someone who left may ask again; someone who was removed may not.

**Contract**: in `TransitionJoinRequestAsync`'s `createIfMissing` branch, the existing-row path at `:680-689` currently short-circuits to a replay for *any* existing row. It must now branch on status: under the event lock, a `Left` row is revived to `Pending`, `CreatedUtc` is reset to the current request-attempt time, and **`UpdatedUtc` is reset to `NULL`** (required by `CK_EventJoinRequests_StatusUpdatedUtc`); a `Removed` row returns 409 `request_already_resolved`; a `Cancelled` row is unreachable because the event-status gate rejects first; `Pending` and `Accepted` keep today's replay behaviour. Treat `CreatedUtc` in the request DTO as the current attempt's creation time, not the immutable first-ever creation time. Add a dedicated `PushIntentFactory` path for the revived `JoinRequestCreated` notification whose key is `join-request:{requestId}:{(int)type}:{recipientUserId}:{createdUtc.UtcTicks}` using the same timestamp written to the row. Do not change keys for initial joins or the three new lifecycle types. Duplicate retries encounter the row as `Pending` under the same event lock and return the existing replay without creating another outbox occurrence.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Organizer removing an accepted participant returns 200 with `Status` of `Removed`
- Repeating the remove call returns 409 `participant_not_accepted`
- A non-organizer calling remove returns 404 `request_not_found`
- An accepted participant calling leave returns 200 with `Status` of `Left`
- A pending (not accepted) requester calling leave returns 409 `participant_not_accepted`
- The organizer calling leave on their own event returns 404 `request_not_found`
- Remove and leave on a cancelled event return 409 `event_cancelled`

#### Manual Verification:

- After a removal, the removed participant's device receives the notification within 30 seconds, and `GET /events/{id}/contacts` returns 404 for them while the organizer's contact list no longer includes them
- After a leave, the organizer's device receives the notification within 30 seconds and the freed slot is immediately visible as a lower participant count in the venue listing
- A user who left can request to join the same event again, the row returns to `Status = 1` with a refreshed `CreatedUtc` and `UpdatedUtc` NULL, and the organizer receives one fresh join-request notification whose `EventKey` ends with that attempt's UTC ticks
- A user who was removed attempting to request again is refused
- Removing a participant while a second join request is being accepted from another session produces a final accepted count that never exceeds `ParticipantLimit`

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 5: Client — Organizer Lifecycle Actions

### Overview

Surface cancel and remove on `MyEventsPage`, reusing the confirmation, snackbar and conflict-handling patterns the accept/reject flow already established.

### Changes Required:

#### 1. API client methods

**File**: `app/ChoNaBojoApp/Services/ApiService.cs` and its `IApiService` interface

**Intent**: Add the three lifecycle calls, translating status codes exactly as every existing method does.

**Contract**: `CancelEventAsync(Guid eventId, CancellationToken)`, `RemoveEventParticipantAsync(Guid eventId, Guid requestId, CancellationToken)` and `LeaveEventAsync(Guid eventId, CancellationToken)` (the last is consumed in Phase 6 but added here so the service surface lands once). Follow the `ResolveEventJoinRequestAsync` pattern at `:625-700`: 200 deserialize-and-validate, 404 and 409 into `EventConflictResponse`, 401 unauthorized, `HttpRequestException`/`TaskCanceledException` into network, everything else unknown. Remove and leave reuse the existing `ResolveJoinRequestResult`; cancel gets a `CancelEventResult` in `Services/Events/EventResults.cs` following the same factory-method shape.

#### 2. View model commands

**File**: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs`

**Intent**: Add the two organizer commands with the same in-flight guarding, confirmation and conflict handling that `ResolveRequestAsync` (`:637-778`) uses.

**Contract**: `[RelayCommand]` `CancelEventAsync(OrganizedEventViewData)` and `RemoveParticipantAsync(EventJoinRequestViewData)`. Both are destructive, so both confirm via `_feedbackService.ShowConfirmAsync` before any network call, using the `_isConfirmationInProgress` guard at `:676`. On successful cancel, mutate the organizer card in place with a `with { }` expression, then call the existing `ClearSelectedEvent` path so the selected request queue and all revealed contact payloads are purged without a refetch; returning to the list is intentional. On successful removal, mutate the affected request row in place as `ApplySuccessfulResolution` does. On conflict, show the server's `ConflictMessage` in a snackbar and call `RefreshSelectedEventStateAsync` (`:939`) so the UI reconciles with reality. `OrganizedEventViewData` gains a status field and a `CanCancel` computed flag; `EventJoinRequestViewData` gains `CanRemove`, true only while the row is `Accepted` and the event is active. Every contact visibility and contact-load guard must additionally require the event status to be `Active`, so reopening a cancelled event cannot reveal or fetch contacts.

Confirmation copy: cancel — *"Cancel this event? Everyone who asked to join or was accepted will be notified. This cannot be undone."*; remove — *"Remove this participant? They will be notified and cannot request this event again."*

#### 3. XAML

**File**: `app/ChoNaBojoApp/Views/MyEventsPage.xaml`

**Intent**: Place both actions where the organizer already manages the event.

**Contract**: a **Cancel event** button using `DestructiveButtonStyle` on the organizer detail summary card (`:291-314`), bound to `CancelEventCommand` and gated on `CanCancel`. A **Remove** button on each accepted request card in the queue (`:326-378`), bound through `RelativeSource AncestorType={x:Type viewModels:MyEventsViewModel}` as the accept/reject buttons already are, gated on `CanRemove`. Per `context/foundation/ui-guidelines.md`: no hardcoded colours, 48pt minimum touch targets, 8pt spacing multiples, a `SemanticProperties.Description` on every new control, and no `AppThemeBinding` dark branch.

### Success Criteria:

#### Automated Verification:

- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`

#### Manual Verification:

- Cancelling from My events shows the confirm dialog; dismissing it makes no network call and leaves the card untouched
- Confirming shows a success snackbar, the card immediately reflects the cancelled state, selected detail closes, and previously revealed contacts disappear without a refetch
- The Cancel button is absent or disabled on an event that is already cancelled or finished
- Removing a participant shows the confirm dialog, then the row updates in place to a removed state and the Remove button disappears
- Removing a participant whom the server has already removed shows the server's conflict message and the queue reconciles
- Both actions with the device offline show the network snackbar and leave the UI unchanged
- Double-tapping either button does not issue two requests

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 6: Client — Leave Event and Lifecycle Visibility

### Overview

The participant-side action, plus the presentation changes that make cancelled, finished and inactive participation legible instead of confusing: status badges and a separate collapsed *History* section.

### Changes Required:

#### 1. Leave command

**File**: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs`, `app/ChoNaBojoApp/Views/MyEventsPage.xaml`

**Intent**: Let an accepted participant exit from their My-requests card.

**Contract**: a `[RelayCommand]` `LeaveEventAsync(RequestedEventViewData)` mirroring the Phase 5 commands, confirming with *"Leave this event? The organizer will be notified. You can ask to join again later."* — wording that reflects the Q4 decision that leaving is reversible. A **Leave** button with `DestructiveButtonStyle` on the My-requests card, gated on a `CanLeave` flag that is true only while the request is `Accepted` and the event is active.

#### 2. Status badges and the History section

**File**: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs`, `app/ChoNaBojoApp/Views/MyEventsPage.xaml`

**Intent**: Separate live events from dead ones so a participant is never left guessing whether a game is still on.

**Contract**: `OrganizedEventViewData` and `RequestedEventViewData` gain a display label and an `IsHistory` flag. For organized events, **`IsHistory` is derived as `EventStatus == Cancelled || EventStatus == Closed || EstimatedEndsAtUtc <= now`**. For requested events, the live collection contains only active, unexpired events whose request `Status` is `Pending` or `Accepted`; `Rejected`, `Left`, `Removed` and `Cancelled` request rows are routed to history even when the event itself remains active. The trailing time comparison is essential because the auto-close job lags and may not exist at all if Phase 7 is cut. The list partitions into the existing live sections plus one collapsed *History* section, expanded by a bound `IsHistorySectionExpanded` toggle, defaulting to collapsed. Resolve the badge from event lifecycle first (`Cancelled` with `ErrorColor`, `Finished` with a neutral surface tone), then from terminal request state (`Rejected`, `Left`, `Removed`, or `Event cancelled`) so every inactive reason is explicit. Live events keep today's presentation. All lifecycle commands are hidden or disabled for anything in history.

#### 3. Push-triggered refresh

**File**: `app/ChoNaBojoApp/Services/Push/PushNavigationRouter.cs` *(verify only)*

**Intent**: Confirm a lifecycle notification lands the user somewhere that tells the truth.

**Contract**: the router already routes every push to `//MyEventsPage` and calls `RefreshFromPushAsync` when that page is already visible (`:48,103,106`). Because the new types carry the same data keys, no routing change is expected — but confirm on a device that a cancellation push received while My events is open re-partitions the list rather than leaving a stale live card.

### Success Criteria:

#### Automated Verification:

- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`

#### Manual Verification:

- An accepted participant can leave; the organizer's device is notified within 30 seconds and the card moves to *History* with a `Left` badge
- A pending requester sees no Leave button
- Having left, the same user can find the event in the venue listing and request to join again
- Rejected and removed request cards appear in *History* with explicit `Rejected` and `Removed` badges even while the event remains active
- A cancelled event appears under *History* with an error-toned `Cancelled` badge and no action buttons
- An event whose end time has passed shows as `Finished` even with the Phase 7 job stopped, proving the client-side time derivation works
- The History section starts collapsed and its expansion state survives a pull-to-refresh
- A cancellation push arriving while My events is open re-partitions the list without a manual refresh
- Accept, reject and join flows are unregressed

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 7: Auto-Close Hosted Service

### Overview

The background tidy-up that turns "finished" into a persisted fact. Deliberately last, because every read path already enforces the time boundary independently — this phase is the designated cut if the slice runs long.

### Changes Required:

#### 1. Hosted service

**File**: `server/Events/EventAutoCloseWorker.cs` *(new)*

**Intent**: Periodically close every active event whose estimated end time has passed.

**Contract**: a `BackgroundService` structured like `PushDeliveryWorker` (`server/Push/PushDeliveryWorker.cs`) — a loop that creates an `AsyncServiceScope` per iteration, because `ChoNaBojoContext` is scoped and must never be captured by a singleton. Poll interval 60 seconds (versus the push worker's 2, since closure is not latency-sensitive). Each iteration issues **one idempotent set-based statement**: update `SportsEvents` setting `Status = Closed` and `StatusChangedUtc = now` where `Status = Active` and `EstimatedEndsAtUtc <= now`. No `FOR UPDATE SKIP LOCKED` and no lease — unlike a push send, this has no external side effect, so running it twice or on two instances converges on the same result. The `WHERE Status = Active` clause is what makes it idempotent and what guarantees it can never resurrect or overwrite a `Cancelled` event. Log the affected row count when non-zero; log and swallow exceptions so a transient database error never terminates the host.

#### 2. Registration

**File**: `server/Program.cs`

**Intent**: Start the worker with the API.

**Contract**: `builder.Services.AddHostedService<EventAutoCloseWorker>();` next to the existing `AddHostedService<PushDeliveryWorker>()` at `:125`. No new configuration keys and no new secrets.

#### 3. Documentation

**File**: `AGENTS.md`

**Intent**: Record that the API now runs two background workers, since that constrains deployment.

**Contract**: extend the Architecture Notes line about background jobs to name both workers and to restate that the Railway API service must stay continuously running — the same constraint S-06 already documented for push.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- API starts cleanly with both hosted services registered: `dotnet run --project server`
- `GET /health` responds while both workers are running

#### Manual Verification:

- An event seeded with a past `EstimatedEndsAtUtc` flips to `Status = 3` with a non-null `StatusChangedUtc` within roughly one poll interval
- A cancelled event with a past end time stays `Status = 2` and is never overwritten to `Closed`
- Running two API instances against the same database produces no errors and no duplicated state
- A closed event disappears from the venue listing and appears under *History* on My events
- Stopping the worker and restarting it after a gap closes the accumulated backlog on the first pass
- Join, accept, reject, cancel, remove and leave are all unaffected while the worker runs

**Implementation Note**: This is the final phase. After manual confirmation, the slice is complete.

---

## Testing Strategy

Per the Q14 decision, this slice introduces no test infrastructure. Verification is the repository's established model: compile, apply migrations, exercise the API directly, and run a scripted manual matrix on a physical Android device.

### Database constraint checks (SQL, per Phase 1 and 2):

- Reject `SportsEvents.Status = 4` and `PushOutbox.Type = 7`
- Reject `Status = 2` with `StatusChangedUtc` NULL, and `Status = 1` with it set
- Accept `EventJoinRequests.Status` values 4, 5 and 6; reject 7

### API-level checks (HTTP, per Phase 3 and 4):

- Cancel: happy path, idempotent replay, non-organizer, already-ended
- Remove: happy path, replay, non-organizer, target not accepted
- Leave: happy path, replay, pending requester, organizer
- Both on a cancelled event, and both on a closed event
- Re-request after leave succeeds; re-request after removal is refused

### Manual device matrix (two accounts, one physical device plus a second device or emulator):

1. Organizer creates an event; user B requests; user C requests and is accepted.
2. Organizer cancels. **Expect**: both B and C notified within 30s; both see the event under *History* with a `Cancelled` badge; contacts return 404 for everyone; the event is gone from the venue listing.
3. Repeat the setup. Organizer removes C. **Expect**: C notified; C's request appears under *History* with a `Removed` badge; C's contact access is revoked; the freed slot is visible in the venue listing; C cannot request again.
4. Repeat the setup. C leaves. **Expect**: organizer notified; C's request appears under *History* with a `Left` badge; the slot is freed; C can request again and the organizer receives a fresh join-request notification.
5. Seed an event with a past end time. **Expect**: it shows as `Finished` on My events before the worker runs, and reads `Status = 3` after.
6. Concurrency: from two sessions, accept one request while removing another. **Expect**: accepted count never exceeds `ParticipantLimit`.
7. Offline and conflict paths for all three client actions.

### Privacy regression check (mandatory before sign-off):

For each of cancel, remove and leave, confirm `GET /api/events/{id}/contacts` stops serving the affected pair, and confirm no notification body or data payload contains a name, a contact detail or an event title.

## Performance Considerations

- A cancellation fan-out writes one join-request update and one outbox row per pending or accepted request. `ParticipantLimit` bounds accepted rows but pending rows are currently unlimited, so transaction cost is linear in the full roster and not strictly bounded. This risk is accepted for the MVP slice; `context/foundation/todo.md` tracks adding a locked-path outstanding-request cap before broader rollout.
- The event lock serializes all mutations on one event. This is already true for join, accept and reject; cancel holds it marginally longer because of the roster rewrite, but contention is per-event, not global.
- The auto-close sweep is a single indexed `UPDATE` on a 60-second cadence, served by the new `(Status, EstimatedEndsAtUtc)` index. Cost is proportional to events closing in that window, not to table size.
- Adding a `Status` filter to the venue listing narrows the existing `(VenueId, EstimatedEndsAtUtc)` index scan; no new index is warranted there at MVP volumes.

## Migration Notes

- **Two migrations**, one per plumbing phase, applied with `dotnet ef database update --project server`. The app never migrates itself.
- Existing `SportsEvents` rows backfill to `Status = 1` (`Active`) with `StatusChangedUtc` NULL via the column default. Existing `EventJoinRequests` rows are untouched — the widened CHECK only admits new values.
- **Deploy order matters**: apply migrations before the API build that writes the new values, and ship the API before the app build that reads `EventStatus` from `/me/events`. An older client receiving the new field ignores it; a newer client against an older API receives enum value `0`, rejects the payload through `MyEventsResult.Unknown()`, and exposes no lifecycle actions.
- **Rollback**: both migrations are reversible, but reverting Phase 1 after any event has been cancelled or closed loses that state irrecoverably. API rollback while the new app is deployed is not wire-compatible: My events deliberately fails closed until the matching API returns. Prefer leaving the schema and compatible API contract in place.

## References

- Roadmap slice: `context/foundation/roadmap.md:169-180` (S-07)
- PRD requirements: `context/foundation/prd.md` — FR-012, FR-013, FR-014
- Prior slice this builds directly on: `context/archive/2026-09-10-push-notifications/plan.md`
- Transactional template: `server/Events/EventEndpoints.cs:642-860`
- Background worker template: `server/Push/PushDeliveryWorker.cs`, `server/Push/PushOutboxProcessor.cs:82-133`
- Destructive-confirmation pattern: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs:676`
- UI rules: `context/foundation/ui-guidelines.md`
- Recurring rules: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `.github/skills/10x-plan/references/progress-format.md`.

### Phase 1: Event and Join-Request State Model

#### Automated

- [x] 1.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — daa86bc
- [x] 1.2 Migration is generated with no model-vs-snapshot drift — daa86bc
- [x] 1.3 Migration applies cleanly against the dev database — daa86bc
- [x] 1.4 `dotnet ef migrations has-pending-model-changes --project server` reports no drift — daa86bc

#### Manual

- [x] 1.5 `SportsEvents.Status = 4` rejected by `CK_SportsEvents_Status` — daa86bc
- [x] 1.6 `Status = 2` with NULL `StatusChangedUtc` rejected — daa86bc
- [x] 1.7 `EventJoinRequests.Status = 6` accepted, 7 rejected — daa86bc
- [x] 1.8 Pre-existing events read back as Active with NULL `StatusChangedUtc` — daa86bc
- [x] 1.9 Listing, join, accept, reject and contacts unregressed on active events — daa86bc
- [x] 1.10 Missing, zero or unknown event status fails closed as `MyEventsResult.Unknown()` — daa86bc

### Phase 2: Lifecycle Push Types and Intents

#### Automated

- [x] 2.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [x] 2.2 Migration applies cleanly

#### Manual

- [x] 2.3 `PushOutbox.Type = 6` accepted, 7 rejected by `CK_PushOutbox_Type`
- [x] 2.4 Manually inserted lifecycle row delivers to a device with a generic body
- [x] 2.5 Existing join, accept and reject notifications unchanged

### Phase 3: Cancel Event Endpoint

#### Automated

- [ ] 3.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [ ] 3.2 Organizer cancel returns 200 with Status Cancelled
- [ ] 3.3 Repeat cancel returns 200, 0 notified, no new outbox rows
- [ ] 3.4 Non-organizer cancel returns 404 `event_not_found`
- [ ] 3.5 Cancel on a past-end event returns 409 `event_ended`

#### Manual

- [ ] 3.6 Mixed pending/accepted roster yields one outbox row each, delivered within 30s
- [ ] 3.7 Join-request rows read Status 6 with `UpdatedUtc`; event reads Status 2 with `StatusChangedUtc`
- [ ] 3.8 Cancelled event absent from the venue listing for every account
- [ ] 3.9 Contacts return 404 for organizer and previously accepted participant
- [ ] 3.10 Joining a cancelled event returns 409 `event_cancelled`

### Phase 4: Remove Participant and Leave Event Endpoints

#### Automated

- [ ] 4.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [ ] 4.2 Organizer remove returns 200 with Status Removed
- [ ] 4.3 Repeat remove returns 409 `participant_not_accepted`
- [ ] 4.4 Non-organizer remove returns 404 `request_not_found`
- [ ] 4.5 Accepted participant leave returns 200 with Status Left
- [ ] 4.6 Pending requester leave returns 409 `participant_not_accepted`
- [ ] 4.7 Organizer leave returns 404 `request_not_found`
- [ ] 4.8 Remove and leave on a cancelled event return 409 `event_cancelled`

#### Manual

- [ ] 4.9 Removed participant notified within 30s and contact access revoked both ways
- [ ] 4.10 Leave notifies the organizer within 30s and frees the slot in the venue listing
- [ ] 4.11 Re-request after leave refreshes `CreatedUtc`, revives the row to Pending with NULL `UpdatedUtc`, and emits one attempt-keyed notification
- [ ] 4.12 Re-request after removal is refused
- [ ] 4.13 Concurrent remove and accept never exceed `ParticipantLimit`

### Phase 5: Client — Organizer Lifecycle Actions

#### Automated

- [ ] 5.1 Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- [ ] 5.2 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`

#### Manual

- [ ] 5.3 Cancel confirm dialog; dismissal makes no network call
- [ ] 5.4 Confirmed cancel updates the card, closes selected detail, and removes revealed contacts without a refetch
- [ ] 5.5 Cancel hidden or disabled on an already cancelled or finished event
- [ ] 5.6 Remove confirms, updates the row in place, and hides the button
- [ ] 5.7 Server-side conflict surfaces its message and the queue reconciles
- [ ] 5.8 Offline attempts show the network snackbar and leave the UI unchanged
- [ ] 5.9 Double-tap does not issue two requests

### Phase 6: Client — Leave Event and Lifecycle Visibility

#### Automated

- [ ] 6.1 Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- [ ] 6.2 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`

#### Manual

- [ ] 6.3 Accepted participant leaves; organizer notified within 30s and card moves to History with a Left badge
- [ ] 6.4 Pending requester sees no Leave button
- [ ] 6.5 A user who left can request to join again from the venue listing
- [ ] 6.6 Rejected and removed requests appear in History with explicit badges while the event remains active
- [ ] 6.7 Cancelled event appears under History with an error-toned badge and no actions
- [ ] 6.8 Past-end event shows as Finished with the Phase 7 worker stopped
- [ ] 6.9 History starts collapsed and its state survives a pull-to-refresh
- [ ] 6.10 Cancellation push while My events is open re-partitions the list without manual refresh
- [ ] 6.11 Accept, reject and join flows unregressed

### Phase 7: Auto-Close Hosted Service

#### Automated

- [ ] 7.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [ ] 7.2 API starts cleanly with both hosted services: `dotnet run --project server`
- [ ] 7.3 `GET /health` responds while both workers run

#### Manual

- [ ] 7.4 Past-end event flips to Status 3 with `StatusChangedUtc` within one poll interval
- [ ] 7.5 Cancelled past-end event stays Status 2 and is never overwritten
- [ ] 7.6 Two API instances produce no errors and no duplicated state
- [ ] 7.7 Closed event leaves the venue listing and appears under History
- [ ] 7.8 Restart after a gap closes the accumulated backlog on the first pass
- [ ] 7.9 Join, accept, reject, cancel, remove and leave unaffected while the worker runs
