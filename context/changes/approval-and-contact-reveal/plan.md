# Approval and Contact Reveal (S-05) Implementation Plan

## Overview

Close the matchmaking loop. The organizer sees join requests for their events on a new "My events" surface and accepts or rejects each one; acceptance transactionally claims a participant slot. On acceptance, the organizer and that participant — and only those two — can see each other's contact details through a single dedicated reveal endpoint. `AutoAccept`, persisted since S-03 but never honored, resolves through the same slot-claim path.

This is the roadmap's **north star** slice (`context/foundation/roadmap.md` §S-05) and its highest privacy-risk slice: a single authorization slip breaks the PRD's main guardrail ("contact info is only revealed after explicit organizer acceptance — never to unapproved requesters").

## Current State Analysis

**What exists (S-04, `context/archive/2026-09-06-event-listing-and-join-request/`):**

- `EventJoinRequest` entity (`server/Data/Entities/EventJoinRequest.cs`) with `Status`, `CreatedUtc`, `UpdatedUtc`. `UpdatedUtc` is declared but never written — nothing mutates a request today.
- `POST /api/events/{eventId}/join-requests` (`server/Events/EventEndpoints.cs:RequestToJoinEventAsync`) creates requests, always with `EventJoinRequestStatus.Pending`, and replays the stored request on retry.
- `GET /api/venues/{venueId}/events` projects `IsOrganizer`, `ParticipantCount` (`1 + AcceptedCount`), and `CurrentUserRequestStatus` per caller.
- DB constraints in `server/Data/ChoNaBojoContext.cs`: unique `IX_EventJoinRequests_SportsEventId_RequesterUserId`, an index on `(SportsEventId, Status)`, and `CK_EventJoinRequests_Status IN (1,2,3)`.
- Client: `EventCardViewData` (`app/ChoNaBojoApp/ViewModels/MapViewModel.cs:74`) renders join state; `IApiService.RequestToJoinEventAsync` returns a typed `JoinEventResult`.

**What's missing:**

- **No accept/reject path at all.** `AutoAccept` is stored on `SportsEvent`, surfaced in `EventListItemResponse`, rendered in the UI as "Enabled" / "Organizer approval required" — and does nothing. Every request is `Pending` forever.
- **No organizer surface.** `app/ChoNaBojoApp/AppShell.xaml` contains exactly one `ShellContent` (MapPage). Events are only reachable via map → venue bottom sheet → `VenueEventsPage` modal. An organizer cannot find requests without remembering which venue their event is at, and push notifications do not arrive until S-06.
- **No contact projection anywhere.** `User` (`server/Data/Entities/User.cs`) carries `ContactPhone`, `ContactEmail`, `CommunicatorPlatform`, `CommunicatorHandle` under the `CK_Users_ContactMethod` constraint (at least one method required), but `CurrentUserResponse` returns only `UserId` and no endpoint reads those columns.
- **No transactional capacity guarantee.** Capacity is read `AsNoTracking` (`EventEndpoints.cs:RequestToJoinEventAsync`) and compared as `1 + AcceptedCount >= ParticipantLimit`. The roadmap S-04 risk note explicitly defers the last-spot race to "transactional accept in S-05".
- **An open privacy deferral.** `context/foundation/lessons.md` records that organizer-authored `Title`/`Description` are projected to every authenticated user with only blank/length validation — an unguarded self-publish path for exactly the data class this slice gates. The lesson requires the owning slice to either close it or re-date it.

### Key Discoveries:

- All domain routes already sit under `app.MapGroup("/api").RequireAuthorization()` (`server/Program.cs:~150`) — authentication is enforced; **per-event authorization is not, and is this slice's job**.
- `app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs:389` already calls `EventValidation.ValidateCreateEventRequest` and `ApplyValidationErrors` maps `title`/`description` keys onto field errors (`CreateEventViewModel.cs:569-581`). **Phase 1 therefore needs no client changes** — the form inherits the new guard for free.
- Error convention (`context/foundation/lessons.md`): one body shape per status code. `EventConflictResponse(Code, Field, Message)` for 409, `ValidationProblemResponse` for 400. `ApiService.cs:189-196,248-255,304-311` parses exactly this. The legacy anonymous `{ message }` conflict body in `AuthEndpoints.cs:67,93` is already marked legacy-to-be-aligned; **do not extend it**.
- Client conventions: typed `*Result` records with a `Status` enum (`app/ChoNaBojoApp/Services/Events/EventResults.cs`); transport details never escape into view models; view data are immutable records replaced wholesale (`MapViewModel.ReplaceEventCard`, `MapViewModel.cs:1277`).
- `TruncateToMicrosecond` / `NormalizeUtcTimestamp` (`EventEndpoints.cs`) exist because PostgreSQL stores microsecond resolution — any new timestamp write must go through them.
- Timestamps are `timestamp with time zone`; `SportsEvent.Id` and `EventJoinRequest.Id` default to `gen_random_uuid()`.
- UI contract (`context/foundation/ui-guidelines.md` §8): contact values **must** bind through a view-model flag that is false unless status is `Accepted`; raw contact fields are never bound directly in XAML.

## Desired End State

A logged-in organizer opens a **"My events"** tab, sees their events with a pending-request count, drills into an event, and accepts or rejects each request. On accept, the participant's contact details become visible to the organizer, and the organizer's contact details become visible to that participant on their own "My requests" section. Rejections are final for that event. When an event has `AutoAccept` enabled, a join request is accepted the moment it is made and both sides see contact details immediately, with no organizer interaction.

Verification: two accounts on one device/emulator pair (or emulator + Windows head) can complete request → accept → mutual contact reveal end to end, and a third uninvolved account can reach neither party's contacts through any endpoint.

## What We're NOT Doing

