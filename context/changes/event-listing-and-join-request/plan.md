# Event Listing and Join Request Implementation Plan

## Overview

Implement roadmap slice S-04 as an authenticated end-to-end flow. A user selects a venue on the map, sees its non-expired events with participant fill state, optionally narrows them by sport and an availability window, and sends a durable join request. The change establishes the request state that S-05 will later accept or reject, while keeping acceptance, auto-accept activation, capacity allocation, notifications, and contact reveal out of this slice.

## Current State Analysis

S-03 provides the `SportsEvent` model, an authenticated create endpoint, shared event contracts and validation, typed MAUI API outcomes, and a map venue sheet with a working Create event action. An event currently reports participant count as the organizer only. There is no event-list endpoint, join-request persistence, participant relationship, availability-filter contract, or join UI.

The venue sheet is embedded in `MapPage` and currently contains a static prompt above the Create event button. It is wrapped in a `ScrollView`, so adding a vertically scrolling event list requires restructuring the sheet rather than nesting a `CollectionView`. Existing client patterns provide cancellation-aware commands, typed transport outcomes, snackbar feedback, modal `ShowAsync` handoffs, and local-wall-time-to-UTC conversion. Generation-scoped supersession specifically lives in `AddressSearchViewModel` (`_sessionCancellation` / `_suggestionSearchCancellation` via `CreateLinkedTokenSource`), not in `MapViewModel`, whose cancellation today is only the `[RelayCommand]`-supplied token.

## Desired End State

Selecting a venue loads all of its events whose estimated end time is still in the future, ordered by start time. The active map sport optionally narrows the list, and the user can apply `Any time`, `Today`, `Tomorrow`, `Next 7 days`, or a custom local date/time range; filtered events match when their interval overlaps the selected availability interval.

Each card shows sport, title, local start/end time, and `accepted participants / participant limit`. Full events and the caller's own events remain visible but have no Join action. Existing requests display their `Pending`, `Accepted`, or `Rejected` state. A new join action stores one pending request, returns the same canonical request on retries, and updates the card without exposing any user contact field. If the event became unavailable after listing, the app explains why and refreshes the selected venue.

### Key Discoveries:

- `SportsEvent` already stores the complete listing data, but `CreatedEventResponse` currently hardcodes the organizer-only count in `server/Events/EventEndpoints.cs:196-216`.
- S-03 intentionally deferred the listing index until S-04's real query shape was known (`context/archive/2026-09-02-event-creation/plan.md`, Performance Considerations).
- Protected domain endpoints inherit authorization from the `/api` group in `server/Program.cs:123-127`; caller identity must continue to come from `HttpContext.GetUserId()`.
- The roadmap requires expiry at `EstimatedEndsAtUtc`, not `StartsAtUtc`; joining after an event starts remains valid until its estimated end (`context/foundation/prd.md`, FR-007).
- The current venue sheet is `app/ChoNaBojoApp/Views/MapPage.xaml:225-323`, and its state is owned by `MapViewModel.SelectVenue` / `DismissVenueSheet` in `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:183-191,293-306`.
- Shared wire types and pure validation belong in the dependency-free Contracts and Validation projects, while joinability, identity, accepted counts, and status transitions remain server-authoritative (`context/foundation/lessons.md`).
- There is no test project in `solutions/ChoNaBojo.slnx`; this plan keeps the approved S-03 verification model of builds, EF checks, API probes, and human Android/database checks.

## What We're NOT Doing

- No organizer accept/reject UI or endpoint; S-05 owns those operations.
- No auto-accept activation. Even when `SportsEvent.AutoAccept` is true, S-04 records `Pending`; S-05 routes it through the same capacity-safe acceptance path.
- No separate participant table. Accepted `EventJoinRequest` rows will be the participant source of truth, with the organizer counted implicitly as participant one.
- No contact fields in event-list or join-request contracts, responses, logs, or UI; contact reveal remains S-05.
- No push notifications; those belong to S-06.
- No cancellation, removal, leaving, event status, or background auto-close behavior; those belong to S-07.
- No event edit, event details fetched by id, user invitations, ratings, chat, iOS work, or dark theme.
- No pagination or infinite scrolling. Venue-level event volume is small for the Warsaw MVP; the query is indexed and ordered, and pagination can be introduced if measured data requires it.
- No new unit, integration, or UI test project in this change.
- No unrelated RF-1 migration of the existing create-event date/time fields. New custom-filter inputs follow the current UI guidelines without modifying the archived S-03 surface.

## Implementation Approach

Use one `EventJoinRequest` aggregate with a persisted `Pending`, `Accepted`, or `Rejected` status. A unique `(SportsEventId, RequesterUserId)` constraint makes the operation naturally idempotent: the first request returns `201 Created`, while every repeat returns the existing request as `200 OK`, regardless of later event expiry or capacity changes. This also establishes the state S-05 needs without creating a second participant model or a client request identifier.