- **No push notifications.** In-app pull only; FCM is S-06 (`roadmap.md` §S-06). The organizer learns of requests by opening the tab.
- **No cancel event / remove participant / leave event / auto-close job.** All S-07.
- **No re-request after rejection.** Rejection is final for that event in the MVP. A future slice may add time-gated re-submission (organizer-facing cooldown); this plan does not build it and does not add schema for it.
- **No auto-rejection of the remaining queue when an event fills.** Pending requests stay pending; further accepts return `event_full`.
- **No full-roster contact sharing.** Reveal is strictly pairwise: organizer ↔ each accepted participant. Accepted participants never see each other.
- **No new `Expired`/`Closed` request status.** The enum stays `Pending`/`Accepted`/`Rejected`.
- **No test projects.** Per the user's decision, this slice adds no unit or integration test projects; automated verification is limited to build, migration, and OpenAPI/HTTP-level checks the agent can run itself. Unit tests arrive in a later course module.
- **No in-app messaging, ratings, reporting, or blocking.** PRD Non-Goals.

## Implementation Approach

Five phases, ordered so each ends at a state that is manually verifiable and independently committable.

Phase 1 is a self-contained shared-validation change that closes the recorded `Title`/`Description` leak before this slice adds any further contact surface — it would be incoherent to ship acceptance-gated reveal while an unguarded self-publish path remains in the same feature.

Phases 2 and 3 build the server: first the write path (state machine + transactional slot claim + auto-accept), then the read path (my-events read model + the single contact-reveal choke point). Splitting them keeps the transactional logic verifiable via HTTP before any projection depends on it.

Phases 4 and 5 build the client: first the new navigation surface with organizer actions, then the contact-reveal component on both sides. This ordering means the reveal component is written last, against endpoints already proven by hand.

**Authorization posture (applies to Phases 2 and 3):** the caller's relationship to an event is derived server-side from the JWT subject on every request — never from a client-supplied role. A caller with no organizer/accepted-participant relationship to the addressed event receives **404**, indistinguishable from a nonexistent id, so request and event identifiers cannot be probed for existence.

## Critical Implementation Details

**Slot-claim ordering.** The accepted-count read and the status write must happen inside one transaction that first takes a row lock on the `SportsEvents` row. Reading the count before the lock, or locking the `EventJoinRequests` rows instead of the event row, both leave the last-spot race open: two organizer taps (or an organizer accept racing an auto-accept) can each observe `AcceptedCount = limit - 2` and both commit. EF Core has no first-class row-lock API, so the lock is issued explicitly before the recount:

```csharp
await dbContext.Database.ExecuteSqlAsync(
    $"""SELECT 1 FROM "SportsEvents" WHERE "Id" = {eventId} FOR UPDATE""",
    cancellationToken);
```

**Auto-accept reuses the same claim.** `RequestToJoinEventAsync` must not implement a second capacity check for the auto-accept path — it calls the same transactional claim helper. Two independent capacity code paths is the defect this design exists to prevent.

**Idempotent replay must stay ahead of dynamic checks.** The existing "look up an existing request before applying expiry/capacity/organizer checks" ordering in `RequestToJoinEventAsync` is deliberate (an exact retry replays the stored outcome even if the event later filled). Preserve it when adding auto-accept: the replay branch returns before any claim is attempted.

**`UpdatedUtc` is the transition marker.** It is currently always null. Every accept/reject writes it via `NormalizeUtcTimestamp(DateTimeOffset.UtcNow)`; nothing else writes it. The client uses it to order and label resolved requests.

---

## Phase 1: Free-text contact guard

### Overview

Close the `lessons.md` deferral: organizer-authored `Title` and `Description` are projected to every authenticated user, so a phone number or messenger handle typed there bypasses the acceptance-gated reveal entirely. Add a contact-pattern guard on the event write path in the shared validation project. No client changes — `CreateEventViewModel` already runs this validator and already maps `title`/`description` errors onto field errors.

### Changes Required:

#### 1. Contact-pattern detector

**File**: `shared/ChoNaBojo.Validation/ContactPatternGuard.cs` (new)

**Intent**: Provide a pure, framework-neutral predicate that reports whether a free-text string contains something that looks like a contact channel, so the same rule can be applied on the server write path and (already, transitively) in the client form. Precision is preferred over recall: a guard that fires on "court #2 at 18:00" or "5v5" would make the feature unusable, so patterns must be narrow and each one justified.

**Contract**: `public static bool ContainsContactPattern(string? text)` in namespace `ChoNaBojo.Validation`. Returns `false` for null/blank. Detects, at minimum: an email-shaped token; a run of digits that reads as a phone number once separators (spaces, dashes, dots, parentheses, a leading `+`) are stripped — long enough to exclude times, scores, and dates; an `http://`/`https://`/`www.` URL; and an `@handle` token (leading `@` followed by handle characters) that is not part of an email already matched. Use compiled `Regex` statics with a bounded match timeout — this runs on user input on both client and server. No dependency beyond `System.Text.RegularExpressions`; the project must remain free of ASP.NET Core, EF Core, Npgsql, and MAUI references.

#### 2. Wire the guard into event creation validation

**File**: `shared/ChoNaBojo.Validation/EventValidation.cs`

**Intent**: Reject event creation when the trimmed title or description contains a contact pattern, so the unguarded self-publish path is closed at the write boundary rather than by trusting organizers.

**Contract**: Inside `ValidateCreateEventRequest`, after the existing length checks, add errors under the existing `"title"` and `"description"` keys via `AddValidationError`. Message must tell the user what to do, not just that they failed — the value proposition is that contacts are exchanged automatically after acceptance, so say so (e.g. "Don't put contact details here — they're shared automatically once you accept someone."). Do not introduce new error keys: `CreateEventViewModel.ApplyValidationErrors` (`app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs:569-581`) and its `title`/`description` allow-list at line 613 already route exactly these two keys to field errors, and any other key falls through to the generic banner.