Add `GET /api/venues/{venueId:int}/events` with optional `sportId`, `availableFromUtc`, and `availableToUtc` query values. Both availability bounds are absent for `Any time` or present together for a bounded search. The server validates zero UTC offsets and a strictly increasing range, always filters `EstimatedEndsAtUtc > now`, applies interval overlap as `StartsAtUtc < availableToUtc && EstimatedEndsAtUtc > availableFromUtc`, and projects safe DTOs directly from `AsNoTracking()` queries. Full and caller-related rows remain visible; expired rows never leave the API.

Replace the compact inline venue sheet with a dedicated modal `VenueEventsPage` so event discovery, filtering, and large-text layouts have the full screen available while the map remains preserved underneath. `MapViewModel` remains the owner of the selected venue, event list, availability choice, loading/error state, and join commands. It cancels obsolete event-list loads when the venue or filter changes. A toolbar `Filter` action reveals sport and availability controls, and a custom local-range editor reuses the existing DST-safe conversion rules inline on the page. The page uses one star-sized `CollectionView` row plus a sticky Create event row so the event list owns vertical scrolling without nested vertical scroll containers.

## Critical Implementation Details

### State sequencing

The join endpoint must look up `(SportsEventId, RequesterUserId)` before applying dynamic expiry, capacity, or organizer checks. An exact retry after response loss must return the stored request even if the event ended or filled after the original insert. For a new request, the server checks event existence, end time, organizer identity, and accepted capacity immediately before insertion; request creation itself does not reserve a place.

### Timing & lifecycle

Preset and custom windows are created in device-local time, reject invalid or ambiguous DST wall times, and are converted to zero-offset UTC before the API call. The API compares normalized UTC instants and uses one captured `nowUtc` per request so list inclusion and joinability do not drift during query evaluation.

### User experience spec

An event-list load is scoped to the selected venue and filter generation. Results from a canceled or superseded load must not overwrite a newer venue/filter selection. A stale join conflict first produces specific feedback and then reloads the authoritative list; a successful or replayed join updates the matching card to the returned request status and prevents another submission.

The dedicated venue-events modal keeps the map viewport and selection state alive beneath it. Its toolbar `Filter` action reveals wrapped sport and availability buttons only when needed. Filter button labels must autosize on Android and may use two lines so translated or system-scaled text remains within each button's border without reducing the 48-point touch target.

## Phase 1: Shared Discovery and Join Contracts

### Overview

Define dependency-free list, filter, status, join-response, and conflict contracts that contain all required UI state and no private contact data.

### Changes Required:

#### 1. Join request status

**File**: `shared/ChoNaBojo.Contracts/Enums/EventJoinRequestStatus.cs` (new)

**Intent**: Establish the persisted request lifecycle that S-04 creates and S-05 will transition.

**Contract**: Define stable numeric values for `Pending`, `Accepted`, and `Rejected`. Any server transition must reject undefined values before persistence, and Phase 2 mirrors the values in a database check constraint.

#### 2. Event listing and join DTOs

**File**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs`

**Intent**: Give the API and app one safe wire contract for venue event discovery, caller relationship state, and idempotent join results.

**Contract**: Add an event-list item with event id, title, optional description, UTC start/end, participant limit, accepted participant count including the organizer, auto-accept display value, sport summary, `IsOrganizer`, and nullable current-user request status. Add `JoinRequestResponse` with request id, event id, status, created UTC, and updated UTC. Do not add organizer/requester ids, login email, phone, contact email, communicator data, password/hash/token fields, or EF types.

#### 3. Availability query and validation

**File**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs`

**Intent**: Record the optional sport and availability query without coupling either application to ASP.NET query types.

**Contract**: Add a query record with nullable `SportId`, `AvailableFromUtc`, and `AvailableToUtc`. `Any time` is represented by both time values being absent; a bounded filter supplies both.

**File**: `shared/ChoNaBojo.Validation/EventListingValidation.cs` (new)

**Intent**: Validate the same query invariants in the client before sending and in the authoritative API before querying.

**Contract**: Validate a positive optional sport id, require availability bounds together, require zero UTC offsets, and require `AvailableFromUtc < AvailableToUtc`. Return lower-camel keys through the existing framework-neutral `ValidationResult`.

#### 4. Stable conflict vocabulary

**File**: `shared/ChoNaBojo.Contracts/Consts/EventConflictCodes.cs` (new)

**Intent**: Prevent string drift between endpoint classification and client recovery.

**Contract**: Define stable codes for `venue_not_found`, `sport_not_supported_at_venue`, `event_not_found`, `event_ended`, `event_full`, and `organizer_cannot_join`. Continue using `EventConflictResponse` as the single 409 body shape and `ValidationProblemResponse` as the single 400 body shape.

### Success Criteria:

#### Automated Verification:

- Shared contracts and validation compile: `dotnet build shared\ChoNaBojo.Validation\ChoNaBojo.Validation.csproj`
- Whole solution compiles against the new contracts: `dotnet build solutions\ChoNaBojo.slnx`
- Contract privacy scan is clean: `rg "Contact|LoginEmail|Communicator|Password|Hash|Token" shared\ChoNaBojo.Contracts\DTOs\EventDTOs.cs` returns no match

#### Manual Verification:

- DTO fields support every approved card state without exposing user identity or contact data
- Availability validation represents `Any time` and interval-overlap filtering without ambiguous half-open input

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation that the shared contracts are correct before proceeding.

---

## Phase 2: Join Request Persistence and Listing Indexes

### Overview

Add durable join-request state, database-level enum and uniqueness protection, accepted-count lookup support, and the indexes required by the venue event query.

### Changes Required:

#### 1. Join request entity and relationships

**File**: `server/Data/Entities/EventJoinRequest.cs` (new)

**Intent**: Persist one request per user and event as the future source of accepted-participant membership.

**Contract**: Add database-generated `Guid Id`; `Guid SportsEventId`; `Guid RequesterUserId`; `EventJoinRequestStatus Status`; required UTC `CreatedUtc`; nullable UTC `UpdatedUtc`; and navigations to `SportsEvent` and `User`. There is no client request id and no contact snapshot.

**File**: `server/Data/Entities/SportsEvent.cs`

**Intent**: Expose event requests for accepted-count projections and later approval transitions.

**Contract**: Add an `EventJoinRequests` collection navigation without changing organizer semantics.

**File**: `server/Data/Entities/User.cs`

**Intent**: Represent requests made by a user without introducing a global participant role.

**Contract**: Add an `EventJoinRequests` collection navigation separate from `OrganizedEvents`.

#### 2. EF Core configuration

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Register the request aggregate and enforce its invariants at rest.

**Contract**: Add `DbSet<EventJoinRequest>`. Configure database-generated id, required UTC timestamps, `DeleteBehavior.Cascade` on the `SportsEvent` relationship (a request is an owned child with no standalone meaning once its event is gone) and `DeleteBehavior.Restrict` on the `User` relationship (protects the accepted-membership history S-05 depends on), unique `(SportsEventId, RequesterUserId)`, an `(SportsEventId, Status)` accepted-count index, and an enum check constrained to the three defined numeric values. Add a `SportsEvents` listing index beginning with `VenueId` and `EstimatedEndsAtUtc`; the `(VenueId, SportId)` index already exists implicitly as the index backing the composite foreign key to `VenueSport` and needs no explicit declaration — if criterion 3.11's `EXPLAIN` shows a poor plan for the sport-filtered listing, add an explicit `(VenueId, SportId, EstimatedEndsAtUtc)` index rather than relying on PostgreSQL combining two indexes that share a leading column.

#### 3. Generated migration

**File**: `server/Migrations/<timestamp>_AddEventJoinRequests.cs`, `server/Migrations/<timestamp>_AddEventJoinRequests.Designer.cs`, `server/Migrations/ChoNaBojoContextModelSnapshot.cs`

**Intent**: Materialize the additive request table, relationships, checks, uniqueness, and listing indexes through the existing out-of-band migration workflow.

**Contract**: Generate `AddEventJoinRequests`. The application must not call `Database.Migrate()`. The migration contains no contact columns and performs no data backfill because the new table starts empty.

### Success Criteria:

#### Automated Verification:

- Solution builds with the persistence model: `dotnet build solutions\ChoNaBojo.slnx`
- EF resolves the context: `dotnet ef dbcontext info --project server`
- Migration is listed: `dotnet ef migrations list --project server` includes `AddEventJoinRequests`
- Model and snapshot agree: `dotnet ef migrations has-pending-model-changes --project server` exits successfully
- Idempotent migration SQL contains the request table, enum check, foreign keys, unique key, accepted-count index, and event-listing index: `dotnet ef migrations script --project server --idempotent`

#### Manual Verification:

- Apply the migration through the Supabase session-mode 5432 connection while runtime remains on transaction-mode 6543
- Direct inserts confirm undefined status values and duplicate event/requester pairs are rejected
- Schema inspection confirms no contact data is copied into `EventJoinRequests`

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation that the migration applied and the constraints are present before proceeding.

---

## Phase 3: Protected Event Listing and Join APIs

### Overview

Expose authenticated event discovery and naturally idempotent pending-request creation with server-authoritative filtering, count projection, and stale-state classification.

### Changes Required:

#### 1. Venue event listing endpoint

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Return the selected venue's current and upcoming events in one contact-free projection tailored to the caller.

**Contract**: Map `GET /venues/{venueId:int}/events` under the protected `/api` group. Accept the shared optional sport/availability query, normalize one `nowUtc`, run shared validation, verify the venue and requested venue/sport pairing, and return 400 or typed 409 on invalid/stale input. Query `AsNoTracking()`, require `EstimatedEndsAtUtc > nowUtc`, apply optional sport equality and strict interval overlap, order by `StartsAtUtc` then id, and project the safe list DTO. `ParticipantCount` is `1 + Accepted request count`; caller state comes only from the authenticated user. Full and caller-related events remain in the result; expired events do not.