#### 3. Record the closure

**File**: `context/foundation/lessons.md`

**Intent**: The recorded deferral names this slice as the owner of enforcement; mark it closed so a future reader doesn't re-litigate it.

**Contract**: Append a dated closure note to the existing "User-authored free text becomes a contact-leak channel…" entry naming this change id and the guard's location. Do not rewrite or delete the existing rule — the file is append-only.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- `shared/ChoNaBojo.Validation` has no new package references and still references only `ChoNaBojo.Contracts` and `ChoNaBojo.Utils`: inspect `shared/ChoNaBojo.Validation/ChoNaBojo.Validation.csproj`
- API starts and `POST /api/events` with a description containing an email returns 400 with a `description` key: `dotnet run --project server` + an authenticated request

#### Manual Verification:

- Creating an event with a phone number in the description shows an inline error under the description field in the app, not a generic banner
- Creating an event titled "5v5 football, court #2, 18:00-19:30" succeeds — no false positive
- The error message tells the organizer that contacts are shared automatically after acceptance

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Server — transactional accept/reject and auto-accept

### Overview

Add the acceptance state machine. Accept and reject endpoints for the organizer, a row-locked transactional slot claim shared by manual accept and auto-accept, and resolution of the long-dead `AutoAccept` flag inside the existing join-request endpoint. No contact data is exposed in this phase.

### Changes Required:

#### 1. New conflict codes

**File**: `shared/ChoNaBojo.Contracts/Consts/EventConflictCodes.cs`

**Intent**: Name the new failure modes so the client can branch on stable machine-readable codes rather than message text.

**Contract**: Add constants for: the addressed request no longer being pending (`request_already_resolved`), and the addressed request/event not being visible to this caller (`request_not_found`). Reuse the existing `EventEnded` and `EventFull` codes for the accept path — accepting into an ended or full event is the same conflict class the join path already names. Follow the existing `snake_case` string convention.

#### 2. Transactional slot claim

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Provide the single code path that moves a join request to `Accepted` while guaranteeing the participant limit is never exceeded, so manual accept and auto-accept cannot each hold their own (divergent) capacity logic.

**Contract**: A private helper taking the `DbContext`, the target event id, the join request to accept (or the requester id, for the auto-accept path), and a cancellation token; returning either success or a typed conflict reason (`EventEnded`, `EventFull`, `EventNotFound`). It opens a transaction, takes `FOR UPDATE` on the `SportsEvents` row (see Critical Implementation Details for the raw-SQL form — EF Core exposes no row-lock API), re-reads `EstimatedEndsAtUtc` and the current `Accepted` count under the lock, applies the same `1 + AcceptedCount >= ParticipantLimit` rule already used at request time, writes `Status` and `UpdatedUtc`, and commits. Capacity counts the organizer as one participant — this must stay consistent with `GetVenueEventsAsync`'s `1 + AcceptedCount` projection and `CreatedEventResponse`'s hardcoded `1`.

#### 3. Accept and reject endpoints

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Let the organizer resolve a pending request, and only the organizer of that specific event.

**Contract**: Two routes registered in `MapEventEndpoints`, named following the existing `WithName` convention (`EventsJoinRequestsAccept`, `EventsJoinRequestsReject`):
`POST /events/{eventId:guid}/join-requests/{requestId:guid}/accept` and `.../reject`.
Both derive the caller from `httpContext.GetUserId()`. Both return `200` with the existing `JoinRequestResponse` on success — including the idempotent case where the request is already in the target status, so a double-tap replays rather than conflicts. Failure mapping:
- **404** `EventConflictResponse` (`request_not_found`) when the event or request does not exist, the request does not belong to that event, **or the caller is not that event's organizer** — these three cases must be indistinguishable to the caller, including in response timing order (perform the organizer check as part of the same lookup, not as a later branch).
- **409** `EventConflictResponse` when the request is in the *other* resolved status (`request_already_resolved`; e.g. accepting an already-rejected request — rejection is final per the scope decision), or, for accept only, when the event has ended (`event_ended`) or is full (`event_full`).
Both accept and reject resolve through the same event-row lock used by the slot-claim path. After acquiring the lock, re-read the request and organizer relationship, then apply the authorization, idempotency, and state-transition checks before writing `Status`/`UpdatedUtc`. Accept additionally performs the event-ended and capacity checks; reject does not. This single lock order serializes accept, reject, and auto-accept so competing resolutions cannot both succeed.

#### 4. Honor `AutoAccept` on request creation

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Make the S-03 auto-accept toggle real: on an auto-accept event, a join request is accepted at creation, so the participant sees "Joined" and the contact reveal immediately rather than waiting on an organizer who opted out of approving.

**Contract**: In `RequestToJoinEventAsync`, keep the existing replay-first ordering intact (the stored-request lookup returns before any claim). Read `AutoAccept` in the existing `eventState` projection. When it is set, the auto-accept helper owns the event-row lock, request insert, transition to `Accepted`, and commit within one transaction, so the insert and claim cannot interleave with a competing resolution. When the claim reports `event_full`, roll back and return the conflict without persisting a `Pending` request, matching the pre-existing capacity pre-check behavior. If `SaveChangesAsync` throws a unique-constraint or foreign-key `DbUpdateException`, roll back the transaction first and detach the failed `Added` request (or clear tracking) before leaving the helper; only then perform the existing unique replay query or foreign-key error mapping outside the aborted transaction. This preserves the current idempotent replay contract without issuing commands against a failed PostgreSQL transaction.

#### 5. Status transition guard at the data layer

**File**: `server/Data/ChoNaBojoContext.cs` + a new EF migration under `server/Migrations/`

**Intent**: `lessons.md` requires persisted enums to be guarded at both layers. `CK_EventJoinRequests_Status` already restricts the value set; this adds the *transition* invariant that the application now depends on — a resolved request always carries a resolution timestamp.

**Contract**: Add a check constraint on `EventJoinRequests` asserting that `UpdatedUtc IS NULL` when `Status = 1` (Pending) and `UpdatedUtc IS NOT NULL` otherwise, named following the existing `CK_EventJoinRequests_*` convention. Generate the migration with `dotnet ef migrations add` so the designer file and `ChoNaBojoContextModelSnapshot.cs` stay in sync. Existing rows are all `Pending` with null `UpdatedUtc` and satisfy the constraint, so no data backfill is needed.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Migration applies cleanly: `dotnet ef database update --project server`
- Model snapshot has no pending drift: `dotnet ef migrations has-pending-model-changes --project server`
- API starts and both new routes appear in the OpenAPI document: `dotnet run --project server` then fetch `/openapi/v1.json`
- Authenticated HTTP checks against a running API: organizer accept returns 200; a non-organizer accepting the same request returns 404; accepting an already-rejected request returns 409 `request_already_resolved`; a fabricated request id returns 404 with the same body shape as the non-organizer case

#### Manual Verification:

- Two accounts: B requests to join A's event, A accepts, B's card in `VenueEventsPage` shows "Joined" after refresh
- A rejects a second requester C; C's card shows "Request rejected" and re-tapping Join replays the rejection rather than creating a new pending request
- On an event created with auto-accept enabled, B's join request lands as `Accepted` immediately with no organizer action
- An event at its participant limit refuses further accepts with a full-event message, and the still-pending requests remain pending rather than disappearing

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Server — my-events read model and contact reveal

### Overview

Give the client the data behind the new surface: the caller's organized and requested events, the request queue for an organized event, and — through one dedicated, auditable endpoint — the contact details the caller is entitled to for a given event. Contact data appears in no other projection.

### Changes Required:

#### 1. Contact DTO

**File**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs` (or a new `ContactDTOs.cs` in the same folder)

**Intent**: Carry a user's opted-in contact methods across the boundary, mirroring the shape the user chose at registration, without exposing any credential or identity field.

**Contract**: Add these exact records:

```csharp
public sealed record ContactInfoResponse(
	string? Phone,
	string? Email,
	CommunicatorPlatform? CommunicatorPlatform,
	string? CommunicatorHandle);

public sealed record EventContactResponse(
	Guid UserId,
	Guid? JoinRequestId,
	bool IsOrganizer,
	ContactInfoResponse Contact);

public sealed record EventContactsResponse(
	IReadOnlyList<EventContactResponse> Contacts);
```

`IsOrganizer` is the wire representation of the event relationship, avoiding another cross-boundary enum. `JoinRequestId` is null only on an organizer row and is required on every accepted-participant row. The organizer receives zero or more accepted-participant rows; an accepted participant receives exactly one organizer row; unauthorized callers receive 404 rather than an empty collection. Example organizer payload:

```json
{
  "contacts": [{
    "userId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
    "joinRequestId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
    "isOrganizer": false,
    "contact": {
      "phone": "+48123123123",
      "email": null,
      "communicatorPlatform": 3,
      "communicatorHandle": "48123123123"
    }
  }]
}
```

Example accepted-participant payload:

```json
{
  "contacts": [{
    "userId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "joinRequestId": null,
    "isOrganizer": true,
    "contact": {
      "phone": null,
      "email": "organizer@example.com",
      "communicatorPlatform": null,
      "communicatorHandle": null
    }
  }]
}
```

Reuse the existing cross-boundary `CommunicatorPlatform` enum — **no new enum**. The DTO must never carry `LoginEmail`, `NormalizedLoginEmail`, or `PasswordHash`; `UserId` exists only to key a rendered contact row. Per `lessons.md`, secrets never enter Contracts. `CK_Users_ContactMethod` guarantees at least one method is non-null, but the client still renders defensively.

#### 2. My-events and request-queue DTOs

**File**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs`

**Intent**: Describe the two lists the new tab renders, reusing existing summary types so the client's formatting helpers apply unchanged.

**Contract**: A response type for an event the caller organizes — event id, title, times, participant limit and current count, `AutoAccept`, `EventVenueSummary`, `EventSportSummary`, and a **pending request count** so the tab can show an actionable badge without a second call. A response type for an event the caller requested — the same event shell plus the caller's `EventJoinRequestStatus` and the request's `UpdatedUtc`. A response type for one row in an organizer's request queue — request id, requester display key, `EventJoinRequestStatus`, `CreatedUtc`, `UpdatedUtc`. **The request-queue row carries no contact fields**; contacts come only from the reveal endpoint. Reuse `EventVenueSummary`/`EventSportSummary` rather than redeclaring venue/sport shapes.

> Requester identity: the app collects no display name (`RegisterRequest` has no name field), so the queue must identify requesters without leaking an unrevealed contact channel. Use a stable, non-contact label derived server-side — e.g. an ordinal within the event ("Requester 1") combined with the request timestamp. Do **not** send `LoginEmail` or any contact fragment as a display label; that would defeat the gate this phase exists to enforce.

#### 3. My-events endpoints

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Let the client render the "My events" tab in one call per section, without the caller ever addressing another user's data.

**Contract**: `GET /me/events` returning the caller's organized events and requested events (either as one composite response or two collections in one payload — one round trip either way), scoped entirely by `httpContext.GetUserId()` with **no user id accepted from the client**. `GET /events/{eventId:guid}/join-requests` returning the request queue for an event the caller organizes; 404 `request_not_found` when the event does not exist *or* the caller is not its organizer. Follow the existing `AsNoTracking` + anonymous-projection-then-map pattern from `GetVenueEventsAsync`, and the `new DateTimeOffset(value, TimeSpan.Zero)` convention when converting stored UTC to the wire.