#### 2. Idempotent join-request endpoint

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Record one pending request without allocating capacity or trusting client identity/state.

**Contract**: Map `POST /events/{eventId:guid}/join-requests` under the protected `/api` group with no request body. Derive the requester through `HttpContext.GetUserId()`. Look up an existing `(eventId, requesterId)` first and return it as `200 OK`. For a new request, load only the event state needed to reject missing, ended, organizer-owned, or full events with typed 409 codes; joining remains allowed after start until `EstimatedEndsAtUtc`. Insert `Pending` even when `AutoAccept` is true and return `201 Created`. Catch the unique-key race, detach/requery, and return the canonical row as `200 OK`; unrelated database failures are not converted into success.

#### 3. Safe projections and timestamp normalization

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Keep response construction and time handling consistent across create, list, and join operations.

**Contract**: Reuse or extract the existing UTC microsecond normalization helper for request timestamps. Project list rows in SQL rather than loading entity graphs. Update create-response participant count only if a shared projection is introduced; otherwise preserve S-03 behavior while list responses compute accepted counts independently.

### Success Criteria:

#### Automated Verification:

Probes P-01…P-07 are run against a locally started API (`dotnet run --project server`) using the PowerShell probe matrix in Testing Strategy → Integration Tests. No test project or checked-in probe file is added — automated tests arrive after roadmap implementation. If the API cannot be started against the development database in the executing environment, run the identical matrix in Postman and record the outcome at the phase's human gate instead.

- Server and solution build: `dotnet build solutions\ChoNaBojo.slnx`
- Route/auth static scan confirms both new endpoints are mapped on the protected `/api` group and neither opts out of authorization: `rg "MapGet|MapPost|AllowAnonymous|RequireAuthorization" server\Events\EventEndpoints.cs`
- Probe P-01: unauthenticated list and join requests return 401 and do not expose event data
- Probe P-02: listing returns only rows for the requested venue with `EstimatedEndsAtUtc > now`, in stable start/id order
- Probe P-03: sport and availability probes prove strict interval-overlap behavior, including boundary-touching non-overlap
- Probe P-04: full and organizer-owned events remain present with correct count/request state while expired events are absent
- Probe P-05: first valid join returns 201 Pending; repeating it returns 200 with the same request id and one database row
- Probe P-06: new joins after start but before estimated end succeed; ended, full, organizer-owned, missing-event, and stale venue/sport cases return the documented typed 409 codes
- Probe P-07: serialized list, 201, 200 replay, 400, and 409 bodies contain no contact/login/credential data

#### Manual Verification:

- Supabase inspection confirms JWT-derived requester ownership, UTC timestamps, and one row per event/requester
- `EXPLAIN` on unfiltered and sport-filtered venue listings uses the intended event/request indexes at representative development data volume

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation of request ownership, privacy, and query behavior before proceeding.

---

## Phase 4: Typed MAUI Event Data and State Flow

### Overview

Add client transport outcomes, availability conversion, cancellation-safe event loading, join state handling, and refresh integration while keeping raw HTTP concerns out of the UI.

### Changes Required:

#### 1. Typed client outcomes

**File**: `app/ChoNaBojoApp/Services/Events/EventResults.cs`

**Intent**: Represent list and join outcomes explicitly beside the existing create result.

**Contract**: Add a venue-event-list result covering success, validation failure, reference change, unauthorized, network/timeout, and unknown response. Add a join result covering success with replay flag, typed conflict, unauthorized, network/timeout, and unknown response. Raw `HttpResponseMessage` and untyped JSON do not leave `ApiService`.

#### 2. Event API calls

**File**: `app/ChoNaBojoApp/Services/IApiService.cs`

**Intent**: Expose cancellation-aware list and join operations through the established business API seam.

**Contract**: Add `GetVenueEventsAsync` with venue id and shared query, plus `RequestToJoinEventAsync` with event id.

**File**: `app/ChoNaBojoApp/Services/ApiService.cs`

**Intent**: Serialize query values in invariant UTC form and map all documented status/body combinations.

**Contract**: Map list 200/400/409/401 and join 200/201/409/401 to typed results. Transport failures and non-user timeouts map to network; malformed bodies map to unknown. Use the authenticated `ChoNaBojoApi` client. The bodyless POST remains compatible with transparent token refresh and one replay.

#### 3. Availability selection and local-time conversion

**File**: `app/ChoNaBojoApp/Services/Events/EventAvailabilityConversion.cs` (new)

**Intent**: Produce unambiguous UTC windows for presets and custom device-local input.