Ordering: organized events by `StartsAtUtc` then `Id`; requested events the same; queue rows pending-first, then by `CreatedUtc`. Consistent with `GetVenueEventsAsync`'s deterministic ordering so lists don't shuffle between refreshes.

Past events: `GetVenueEventsAsync` hides events whose `EstimatedEndsAtUtc` has passed. The my-events lists must **not** apply that filter — `ui-guidelines.md` §6C states past events "may appear in the user's own-events list" (dimmed, "Ended" chip). A user needs their just-finished event's contacts to still be reachable.

#### 4. Contact reveal endpoint

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Concentrate every contact disclosure in the codebase into one route with one authorization decision, so the gate is auditable in a single place and no future change to a list projection can leak contact data by accident.

**Contract**: `GET /events/{eventId:guid}/contacts`. Resolves the caller's relationship to the event from the JWT subject and returns exactly what that relationship entitles them to:
- **organizer** → the contact block of every participant whose request on this event is `Accepted`;
- **accepted participant** → the organizer's contact block only;
- **anyone else** — including a pending requester, a rejected requester, and a stranger — → **404** `EventConflictResponse` (`request_not_found`), indistinguishable from a nonexistent event.

The entitlement must be computed from the database in the same query that fetches the contacts, never from a client-supplied role, event field, or prior response. Accepted participants must not receive each other's contacts (pairwise only). This is the only endpoint in the codebase permitted to read `User.ContactPhone`, `User.ContactEmail`, `User.CommunicatorPlatform`, or `User.CommunicatorHandle` — note this in a comment on the handler so a future author does not casually add a second reader.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- API starts and all three new routes appear in the OpenAPI document: `dotnet run --project server` then fetch `/openapi/v1.json`
- Authorization matrix verified by authenticated HTTP calls against a running API, with six seeded accounts (organizer A, accepted participant B, uninvolved C, pending requester D, rejected requester E, second accepted participant F) on a dedicated event whose participant limit is at least 3:
  - `GET /api/events/{id}/contacts` as A returns B's contact block
  - as B returns A's contact block and **not** any other participant's
  - as C returns 404
  - as a *pending* requester D returns 404
  - as a *rejected* requester E returns 404
  - after A accepts a second participant F: B's response still contains only A — not F
- `GET /api/me/events` and `GET /api/events/{id}/join-requests` response bodies contain **no** `contactPhone`/`contactEmail`/`communicatorHandle`/`loginEmail` keys — grep the raw JSON
- `GET /api/events/{id}/join-requests` as a non-organizer returns 404 with the same body shape as a nonexistent event id

#### Manual Verification:

- Contact values returned for an accepted pair match what those two accounts entered at registration, including a user who supplied only one method
- An event whose estimated end time has passed still appears in the organizer's own-events payload (it is hidden from the venue listing)
- The pending-request count on an organized event matches the queue length

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Client — "My events" tab with organizer accept/reject

### Overview

Add the app's second navigation surface: a tab listing the events the user organizes and the events they've requested to join, with the organizer able to accept or reject each pending request. Contact reveal is deliberately deferred to Phase 5 — this phase ends with a fully working approval loop whose contact rows are still locked.

### Changes Required:

#### 1. Typed client results

**File**: `app/ChoNaBojoApp/Services/Events/EventResults.cs`

**Intent**: Give each new call the same transport-free outcome type the rest of the app uses, so view models branch on a status enum and never see an `HttpStatusCode`.

**Contract**: Result records + status enums for the my-events list, the organizer request queue, and accept/reject. Each follows the existing shape in this file: private constructor, static factory per outcome, `Success` / `Unauthorized` / `Network` / `Unknown`, plus `Conflict(EventConflictResponse)` where the endpoint can return 409, and a distinct `NotFound` outcome for the 404 posture introduced in Phases 2–3 (the client must render "this is no longer available" rather than an error). Accept/reject success carries the updated `JoinRequestResponse`.

#### 2. API surface

**File**: `app/ChoNaBojoApp/Services/IApiService.cs`, `app/ChoNaBojoApp/Services/ApiService.cs`

**Intent**: Expose the new endpoints through the existing authenticated named client so bearer tokens and transparent refresh apply unchanged.

**Contract**: Methods for fetching my-events, fetching an event's request queue, and accepting/rejecting a request by (event id, request id). Implementation follows the established `switch (response.StatusCode)` pattern at `ApiService.cs:230-258`, parsing `EventConflictResponse` on 409 **and on 404** (both use that body shape per the recorded decision), returning `Network()` on `HttpRequestException`/`TaskCanceledException` and `Unknown()` on an unparseable body. Reuse the existing static `JsonOptions`. Do **not** add a contact method here — that lands in Phase 5.

#### 3. Shell route

**File**: `app/ChoNaBojoApp/AppShell.xaml`, `app/ChoNaBojoApp/AppShell.xaml.cs`, `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Make the new surface reachable; today the Shell has exactly one `ShellContent` and therefore renders no tab bar.

**Contract**: Add a second `ShellContent` (route `MyEventsPage`) alongside the existing `MapPage` entry, converting the Shell to a two-tab layout with Material Symbols icons per `ui-guidelines.md` §6D. Register the new page and its view model in `MauiProgram` following the existing DI registration lifetimes used for `MapPage`/`MapViewModel`. Verify the map surface still behaves correctly once a tab bar exists — `MapPage` currently assumes it is the only Shell content, and the venue bottom sheet plus `VenueEventsPage`'s modal push/pop lifecycle (`VenueEventsPage.xaml.cs:OnDisappearing`) are sensitive to navigation changes.

#### 4. My-events view model

**File**: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs` (new)

**Intent**: Own the tab's state: load both sections, drill into an organized event's request queue, and run accept/reject with in-flight and conflict handling.

**Contract**: Derives from `ViewModelBase`. Immutable view-data records per row (organized event, requested event, queue row) mirroring `EventCardViewData`'s style — computed display properties (`ActionLabel`, `CanAccept`, `IsActionInFlight`, `SemanticDescription`) live on the record, and rows are replaced wholesale via a `ReplaceRow`-style helper rather than mutated. Commands for refresh, open-requests, accept, reject, each guarded by a `CanExecute` that blocks re-entry while in flight and calls `NotifyCanExecuteChanged` in a `finally`. `OpenRequestsCommand` selects an organized event and populates an in-page request-detail state on this same view model; no detail page or Shell route is added. Reuse `IFeedbackService` for snackbars and the existing `SessionExpiryCoordinator` path on `Unauthorized`. On conflict, map the `EventConflictResponse.Code` to user-facing copy and refresh the affected event so the UI converges on server truth: `event_full` → the queue stays but accepts are disabled; `request_already_resolved` → refresh the row; `event_ended` → mark the event ended; `request_not_found` → drop the row.

Expose a `HasRevealedContact`-style gate property on the accepted-participant rows that is **hard-wired false in this phase** and becomes real in Phase 5 — per `ui-guidelines.md` §1 and §8, contact values must bind through such a flag and never directly.

#### 5. My-events page

**File**: `app/ChoNaBojoApp/Views/MyEventsPage.xaml`, `MyEventsPage.xaml.cs` (new)

**Intent**: Render both sections and the request queue using the project's established card/state vocabulary.

**Contract**: Follows `ui-guidelines.md` strictly — no hardcoded colors, spacing only from the 8pt set, `{StaticResource}` for every token, Uranium Material controls, `PrimaryButtonStyle`/`SecondaryButtonStyle` for actions, `CardShadow` on cards, `48` minimum touch targets, and `SemanticProperties.Description` on every interactive control. Card states per §6C: joinable/full/past (past = `Opacity="0.6"` + "Ended" chip, since own-events lists include past events). All three screen states from §10 (loading spinner tinted `PrimaryColor`, empty state with icon + `TitleStyle` message, error state with `ErrorColor` icon + Retry). Render the selected organized event's request queue as an in-page detail section beneath/in place of the list; closing it returns to the list without Shell navigation. Reject is a destructive action and therefore uses the §9 MD3 confirmation dialog with an `ErrorColor` confirm button — reuse `Views/Popups/ConfirmDialog`. Contact rows render only their **locked** state in this phase ("Contact shared after acceptance" with a lock symbol, per §8). `MyEventsPage.OnAppearing` refreshes list, selected-event, and request status so the tab converges after backgrounding or tab switches. `MyEventsPage.OnDisappearing` invokes a view-model cleanup method that clears selected-event contact DTOs and gated contact rows before returning; the same cleanup runs before `OpenRequestsCommand` changes the selected event.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- No hardcoded colors or off-grid spacing in the new XAML: grep the new files for `#` hex literals, `TextColor="Black|White"`, and `Margin`/`Padding`/`Spacing` values outside `{4,8,16,24,32,48}`

#### Manual Verification:

- The app shows two tabs; the map, venue bottom sheet, and venue-events modal all still work, including closing the modal and returning to the map
- An organizer sees their events with an accurate pending-request badge, opens one, and sees the queue
- Accept moves the row to accepted and the participant count increments; reject asks for confirmation first and then moves the row to rejected
- Double-tapping Accept does not produce a duplicate action or an error
- A participant sees their requested events with correct status labels, and contact rows show the locked "Contact shared after acceptance" state
- Killing the network mid-accept produces a retryable message, not a crash or a silently wrong row state
- An empty account sees the empty state, not a spinner or a blank screen

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Client — contact reveal

### Overview

Wire the reveal endpoint into both sides and build the contact component with its locked/revealed states. This is the phase that closes the loop and proves the PRD's primary success criterion.

### Changes Required:

#### 1. Contact API call and result

**File**: `app/ChoNaBojoApp/Services/IApiService.cs`, `ApiService.cs`, `Services/Events/EventResults.cs`

**Intent**: Fetch the caller's entitled contacts for one event, treating 404 as the ordinary "not entitled / no longer available" outcome rather than an error.

**Contract**: A method taking an event id and returning a typed result with `Success` (carrying the contact payload), `NotFound`, `Unauthorized`, `Network`, `Unknown`. Same `switch (response.StatusCode)` structure as the other calls. The result type must not expose transport details.

#### 2. Contact view data and gate

**File**: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs`

**Intent**: Turn the nullable-field DTO into renderable rows and enforce the reveal gate in exactly one place.

**Contract**: Make the Phase 4 gate property real: it is true only when the underlying request status is `Accepted` **and** a contact payload was fetched for that event. Project `ContactInfoResponse` into an ordered row collection, skipping null/blank methods — a user who supplied only a phone yields one row. Each row carries its kind, display value, nullable launch URI, `CanLaunch`, and icon key; separate `OpenContactCommand` and `CopyContactCommand` give each gesture exactly one outcome.

URI construction is conservative and centralized in the view model:
- Phone: trim the value, accept only a leading `+` plus digits, spaces, parentheses, or hyphens, normalize to the leading `+` and digits, and emit `tel:{normalized}`; otherwise copy-only.
- Email: require `MailAddress.TryCreate` success and emit `mailto:{Uri.EscapeDataString(address)}`; otherwise copy-only.
- Messenger / Instagram: trim whitespace and one optional leading `@`; only `[A-Za-z0-9._]+` usernames produce `https://m.me/{escaped}` or `https://www.instagram.com/{escaped}/`.
- WhatsApp: remove a leading `+` and phone separators; only 7–15 digits produce `https://wa.me/{digits}`.
- Any unmapped platform or handle that fails its platform validation has no launch URI and remains copy-only.