**Contract**: Define `Any time`, `Today`, `Tomorrow`, and `Next 7 days` from local calendar boundaries at selection time. Convert custom start/end values through `TimeZoneInfo.Local`, reject invalid or ambiguous DST wall times, require strict ordering, and return the same field keys used by shared validation.

#### 4. Venue event state

**File**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`

**Intent**: Keep selected-venue event state coherent across venue changes, filter changes, creation, joining, refreshes, and dismissal.

**Contract**: Add event-card view data, preset selection, custom-window state, list loading/empty/error properties, retry, filter, and join commands. Selecting a venue starts a load using the active map sport; changing venue/filter cancels the previous generation; dismissal cancels and clears venue-specific state. `MapViewModel` currently has no `CancellationTokenSource` of its own — it relies solely on the `[RelayCommand]`-supplied token — so introduce the supersession pattern already proven in `app/ChoNaBojoApp/ViewModels/AddressSearchViewModel.cs:21-22,115-165`: a disposable `_venueEventsCancellation` field replaced under `CreateLinkedTokenSource` on each new generation, plus a `_joinRequestCancellation` scoped to the in-flight join, both cancelled and disposed on venue change and sheet dismissal. Derived card actions show `Join`, `Full`, `Your event`, `Request pending`, `Joined`, or `Request rejected` with exactly one in-flight join per card. Success or replay replaces the matching card's request state. Typed stale conflicts show feedback and reload; unauthorized state follows the existing session-expiry path; network/unknown errors remain retryable without assuming the request failed.

#### 5. Create-to-list refresh

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml.cs`

**Intent**: Make a newly created event discoverable in the still-selected venue without reopening the sheet or synthesizing incomplete state.

**Contract**: After successful creation and detail dismissal, ask `MapViewModel` to reload the selected venue's list if the same venue remains selected. Preserve the existing `_hasAppliedMapRegion` guard so modal returns never recenter the map.

### Success Criteria:

#### Automated Verification:

- Whole solution builds: `dotnet build solutions\ChoNaBojo.slnx`
- Android head builds: `dotnet build app\ChoNaBojoApp -f net10.0-android`
- Client result handling covers every documented list/join HTTP status without anonymous error parsing
- Event client/state privacy scan is clean: `rg "Contact|LoginEmail|Communicator|Password|Hash|Token" app\ChoNaBojoApp\Services\Events app\ChoNaBojoApp\ViewModels\MapViewModel.cs` finds no event-list or join data binding

#### Manual Verification:

- Rapid venue/filter changes never allow an older response to replace the current selection
- Network, unknown-response, validation, stale-reference, and unauthorized outcomes produce distinct actionable states
- A successful creation reloads the still-selected venue without recentering or losing map context

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation of cancellation, retry, and refresh behavior before proceeding.

---

## Phase 5: Dedicated Venue Event Discovery UX

### Overview

Replace the static venue prompt with a dedicated, accessible event-discovery modal containing a scrollable event list, on-demand availability controls, complete screen states, and join feedback.

### Changes Required:

#### 1. Custom availability editor

**File**: `app/ChoNaBojoApp/ViewModels/AvailabilityFilterViewModel.cs` (new)

**Intent**: Own a custom local start/end draft and field-level conversion errors without bloating `MapViewModel` or the map page's visual state.

**Contract**: Prepare from the current custom range or sensible local defaults, expose editable local start/end date/time values, validate and return a UTC range, and support Apply/Clear/Cancel. Apply is disabled while invalid; Cancel does not change the active filter.

#### 2. Event cards and dedicated venue-events layout

**File**: `app/ChoNaBojoApp/Views/VenueEventsPage.xaml`, `app/ChoNaBojoApp/Views/VenueEventsPage.xaml.cs` (new)

**Intent**: Give event discovery a focused full-screen modal that is easier to browse and filter than the compact map sheet while preserving the map underneath.

**Contract**: Follow the existing modal `ShowAsync`/`TaskCompletionSource` handoff and dismiss venue-scoped state when the modal closes. Use a page grid with one `*` row for the vertical event `CollectionView` and one `Auto` row for the sticky Create event action, so the list receives a finite height and owns vertical scrolling. Put venue context and optional filters in the collection header. A toolbar `Filter` action reveals wrapped sport and availability buttons plus the inline custom date/time editor with field-level errors and accessible Apply/Clear/Cancel actions. On Android, filter buttons support two lines and uniform autosizing so large or long labels fit within their borders while retaining 48-point touch targets. Add an active custom-range summary, loading indicator, retryable error, and `No events here yet` empty state. Render each event as the MD3 card defined by `context/foundation/ui-guidelines.md`: title, sport, local times, people icon plus fill counter, optional description, and one state-specific Join button or disabled label. Full count uses `ErrorColor`; organizer, pending, accepted, and rejected states are explicit.