`OpenContactCommand` calls `Launcher.Default.OpenAsync` only when `CanLaunch`; `CopyContactCommand` always copies the displayed value through `Clipboard.Default.SetTextAsync`. Contacts are fetched lazily when an accepted event's card is opened. The cleanup method called by `MyEventsPage.OnDisappearing` and before changing the selected event clears both the raw contact DTO reference and every projected/gated row; `OnAppearing` refreshes status before contacts can be fetched again. Never persist contacts to `TokenStore` or any other on-device storage.

#### 3. Messenger brand icons

**File**: `app/ChoNaBojoApp/Resources/Images/` (new assets)

**Intent**: `ui-guidelines.md` §8 requires messenger rows to show the communicator's brand icon; these are not in Material Symbols.

**Contract**: Add monochrome SVGs named `msg_whatsapp`, `msg_messenger`, `msg_instagram`, and a `msg_generic` fallback, tinted to `SecondaryTextColor`. The mapping from `CommunicatorPlatform` to icon key must fall back to `msg_generic` for any unmapped value rather than throwing — this is the client-side half of the both-layers enum posture from `lessons.md`.

#### 4. Contact component

**File**: `app/ChoNaBojoApp/Views/ContactRevealView.xaml`, `ContactRevealView.xaml.cs` (new) + consumption in `MyEventsPage`

**Intent**: A single component with the two states from `ui-guidelines.md` §8, used identically by the organizer and participant sides so the reveal rule cannot diverge between them.

**Contract**: Name the reusable component `ContactRevealView`. **Locked** (default): a disabled row with a lock Material Symbol and "Contact shared after acceptance" in `SecondaryTextColor`; the raw value is not merely hidden but **never bound** — the binding source must be the gated collection, which is empty when locked. **Revealed**: rows on a `SurfaceColor` card in `BodyStyle`, with the display value, a dedicated copy button, a distinct open button visible only when `CanLaunch`, and the brand icon for messenger rows. The row itself has no tap gesture. Every button sets an action-specific `SemanticProperties.Description`. Visibility binds to the view-model gate flag from change 2 — never to a raw contact field, per §1's hard rule.

#### 5. Roadmap status

**File**: `context/foundation/roadmap.md`

**Intent**: S-05 is the north star; its completion is the project's primary milestone signal.

**Contract**: Flip the S-05 row in the "At a glance" table and the S-05 section's `Status` from `proposed` to `done`, and update the Backlog Handoff row. Leave the `## Done` section to `/10x-archive`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- No raw contact field is bound in XAML: grep the new and modified XAML for `ContactPhone`, `ContactEmail`, `CommunicatorHandle` — every binding must go through the gated collection or the gate flag
- Contacts are not written to persistent storage: grep the client diff for `TokenStore`, `SecureStorage`, and `Preferences` usage in the contact code path

#### Manual Verification:

- **North star, end to end:** account B requests to join A's event, A accepts, and B sees A's contact details while A sees B's — on two real accounts
- An account with only a messenger handle shows exactly one row with the correct brand icon; valid handles expose the mapped Open action, while invalid or unmapped handles use the generic icon and remain copy-only
- The phone and email Open buttons launch the dialer and mail composer; each separate Copy button puts only that row's value on the clipboard
- A pending requester and a rejected requester both see the locked state, never a value
- On an auto-accept event, contact details appear to both sides immediately after the join request, with no organizer action
- Leaving the tab or changing the selected event clears raw and projected contacts immediately; returning refreshes authorization before refetch and never flashes a prior event's value. Revocation-cache verification is deferred to S-07, when participant removal introduces an accepted-to-unentitled transition.
- Contact rows are legible at large system font sizes (no clipped fixed-height containers)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

No test projects are added in this slice (see "What We're NOT Doing"). Verification is split as follows.

### Agent-automatable checks:

- `dotnet build solutions/ChoNaBojo.slnx` and `dotnet build app/ChoNaBojoApp -f net10.0-android` after every phase
- `dotnet ef database update --project server` and `dotnet ef migrations has-pending-model-changes --project server` after Phase 2
- OpenAPI document inspection for route presence after Phases 2 and 3
- **The Phase 3 authorization matrix run as authenticated HTTP calls** against a locally running API with six seeded accounts and a dedicated event whose participant limit is at least 3 — this is the highest-value automatable check in the slice and must not be downgraded to manual. This fixture is separate from the three-account, limit-2 manual walkthrough below.
- Raw-JSON grep of `GET /api/me/events` and `GET /api/events/{id}/join-requests` responses for contact-field key names
- Source greps: hardcoded colors / off-grid spacing in new XAML; raw contact bindings in XAML; persistent-storage writes in the contact path

### Manual testing steps:

1. Register accounts A, B, C, each with a different contact-method combination (A: phone only; B: all three; C: messenger only).
2. As A, create an event at a Warsaw venue with `AutoAccept` **off** and a limit of 2.
3. As B, find the event on the map and request to join. As C, do the same.
4. As A, open the "My events" tab — confirm the pending badge reads 2. Accept B, reject C (confirm the destructive dialog appears).
5. Confirm A now sees B's three contact rows; B sees A's single phone row; C sees the locked state.
6. Confirm A cannot accept C after rejection, and the event now reports full for further accepts while C's rejected row remains visible.
7. Repeat steps 2–5 on a second event with `AutoAccept` **on** — confirm B is accepted with no organizer action and both sides see contacts immediately.
8. Let an event's estimated end time pass; confirm it disappears from the venue listing but remains in both parties' own-events lists with contacts still reachable.
9. Attempt event creation with a phone number and an email in the description (Phase 1 guard), then with "5v5 football, court #2, 18:00-19:30" (no false positive).