#### 3. Map handoff and child-modal orchestration

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml`, `app/ChoNaBojoApp/Views/MapPage.xaml.cs`

**Intent**: Open the dedicated venue-events modal from a map marker while keeping navigation out of `MapViewModel` and preserving the current map viewport.

**Contract**: Remove the compact inline venue sheet, resolve and await `VenueEventsPage` when a marker is selected, and guard against duplicate modal opens. Keep `_hasAppliedMapRegion` unchanged so returning from venue, create, detail, and filter interactions never recenters the map.

**File**: `app/ChoNaBojoApp/Views/VenueEventsPage.xaml.cs`

**Intent**: Coordinate create-event and event-detail child modals without losing the selected venue or active event filters.

**Contract**: Subscribe to the existing `MapViewModel` create and custom-availability events while the page is active. After successful creation and detail dismissal, reload the same selected venue. Preserve the parent modal while child modals are open, close it if the selected venue is invalidated, and prevent duplicate child-modal opens.

**File**: `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Make the dedicated venue-events page and custom-filter ViewModel resolvable using existing transient page/ViewModel lifetimes.

**Contract**: Register `VenueEventsPage` and `AvailabilityFilterViewModel` as transient services; retain singleton API and venue catalog lifetimes and add no packages.

### Success Criteria:

#### Automated Verification:

- Whole solution builds: `dotnet build solutions\ChoNaBojo.slnx`
- Android head builds: `dotnet build app\ChoNaBojoApp -f net10.0-android`
- XAML scan confirms the event `CollectionView` is not nested in a vertical `ScrollView` and is height-bounded by occupying the dedicated page grid's only `*` row, with Create event in the separate `Auto` row
- UI privacy scan finds no contact binding: `rg "Contact|LoginEmail|Communicator|Password|Hash|Token" app\ChoNaBojoApp\Views\MapPage.xaml app\ChoNaBojoApp\Views\VenueEventsPage.xaml` returns no match

#### Manual Verification:

- Selecting a venue opens the dedicated venue-events modal and shows ordered non-expired event cards, or the loading, empty, and retryable error states as appropriate
- The active map sport narrows events; `Any time`, preset, custom, and cleared availability filters return the expected overlap results
- Full, organizer-owned, pending, accepted, and rejected cards remain visible with the correct disabled state and accessibility description; accepted and rejected are unreachable through S-04 code paths and must be exercised by seeding `EventJoinRequests.Status` in Supabase (Manual Testing Step 9)
- Join submits once, shows in-flight feedback, ends as Pending even for auto-accept events, and displays the same state after closing and reopening the venue
- Duplicate/replayed join is treated as success; an ended/full/stale event explains the conflict and refreshes the list
- Event cards and filter controls remain usable with large Android font scaling and meet 48-point touch targets; filter labels autosize or wrap within their button borders instead of clipping
- Create event still opens from the dedicated venue-events modal, and returning from venue/create/detail/filter interactions preserves the map viewport and selected venue

**Implementation Note**: After completing this phase and all automated verification passes, pause for final human confirmation of the Android event-discovery and join-request flow.

---

## Testing Strategy

### Unit Tests:

- No test project is added, per the approved scope; automated tests are introduced after roadmap implementation.
- Shared availability validation remains pure and clock-independent so it can be covered without production refactoring when test infrastructure is introduced.
- Joinability remains server-authoritative and is exercised through the API probe matrix rather than duplicated as client-only validation.

### Integration Tests:

No harness exists yet, so Phase 3 is verified by an explicit probe matrix run against a locally started API. Every probe below is a single HTTP call and a stated expectation — runnable verbatim from PowerShell (`Invoke-RestMethod` / `Invoke-WebRequest`) by the implementer, or imported into Postman when the API cannot be started locally. Nothing is added to `server/server.http`; that file stays a minimal smoke artifact.

**Setup**

1. Start the API: `dotnet run --project server` (base URL `http://localhost:5100`).
2. Register and log in **two** users via `POST /auth/register` then `POST /auth/login` with body `{ "loginEmail": "...", "password": "..." }`; capture `accessToken` from each `AuthResponse` as `$organizerToken` (event owner) and `$requesterToken` (joiner).
3. Seed fixtures as the organizer via `POST /api/events`: one normal upcoming event, one `autoAccept` event, one event already started but not ended, one event whose `estimatedEndsAtUtc` is in the past, and one event whose participant limit is already reached. Record `$venueId`, `$sportId`, and each `$eventId`.
4. Every `/api/**` call carries `Authorization: Bearer <token>`; `/auth/**` calls carry none.

**Probe matrix**