## Performance Considerations

The `FOR UPDATE` lock is held for the duration of one recount and one update on a single row. Contention is effectively nil at MVP scale (one organizer accepting sequentially), and the existing `IX_EventJoinRequests_SportsEventId_Status` index serves the recount. `GET /me/events` is bounded by how many events one user organizes or has requested — no pagination in the MVP, consistent with the venue listing, which is also unpaginated. The contact endpoint returns at most `ParticipantLimit - 1` blocks (≤ 299) and is fetched lazily per event rather than with the list.

## Migration Notes

One new migration in Phase 2 adds the `UpdatedUtc`/`Status` transition check constraint. All existing `EventJoinRequests` rows are `Pending` with a null `UpdatedUtc` and satisfy it, so no backfill is required and the migration is safe to apply to the live Supabase database. There is no schema change in any other phase. Rollback is `dotnet ef database update <previous-migration>`; the application never auto-migrates (see the `ChoNaBojoContext` summary comment).

## References

- Roadmap slice: `context/foundation/roadmap.md` §S-05 (north star)
- PRD: `context/foundation/prd.md` — FR-005 (auto-accept), FR-008/FR-009 (approval), FR-011 (contact reveal), Access Control, Success Criteria guardrails
- UI contract: `context/foundation/ui-guidelines.md` §6C (card states), §8 (contact reveal), §9 (feedback), §10 (screen states)
- Binding rules: `context/foundation/lessons.md` — persisted-enum both-layers guard; shared-project placement; one error shape per status code; free-text contact-leak channel
- Prior slice: `context/archive/2026-09-06-event-listing-and-join-request/plan.md`
- Join request write path: `server/Events/EventEndpoints.cs` (`RequestToJoinEventAsync`)
- Client card pattern: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:74` (`EventCardViewData`)
- Client result pattern: `app/ChoNaBojoApp/Services/Events/EventResults.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Free-text contact guard

#### Automated

- [x] 1.1 Solution builds — 26b277f
- [x] 1.2 ChoNaBojo.Validation has no new package references — 26b277f
- [x] 1.3 POST /api/events with an email in the description returns 400 with a description key — 26b277f

#### Manual

- [x] 1.4 Phone number in description shows an inline field error, not a generic banner — 26b277f
- [x] 1.5 "5v5 football, court #2, 18:00-19:30" succeeds — no false positive — 26b277f
- [x] 1.6 Error message explains contacts are shared automatically after acceptance — 26b277f

### Phase 2: Server — transactional accept/reject and auto-accept

#### Automated

- [x] 2.1 Solution builds
- [x] 2.2 Migration applies cleanly
- [x] 2.3 No pending model changes
- [x] 2.4 Both new routes appear in the OpenAPI document
- [x] 2.5 Accept/reject HTTP checks: 200 organizer, 404 non-organizer, 409 already-resolved, 404 fabricated id

#### Manual

- [x] 2.6 B requests, A accepts, B's card shows "Joined" after refresh
- [x] 2.7 A rejects C; C's card shows rejected and re-tapping Join replays the rejection
- [x] 2.8 Auto-accept event accepts the request immediately with no organizer action
- [x] 2.9 Full event refuses further accepts and leaves pending requests pending

### Phase 3: Server — my-events read model and contact reveal

#### Automated

- [ ] 3.1 Solution builds
- [ ] 3.2 All three new routes appear in the OpenAPI document
- [ ] 3.3 Contact-reveal authorization matrix verified across organizer, accepted, pending, rejected, uninvolved, and second-participant callers
- [ ] 3.4 my-events and join-requests raw JSON contain no contact or login-email keys
- [ ] 3.5 join-requests as a non-organizer returns 404 with the nonexistent-event body shape

#### Manual

- [ ] 3.6 Revealed contacts match registration input, including a single-method user
- [ ] 3.7 A past event still appears in the organizer's own-events payload
- [ ] 3.8 Pending-request count matches the queue length

### Phase 4: Client — "My events" tab with organizer accept/reject

#### Automated

- [ ] 4.1 Solution builds
- [ ] 4.2 Android app builds
- [ ] 4.3 No hardcoded colors or off-grid spacing in the new XAML

#### Manual

- [ ] 4.4 Two tabs render; map, venue sheet, and venue-events modal all still work
- [ ] 4.5 Organizer sees an accurate pending badge and opens the request queue
- [ ] 4.6 Accept increments the count; reject confirms first, then resolves the row
- [ ] 4.7 Double-tapping Accept produces no duplicate action or error
- [ ] 4.8 Participant sees correct statuses and the locked contact state
- [ ] 4.9 Network loss mid-accept produces a retryable message, not a crash or wrong state
- [ ] 4.10 An empty account sees the empty state

### Phase 5: Client — contact reveal

#### Automated

- [ ] 5.1 Solution builds
- [ ] 5.2 Android app builds
- [ ] 5.3 No raw contact field is bound in XAML
- [ ] 5.4 Contacts are not written to persistent storage

#### Manual

- [ ] 5.5 North star: request → accept → mutual contact reveal on two real accounts
- [ ] 5.6 Single-method users render correctly; valid communicator handles open, while invalid/unmapped handles are copy-only
- [ ] 5.7 Separate phone/email Open and Copy actions work correctly
- [ ] 5.8 Pending and rejected requesters both see the locked state
- [ ] 5.9 Auto-accept event reveals contacts immediately to both sides
- [ ] 5.10 Leaving the tab or changing events clears contacts; returning refetches without flashing prior values
- [ ] 5.11 Contact rows stay legible at large system font sizes