| Probe | Request | Expectation |
|---|---|---|
| P-01 | `GET /api/venues/{venueId}/events` and `POST /api/events/{eventId}/join-requests`, both with **no** `Authorization` header | `401`; body carries no event fields |
| P-02 | `GET /api/venues/{venueId}/events` as requester | Only that venue's rows; every `estimatedEndsAtUtc > now`; expired fixture absent; order is `startsAtUtc` then event id |
| P-03 | `GET /api/venues/{venueId}/events?sportId={sportId}`; then `?availableFromUtc=<A>&availableToUtc=<B>`; then a window whose `availableFromUtc` equals a fixture's `estimatedEndsAtUtc`; then only one bound supplied | Sport filter narrows correctly; overlap is strict so the boundary-touching fixture is **absent**; omitting both bounds means `Any time`; one-sided bounds return `400` with lower-camel validation keys |
| P-04 | `GET /api/venues/{venueId}/events` as organizer, then as requester | Full and organizer-owned events still present; `participantCount` equals `1 + accepted`; `isOrganizer` true only for the organizer; `currentUserRequestStatus` reflects the calling token only |
| P-05 | `POST /api/events/{normalEventId}/join-requests` as requester, then the identical call again; repeat both against the `autoAccept` event | First → `201` with `status: Pending`; replay → `200` with the same `requestId`; auto-accept event also yields `Pending`; Supabase shows exactly one row per `(eventId, requesterId)` |
| P-06 | Join the started-but-not-ended event; the expired event; the full event; the organizer's own event as organizer; a random `Guid`; and a `venueId`/`sportId` pairing that does not exist | `201`; then `409` `event_ended`, `event_full`, `organizer_cannot_join`, `event_not_found`, and `venue_not_found` / `sport_not_supported_at_venue` respectively, each in the `EventConflictResponse` shape |
| P-07 | Re-read the raw JSON of every response produced by P-01…P-06 | No `contact*`, `loginEmail`, `communicator*`, `password`, `hash`, or `token` field appears in any list, `201`, `200` replay, `400`, or `409` body |

Additionally verify migration/model state and inspect query plans for both unfiltered and sport-filtered venue lists (criteria 3.10–3.11).

### Manual Testing Steps:

1. Apply `AddEventJoinRequests` through the Supabase session-mode connection and inspect constraints/indexes.
2. Create multiple events at one venue across sports, times, ownership, and capacity states.
3. Open the dedicated venue-events modal on Android and verify stable ordering plus loading, empty, error, and full-card presentation.
4. Exercise `Any time`, Today, Tomorrow, Next 7 days, and custom overlap windows, including DST-invalid and DST-ambiguous local input.
5. Join a normal and auto-accept event; both become Pending and persist after reopening the venue.
6. Double-tap Join and simulate response loss; confirm one row and canonical replay success.
7. Let an event end or make it full between list and join; confirm specific feedback followed by an authoritative refresh.
8. Verify organizer-owned events remain visible as `Your event` and cannot be joined.
9. Directly set `EventJoinRequests.Status` in Supabase to `Accepted` and then `Rejected` for the requester's row, reopen the venue, and confirm the `Joined` and `Request rejected` card states render with the correct disabled state and accessibility description. S-04 has no code path that produces a non-`Pending` row, so this seeded check is the only S-04 exercise of those two states.
10. Inspect all event-list and join responses/screens for absence of contact, login, communicator, credential, and token data.
11. Increase Android font size and display size, then confirm event controls retain 48-point touch targets and every sport/availability filter label autosizes or wraps within its button border without clipping.
12. Create an event from the venue-events modal and confirm returning refreshes events without recentering the map.

## Performance Considerations

The mandatory venue predicate, non-expiry predicate, and new listing index bound each read to one venue's current/future rows. The optional sport filter is served by the index PostgreSQL generates automatically behind the composite foreign key to `VenueSport` — it is not an explicitly declared index, and PostgreSQL will normally pick a single index rather than bitmap-combine two that share `VenueId` as a leading column. If criterion 3.11's `EXPLAIN` shows a poor plan for the sport-filtered listing, add an explicit `(VenueId, SportId, EstimatedEndsAtUtc)` index. `(SportsEventId, Status)` supports accepted counts. The list projects directly to DTOs with `AsNoTracking()` and does not load organizer or requester entities. Pagination is deferred for the small Warsaw MVP dataset; query-plan verification is required before accepting the migration.

Client loads are generation/cancellation scoped so rapid marker or filter changes do not accumulate obsolete work or flash stale results. No cross-venue event cache is introduced; a selected venue is cheap to reload after create or stale join outcomes.

## Migration Notes

`AddEventJoinRequests` is additive and starts with an empty table. Generate and inspect it locally, then apply it explicitly with `dotnet ef database update --project server --connection "<AppDbMigrations session-mode connection>"`; never migrate on application startup or through the transaction-mode runtime pooler.

Before S-05 stores accepted/rejected states, rollback may drop `EventJoinRequests` and the added listing index. After S-05 depends on the table, rollback must be a forward fix or include an explicit data backup; accepted membership cannot be reconstructed from `SportsEvents`.

## References

- Roadmap S-04: `context/foundation/roadmap.md`
- Product behavior and access control: `context/foundation/prd.md`
- Shared-code, enum, and API error rules: `context/foundation/lessons.md`
- Event-card, venue-sheet, screen-state, and accessibility rules: `context/foundation/ui-guidelines.md`
- Prior event foundation: `context/archive/2026-09-02-event-creation/plan.md`
- Existing event entity/configuration: `server/Data/Entities/SportsEvent.cs`, `server/Data/ChoNaBojoContext.cs:181-271`
- Existing authenticated event endpoint: `server/Events/EventEndpoints.cs`
- Existing map marker handoff and event state: `app/ChoNaBojoApp/Views/MapPage.xaml.cs`, `app/ChoNaBojoApp/Views/VenueEventsPage.xaml`, `app/ChoNaBojoApp/Views/VenueEventsPage.xaml.cs`, `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`
- Existing typed client pattern: `app/ChoNaBojoApp/Services/Events/EventResults.cs`, `app/ChoNaBojoApp/Services/ApiService.cs`
- Existing local-time conversion: `app/ChoNaBojoApp/Services/Events/EventTimeConversion.cs`
- Existing modal handoff: `app/ChoNaBojoApp/Views/CreateEventPage.xaml.cs`
- Planning progress contract: `.github/skills/10x-plan/references/progress-format.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared Discovery and Join Contracts

#### Automated

- [x] 1.1 Shared contracts and validation compile — 057eb95
- [x] 1.2 Whole solution compiles against the new contracts — 057eb95
- [x] 1.3 Contract privacy scan is clean — 057eb95

#### Manual

- [x] 1.4 DTO fields support every approved card state without private data — 057eb95
- [x] 1.5 Availability validation represents Any time and overlap filtering unambiguously — 057eb95

### Phase 2: Join Request Persistence and Listing Indexes

#### Automated

- [x] 2.1 Solution builds with the persistence model — 62e196e
- [x] 2.2 EF resolves the context — 62e196e
- [x] 2.3 AddEventJoinRequests migration is listed — 62e196e
- [x] 2.4 Model and snapshot agree — 62e196e
- [x] 2.5 Idempotent migration SQL contains all required constraints and indexes — 62e196e

#### Manual

- [x] 2.6 Migration is applied through session mode while runtime remains on transaction mode — 62e196e
- [x] 2.7 Undefined statuses and duplicate event/requester rows are rejected — 62e196e
- [x] 2.8 EventJoinRequests contains no copied contact data — 62e196e

### Phase 3: Protected Event Listing and Join APIs

#### Automated

- [x] 3.1 Server and solution build — acaeb9f
- [x] 3.2 Route/auth static scan on EventEndpoints.cs is clean — acaeb9f
- [x] 3.3 P-01 unauthenticated list and join requests return 401 — acaeb9f
- [x] 3.4 P-02 listing scope, expiry, and stable ordering probes pass — acaeb9f
- [x] 3.5 P-03 sport and interval-overlap probes pass — acaeb9f
- [x] 3.6 P-04 full and caller-related visibility and participant counts are correct — acaeb9f
- [x] 3.7 P-05 first join and idempotent replay return one canonical request — acaeb9f
- [x] 3.8 P-06 join timing and typed conflict probes pass — acaeb9f
- [x] 3.9 P-07 serialized API bodies pass the privacy scan — acaeb9f

#### Manual

- [x] 3.10 Supabase confirms JWT ownership, UTC timestamps, and request uniqueness — acaeb9f
- [x] 3.11 Representative listing queries use the intended indexes — acaeb9f

### Phase 4: Typed MAUI Event Data and State Flow

#### Automated

- [x] 4.1 Whole solution builds — 62a937b
- [x] 4.2 Android head builds — 62a937b
- [x] 4.3 Client maps every documented list and join outcome — 62a937b
- [x] 4.4 Event client and state privacy scan is clean — 62a937b

#### Manual

- [x] 4.5 Superseded venue and filter loads cannot overwrite current state — 62a937b
- [x] 4.6 Failure classes produce distinct actionable states — 62a937b
- [x] 4.7 Successful creation refreshes the selected venue without losing map context — 62a937b

### Phase 5: Dedicated Venue Event Discovery UX

#### Automated

- [x] 5.1 Whole solution builds
- [x] 5.2 Android head builds
- [x] 5.3 Event CollectionView is not nested in the previous venue-sheet ScrollView and is height-bounded
- [x] 5.4 Event-list and availability XAML privacy scan is clean

#### Manual

- [x] 5.5 Venue event loading, ordering, empty, and error states work on Android
- [x] 5.6 Sport, preset, custom, and cleared availability filters produce expected results
- [x] 5.7 Full and caller-related event cards show the correct disabled states, including Supabase-seeded accepted and rejected
- [x] 5.8 Normal and auto-accept joins persist as Pending
- [x] 5.9 Duplicate and stale join outcomes reconcile correctly
- [x] 5.10 Event controls remain accessible with large text and 48-point touch targets
- [x] 5.11 Create and filter modal returns preserve the map viewport and selected venue
