# Event Creation Implementation Plan

## Overview

Implement roadmap slice S-03 as an authenticated end-to-end flow. From the selected venue on the map, an organizer creates a titled sports event with an optional description, a device-local start and estimated end time, a participant limit from 2 through 300, and an optional auto-accept setting. The API derives organizer identity from the JWT, validates and stores UTC instants, guarantees retry-safe creation, and returns enough contact-free data to open a read-only event detail screen immediately.

## Current State Analysis

The authenticated map and venue catalog are already complete. `MapViewModel` retains the selected `VenueResponse` and resolves its supported sports, while `MapPage.xaml` contains the disabled S-03 "Create event" affordance. The protected `/api` route group, canonical JWT user-id extraction, shared DTO/validation projects, typed MAUI API results, session-scoped venue catalog, feedback service, and transparent token refresh are all established.

No event DTO, validator, entity, migration, endpoint, client result, form, detail screen, or automated test project exists. This change is therefore the first domain write slice and the first runtime exercise of a protected POST body through the refresh-and-replay handler.

## Desired End State

A logged-in user can select a venue, open an event form, choose only a sport supported by that venue, enter a required title and optional description, select local start/end values including an overnight end, set capacity and auto-accept, and submit once. The UI immediately shows an explicit creating/waiting state, preserves the draft through recoverable failures, and opens a read-only detail view within a normal-response target of five seconds.

The server stores one `SportsEvent` with UTC timestamps and the authenticated user as organizer. Database constraints protect capacity, title, duration, and venue/sport integrity. Repeating an uncertain submission with the same request identifier returns the original event rather than creating a duplicate. Neither the create response nor the detail screen contains user contact information.

### Key Discoveries:

- The selected venue and its supported sport ids already live in `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:104-113,261-273`; the disabled entry point is `app/ChoNaBojoApp/Views/MapPage.xaml:304-323`.
- Shared wire contracts and pure validators must remain in dependency-free projects, while organizer identity, venue/sport checks, and persistence stay server-only (`context/foundation/lessons.md`).
- Protected domain endpoints inherit authentication from `server/Program.cs:123-125`, and organizer identity must use `server/Auth/CurrentUser.cs:12-32`.
- `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:51-100,193-210` buffers POST content for a single refresh retry, but event creation is the first feature that must verify this path with a real body.
- The venue catalog is loaded once behind a semaphore and has no refresh contract (`app/ChoNaBojoApp/Services/Venues/VenueCatalog.cs:46-97`); stale venue/sport recovery requires an explicit atomic refresh path.
- Existing PostgreSQL timestamps use `timestamp with time zone` and UTC `DateTime` values (`server/Migrations/20260716215146_AddAuthTables.cs:19-51`).
- There is no test project in `solutions/ChoNaBojo.slnx`; this plan uses agent-executable builds, EF checks, and API probes plus human UI/database verification, without adding test automation infrastructure.

## What We're NOT Doing

- No event listing, availability filter, or join request; those belong to S-04.
- No join-request, participant, or accepted-participant persistence.
- No contact reveal or contact fields in any event contract or UI.
- No push notifications; those belong to S-06.
- No event edit, cancel, remove-participant, leave-event, status enum, or background auto-close behavior; those belong to S-07.
- No overlap prevention or venue reservation logic; overlapping events are allowed.
- No general `GET /api/events/{id}` endpoint, deep link, or process-death restoration for the detail screen. S-03 renders the detail from the create response.
- No event list or synthetic persisted-event cache in the venue sheet.
- No Android UI automation, unit-test project, integration-test project, or new testing package in this change.
- No iOS targets, dark theme, new observability platform, or unrelated auth/session changes.

## Implementation Approach

Build bottom-up in five independently verifiable phases. First establish one shared contract and pure rule set. Then add a `SportsEvent` entity whose database constraints mirror the application rules and whose composite venue/sport foreign key prevents unsupported pairings. Add a thin authenticated minimal-API endpoint with persisted client request ids for idempotency. Finally wire the existing map affordance to a MAUI form, typed API result, catalog refresh path, and response-backed detail modal.

The organizer counts implicitly as participant one; S-03 does not create a participant row. The create response reports `ParticipantCount = 1`, allowing the detail UI to show capacity without pre-creating S-04/S-05 state.

## Critical Implementation Details

### Timing & lifecycle

The client combines the selected date and times in `TimeZoneInfo.Local`, rejects invalid or ambiguous DST wall times, converts valid values to UTC, and sends zero-offset `DateTimeOffset` values. An end clock value less than or equal to the start clock means the next local day; equal clock values represent 24 hours and remain valid only when the elapsed UTC duration is no more than 24 hours.

### State sequencing

The endpoint must look up `(OrganizerUserId, ClientRequestId)` before applying the dynamic "start is not in the past" rule. Otherwise an exact retry could fail merely because time advanced after the original event was created. A request id that already exists always returns the stored event; the payload is not re-compared, because the client mints one id per draft and Retry resends only the captured snapshot, so a same-key/different-payload request is unreachable from the app. On the client, an uncertain transport failure preserves both the normalized request snapshot and request id; Retry resends that exact request rather than rebuilding it from editable fields.

### User experience spec

Five seconds is a normal-response target, not a destructive client timeout. The form disables inputs and navigation immediately, shows "Creating event...", and changes to a longer-wait message after five seconds while the same request continues; a completed failure always restores an actionable state without silently retrying.

## Phase 1: Shared Event Contract and Validation

### Overview

Define the event creation wire shape, domain constants, stable error keys, response detail projection, and framework-neutral validation shared by the API and MAUI form.

### Changes Required:

#### 1. Event policy constants

**File**: `shared/ChoNaBojo.Contracts/Consts/EventPolicy.cs` (new)

**Intent**: Centralize event creation limits so the app, API, and database configuration cannot drift.

**Contract**: Expose title maximum 100 characters, description maximum 1000 characters, participant minimum 2, participant maximum 300, maximum elapsed duration 24 hours, and the two-minute server clock-skew tolerance used by the "now or future" start rule.

#### 2. Event request, response, and conflict DTOs

**File**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs` (new)

**Intent**: Give the app and server one dependency-free contract for creation, replay, stale-reference recovery, and the response-backed detail screen.

**Contract**: Add `CreateEventRequest` with `ClientRequestId`, `VenueId`, `SportId`, `Title`, optional `Description`, zero-offset `StartsAtUtc`, zero-offset `EstimatedEndsAtUtc`, `ParticipantLimit`, and `AutoAccept`. Add `CreatedEventResponse` containing the event id, normalized title/description, UTC start/end/created timestamps, limit, participant count, auto-accept, and safe venue/sport summaries. Add `EventConflictResponse` with stable `Code`, `Field`, and `Message` members. No DTO may contain organizer contact, login email, communicator, password, hash, JWT, or EF entity types.

#### 3. Pure event validation

**File**: `shared/ChoNaBojo.Validation/EventValidation.cs` (new)

**Intent**: Enforce deterministic creation rules in both the form and authoritative API while returning the existing framework-neutral `ValidationResult`.

**Contract**: Validate non-empty `ClientRequestId`; positive venue/sport ids; required trimmed title and agreed text limits; optional description length; zero UTC offsets; participant range 2-300; strict end-after-start; elapsed duration no greater than 24 hours; and start no earlier than the supplied `nowUtc` minus the policy tolerance. Use stable lower-camel error keys matching the request properties. Venue existence, venue/sport support, organizer identity, local DST conversion, and idempotency lookup remain outside this pure validator.

### Success Criteria:

#### Automated Verification:

- Shared contracts and validation compile: `dotnet build shared\ChoNaBojo.Validation\ChoNaBojo.Validation.csproj`
- Whole solution compiles against the new contract: `dotnet build solutions\ChoNaBojo.slnx`
- Contract privacy scan covers the whole shared DTO folder, not just the new file: `rg "Contact|LoginEmail|Communicator|Password|Hash|Token" shared\ChoNaBojo.Contracts\DTOs` returns no match in any type reachable from the event contracts

#### Manual Verification:

- Request fields, limits, stable error keys, UTC requirement, and response summaries match the approved product decisions
- `CreatedEventResponse` contains everything the read-only detail screen needs and no private user data

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation that the shared contract is correct before proceeding.

---

## Phase 2: Sports Event Persistence and Migration

### Overview

Add durable event storage with database-level protection for organizer ownership, supported venue/sport pairs, idempotency, text limits, capacity, and time range.

### Changes Required:

#### 1. Sports event entity

**File**: `server/Data/Entities/SportsEvent.cs` (new)

**Intent**: Persist the S-03 event without introducing participant or lifecycle state owned by later slices.

**Contract**: Add database-generated `Guid Id`; `Guid OrganizerUserId`; `Guid ClientRequestId`; `int VenueId`; `int SportId`; required `Title`; optional `Description`; UTC `DateTime StartsAtUtc`, `EstimatedEndsAtUtc`, and `CreatedUtc`; `ParticipantLimit`; and `AutoAccept`. Navigation may link to `User` and the composite `VenueSport`; it must not expose persistence entities through shared contracts.

#### 2. Event relationships

**File**: `server/Data/Entities/User.cs`

**Intent**: Represent the user's organized events in the persistence model without adding a global organizer role.

**Contract**: Add the organized-events navigation collection only; per-event organizer authority continues to derive from `SportsEvent.OrganizerUserId`.

**File**: `server/Data/Entities/VenueSport.cs`

**Intent**: Provide the principal navigation for events associated with one supported venue/sport pair.

**Contract**: Add the sports-events navigation collection while retaining the existing composite key.

#### 3. DbContext event configuration

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Register and constrain `SportsEvent` consistently with the existing EF Core model.

**Contract**: Add `DbSet<SportsEvent> SportsEvents`. Map to `SportsEvents`; generate `Id` with `gen_random_uuid()`; require and size title/description; require UTC timestamp columns; use restrictive organizer and composite `(VenueId, SportId)` relationships; add the unique `(OrganizerUserId, ClientRequestId)` index. Do not add a `(VenueId, EstimatedEndsAtUtc)` lookup index — S-03 issues no listing query, and S-04 also filters by sport and availability, so it must add the index its actual query shape needs. Add checks for a non-empty trimmed title, null-or-non-empty trimmed description, participant range, end after start, elapsed duration at most 24 hours, and non-empty client request id. Do not add `Status` or an overlap constraint.

#### 4. Generated migration

**File**: `server/Migrations/<timestamp>_AddSportsEvents.cs`, `server/Migrations/<timestamp>_AddSportsEvents.Designer.cs`, `server/Migrations/ChoNaBojoContextModelSnapshot.cs`

**Intent**: Materialize the event table and all constraints through the repository's out-of-band EF migration workflow.

**Contract**: Generate `AddSportsEvents`. The migration creates the `SportsEvents` table, organizer and composite venue/sport foreign keys with restrictive deletion, all application-mirroring checks, and the unique replay index. The application must not call `Database.Migrate()`.

**Reference-data operational contract**: `VenueSport` already cascades from both `Venue` (`ChoNaBojoContext.cs:107-110`) and `Sport` (`:112-115`), and `Sport` rows are seeded with `HasData` (`:73`). Adding a RESTRICT edge from `SportsEvents` to `VenueSport` therefore means that once any event row exists, deleting a `Venue` or a `Sport` cascades into `VenueSport` and is rejected, failing the entire statement — including an EF-generated `DeleteData` produced by editing the `HasData` sport list. RESTRICT is still the correct choice, because cascading a venue deletion into users' events would silently destroy their data. The consequence must be treated as an explicit rule: **once events exist, venue/sport reference data is append-only** — corrections are `UPDATE`s (rename, re-address, re-map), never deletes; the seeded sport list may gain rows but must not lose or renumber them; and manual Warsaw venue uploads must add rather than replace. Retiring a sport or venue (or removing a `VenueSport` pairing) is out of scope for S-03 and needs its own slice with a deactivation flag and a story for the events already attached.

### Success Criteria:

#### Automated Verification:

- Solution builds with the event model: `dotnet build solutions\ChoNaBojo.slnx`
- EF resolves the context: `dotnet ef dbcontext info --project server`
- Migration is generated and listed: `dotnet ef migrations list --project server` includes `AddSportsEvents`
- Model and snapshot agree: `dotnet ef migrations has-pending-model-changes --project server` exits successfully
- Idempotent SQL script contains the table, checks, foreign keys, and indexes: `dotnet ef migrations script 20260902012920_TranslateSportNamesToEnglish --project server --idempotent`

#### Manual Verification:

- Apply the migration through the Supabase session-mode 5432 connection and confirm the application still uses transaction-mode 6543 at runtime
- Supabase schema inspection confirms the organizer FK, composite venue/sport FK, text/capacity/time/request-id checks, and the unique replay index
- Direct invalid inserts are rejected for unsupported venue/sport, blank title, participant limits outside 2-300, non-positive duration, and duration over 24 hours

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation that the migration applied and the database constraints are present before proceeding.

---

## Phase 3: Authenticated Idempotent Create API

### Overview

Expose `POST /api/events` as an authenticated minimal API operation that creates one event, reconstructs exact replays, and reports actionable validation/reference conflicts.

### Changes Required:

#### 1. Event endpoint

**File**: `server/Events/EventEndpoints.cs` (new)

**Intent**: Own authoritative event creation, organizer assignment, stale-reference classification, idempotency, persistence, and safe response projection.

**Contract**: Add `MapEventEndpoints` and `POST /events` under the protected `/api` group. Derive `OrganizerUserId` only through `HttpContext.GetUserId()`. Normalize title and description before persistence. Query the organizer/request-id pair before dynamic time validation: if the pair already exists, return `200 OK` with the stored event's response, without comparing the incoming payload. For a new key, run shared validation, resolve the exact `VenueSport` pair and safe venue/sport summary, save the event, and return `201 Created` with no contact data. There is no changed-payload conflict code: standard idempotency-key semantics apply — same key, same event.

**Canonical timestamp normalization**: Every incoming `DateTimeOffset` must pass through one shared conversion helper before it is persisted. The helper must (a) use `.UtcDateTime` — never `.DateTime`, which yields `Kind=Unspecified` and is rejected by Npgsql for `timestamptz` — and (b) truncate to microsecond resolution, because PostgreSQL `timestamptz` stores microseconds while .NET ticks are 100ns, so a value carrying a non-zero seventh fractional digit would otherwise be silently truncated on write and read back differently from what the client sent. There is no UTC value converter in `ChoNaBojoContext` today (the codebase relies on `DateTime.UtcNow` at call sites, e.g. `AuthEndpoints.cs:70-81`, `RefreshTokenService.cs:67-80`), so this helper is the single normalization path for event timestamps.

#### 2. Reference-change and database race handling

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Turn stale catalog data and concurrent duplicate submissions into stable client outcomes instead of unhandled EF/PostgreSQL failures.

**Contract**: Return typed `409 Conflict` codes for `venue_not_found`, `sport_not_found`, `sport_not_supported_at_venue`, and `reference_data_changed`, with `venueId` or `sportId` as the affected field. Catch only the relevant unique-constraint race, detach/requery by organizer/request id, and return the stored event as a `200 OK` replay. Map a relevant foreign-key race to `reference_data_changed`; propagate unrelated database failures.

#### 3. Endpoint registration

**File**: `server/Program.cs`

**Intent**: Register event routes on the existing authenticated domain group.

**Contract**: Import the events namespace and call `apiGroup.MapEventEndpoints()` beside `MapVenueEndpoints()`. Do not add anonymous overrides or duplicate authorization declarations.

### Success Criteria:

#### Automated Verification:

- Server and solution build: `dotnet build solutions\ChoNaBojo.slnx`
- API is started in the background and confirmed listening on `http://localhost:5100` (`dotnet run --project server` blocks, so it must not be run as a foreground verification step); it stays running for the remaining checks, which authenticate with a JWT obtained from `/auth/login`
- Unauthenticated `POST /api/events` returns 401 and creates no row
- A valid authenticated request returns 201 with UTC timestamps, participant count 1, safe venue/sport summaries, and no contact fields
- Repeating the request id returns 200 with the same event id and only one database row, whether or not the payload is byte-identical, and leaves the stored event unchanged
- Invalid title, description, ids, participant boundaries, past start, end ordering, and duration return the agreed 400 validation keys; stale venue/sport cases return the agreed 409 codes without creating a row
- A normal valid create call completes within five seconds when measured from the local API against the configured development database
- Sending start/end timestamps with sub-microsecond precision stores and returns them consistently, and replaying that request id returns 200 with the same event id

#### Manual Verification:

- Supabase row inspection confirms the organizer id came from the JWT, normalized text was stored, and all timestamps are UTC instants
- API response inspection confirms no login or contact data is emitted on first creation or replay — the captured serialized 201 and 200 bodies are scanned in full, since the source-file greps cannot see nested types declared elsewhere and are a lint, not the privacy check

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation of persisted ownership and privacy before proceeding.

---

## Phase 4: MAUI Create Form and Recovery Flow

### Overview

Enable event creation from the selected venue and add the typed client, local-time conversion, form validation, explicit wait state, replay-safe retry, and stale-catalog recovery.

### Changes Required:

#### 1. Typed event API result

**File**: `app/ChoNaBojoApp/Services/Events/EventResults.cs` (new)

**Intent**: Represent every create outcome explicitly so the ViewModel never receives a raw HTTP response.

**Contract**: Model success (including whether the response was a replay), validation errors, reference changes with code/field/message, unauthorized, network/timeout, and unknown failures. Carry `CreatedEventResponse` only on success. There is no idempotency-key conflict case — a reused request id resolves to a 200 replay success.

#### 2. Event client call

**File**: `app/ChoNaBojoApp/Services/IApiService.cs`

**Intent**: Add event creation to the authenticated business API seam.

**Contract**: Add a cancellation-aware `CreateEventAsync(CreateEventRequest, CancellationToken)` returning the typed event result.

**File**: `app/ChoNaBojoApp/Services/ApiService.cs`

**Intent**: Send the protected JSON POST and map all documented statuses/bodies without leaking transport concerns to the form.

**Contract**: Map 200/201 to success, 400 RFC-7807 errors to validation, 409 `EventConflictResponse` to reference-change outcomes, 401 to unauthorized, transport or non-user cancellation to network, and malformed/unsupported JSON to unknown. Use the existing authenticated `ChoNaBojoApi` client so token refresh and buffered body replay remain active.

#### 3. Refreshable venue catalog

**File**: `app/ChoNaBojoApp/Services/Venues/IVenueCatalog.cs`

**Intent**: Expose an explicit recovery operation when event creation reports stale venue/sport data.

**Contract**: Add cancellation-aware refresh semantics that reload venues and sports together.

**File**: `app/ChoNaBojoApp/Services/Venues/VenueCatalog.cs`

**Intent**: Refresh reference data without exposing partial replacement state or issuing concurrent duplicate loads.

**Contract**: Reuse the existing semaphore, fetch both resources concurrently, and replace both cached collections only after both succeed. Keep the last successful catalog on refresh failure. Return the established load status so the form can distinguish unauthorized, network, and unknown outcomes.

#### 4. Device-local time conversion

**File**: `app/ChoNaBojoApp/Services/Events/EventTimeConversion.cs` (new)

**Intent**: Convert user-entered local date/time values into the UTC wire contract without silently guessing through DST transitions.

**Contract**: Combine date/start/end as unspecified local wall times; roll end to the next day when its clock value is less than or equal to start; reject `TimeZoneInfo.Local` invalid or ambiguous times; convert through the device time zone to zero-offset UTC values; and enforce positive elapsed duration no greater than 24 hours before request creation. Return field-specific errors rather than throwing for expected invalid input.

#### 5. Create event ViewModel

**File**: `app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs` (new)

**Intent**: Own the draft, supported-sport choices, shared validation, request identity/snapshot, create command, busy/long-wait states, recovery, and success/cancel events.

**Contract**: Prepare from one selected `VenueResponse` plus the current catalog and optional active map sport. Require title, keep description optional, expose date/start/end, 2-300 participant limit, and auto-accept. Generate one request id per draft. Preselect the active map sport when valid, otherwise auto-select only a single supported sport. On submit, convert local time, run shared validation, capture one normalized request snapshot, disable inputs/navigation, and submit once. After five seconds expose a "Still creating..." state without canceling. An uncertain network failure preserves the exact snapshot/id for a safe Retry and keeps editing locked until the outcome is resolved or the draft is abandoned. A definitive validation failure unlocks the form and preserves every field.

**Reference-conflict recovery, split by conflict class**: The venue is fixed by the map selection (`MapViewModel.cs:261-273`) and is not an editable field in the form, so the two conflict classes cannot share one recovery path.

- **Sport-level** (`sport_not_found`, `sport_not_supported_at_venue`, and `reference_data_changed` naming `sportId`): refresh the catalog, unlock the form, mark only the sport selection invalid, re-open the sport picker in place, and preserve every other field. No event was created.
- **Venue-level** (`venue_not_found`, and `reference_data_changed` naming `venueId`): the draft is anchored to a venue that no longer exists and cannot be repaired in place. Refresh the catalog, show an explanatory message, close the form, and return to the map with the stale venue's sheet dismissed. Losing the draft is accepted for this branch.

Every conflict code Phase 3 can emit must terminate in one of these two defined states.

#### 6. Create event page

**File**: `app/ChoNaBojoApp/Views/CreateEventPage.xaml`, `app/ChoNaBojoApp/Views/CreateEventPage.xaml.cs` (new)

**Intent**: Present an accessible MD3 form and return either a `CreatedEventResponse` or cancellation to the map without introducing a second navigation architecture.

**Contract**: Follow the existing modal `ShowAsync`/`TaskCompletionSource` handoff used by `AddressSearchPage`. Use Uranium UI form controls and existing resource styles for title, optional description, sport, date, start/end, participant limit, auto-accept, inline errors, Create, and Cancel. Show an activity indicator plus explicit "Creating event..." and long-wait copy, and block Back/Cancel while a request outcome is unknown.

**In-flight dismissal guard**: `AddressSearchPage` resolves its completion source on dismissal (`AddressSearchPage.xaml.cs:31-38` nulls `_completion` and completes it with `null` in `OnDisappearing`). Copying that behavior verbatim would let Android hardware Back or swipe-dismiss tear the page down mid-POST, creating an event the app cannot show or cancel before S-04/S-07 — and a re-created draft would mint a new `ClientRequestId`, so Phase 3 idempotency would not deduplicate it. Therefore in-flight state must be authoritative over dismissal: override `OnBackButtonPressed` to return `true` while a request outcome is unknown, and complete the `TaskCompletionSource` with cancellation in `OnDisappearing` only when no request is outstanding. If the page is destroyed anyway by a dismissal path that bypasses both hooks, the in-flight request must still complete and its result must not be surfaced through a dead completion source.

#### 7. Map entry point and DI

**File**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`

**Intent**: Raise a create request for the currently selected venue without moving page navigation into the ViewModel.

**Contract**: Add a create command/event that requires `SelectedVenue`, preserves the selected venue sheet, and supplies the active sport filter as an optional default.

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml`, `app/ChoNaBojoApp/Views/MapPage.xaml.cs`

**Intent**: Replace the disabled placeholder with the live form handoff and remove copy that would contradict a successful creation.

**Contract**: Enable the styled Create event button, bind it to the ViewModel command, remove "Coming soon" and the unconditional "No events here yet" assertion, resolve `CreateEventPage` from DI, and await its typed result. Until Phase 5, a successful result may be acknowledged by snackbar while retaining the selected map/venue context.

**File**: `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Register the new page and ViewModel using existing transient lifetimes.

**Contract**: Add transient registrations for `CreateEventPage` and `CreateEventViewModel`; keep API/catalog services singleton and do not add packages.

### Success Criteria:

#### Automated Verification:

- Whole solution builds: `dotnet build solutions\ChoNaBojo.slnx`
- Android head builds: `dotnet build app\ChoNaBojoApp -f net10.0-android`
- Windows head builds: `dotnet build app\ChoNaBojoApp -f net10.0-windows10.0.19041.0`
- Placeholder scan confirms the venue action is live: `rg "Coming soon|IsEnabled=\"False\"" app\ChoNaBojoApp\Views\MapPage.xaml` finds no create-event placeholder

#### Manual Verification:

- The form opens for the selected venue, lists only its supported sports, and applies the agreed preselection behavior
- Required/optional text and participant boundaries show inline errors while valid boundary values 2 and 300 submit
- Same-day, overnight, exactly-24-hour, past-start, spring DST-gap, and autumn ambiguous-time cases follow the agreed rules using the device time zone
- Submit immediately disables fields and navigation, shows creating feedback, changes to a clear long-wait state after five seconds, and never double-submits
- A network/timeout outcome preserves the exact request snapshot and allows a retry that resolves to one event
- A stale sport conflict refreshes the catalog, marks the sport selection invalid, preserves all other draft values, and creates no event
- A stale venue conflict refreshes the catalog, explains the problem, closes the form, and returns to the map with the stale venue's sheet dismissed, creating no event
- Android hardware Back and swipe-dismiss are both refused while a create request is in flight, and the form only closes once the outcome is known

**Implementation Note**: After completing this phase and all automated verification passes, pause for human confirmation of the Android form, timing, and recovery behavior before proceeding.

---

## Phase 5: Created Event Detail and End-to-End Handoff

### Overview

Complete the organizer experience by presenting the safe create response in a read-only detail modal and verifying the entire map-to-create-to-detail flow, including authenticated POST replay.

### Changes Required:

#### 1. Event detail ViewModel

**File**: `app/ChoNaBojoApp/ViewModels/EventDetailViewModel.cs` (new)

**Intent**: Project a `CreatedEventResponse` into display-only local values without making another API request.

**Contract**: Expose title, optional description, venue name/address, sport name, start/end converted from UTC to the device's current local time, participant display `1 / limit`, auto-accept display, and close intent. Do not expose or synthesize organizer contact information.

#### 2. Event detail page

**File**: `app/ChoNaBojoApp/Views/EventDetailPage.xaml`, `app/ChoNaBojoApp/Views/EventDetailPage.xaml.cs` (new)

**Intent**: Show immediate confirmation of exactly what was created and let system Back or Close return to the same map context.

**Contract**: Render a read-only, accessible MD3 summary using existing typography, surface, button, spacing, and semantic conventions. Open as a modal rooted separately from the create form so the submitted form is not left in the back stack. Back/Close dismisses the detail and reveals the map with the same selected venue sheet.

#### 3. Create-to-detail orchestration and DI

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml.cs`

**Intent**: Turn the completed create result into the selected organizer-facing destination.

**Contract**: After `CreateEventPage` closes with success, resolve and initialize `EventDetailPage`, open it modally, and retain map/venue selection beneath it. Cancellation or failed creation must not open detail.

**File**: `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Make the detail page and ViewModel DI-resolvable.

**Contract**: Add transient `EventDetailPage` and `EventDetailViewModel` registrations without changing shell roots or auth navigation.

### Success Criteria:

#### Automated Verification:

- Whole solution builds: `dotnet build solutions\ChoNaBojo.slnx`
- Android and Windows heads build in one verification pass: `dotnet build app\ChoNaBojoApp -f net10.0-android` and `dotnet build app\ChoNaBojoApp -f net10.0-windows10.0.19041.0`
- Event detail privacy scan finds no contact binding: `rg "Contact|LoginEmail|Communicator|Password|Hash|Token" app\ChoNaBojoApp\Views\EventDetailPage.xaml app\ChoNaBojoApp\ViewModels\EventDetailViewModel.cs` returns no match
- EF model remains synchronized after all source changes: `dotnet ef migrations has-pending-model-changes --project server` exits successfully

#### Manual Verification:

- A valid creation opens detail within the five-second normal target and shows the exact title, description, venue, sport, device-local times, participant count, capacity, and auto-accept value
- Detail contains no contact data; system Back and Close return to the same map and selected venue without returning to or resubmitting the completed form
- Canceling an unsubmitted form returns directly to the selected venue sheet and creates nothing
- With an expired access token and valid refresh token, one create action refreshes and replays the complete protected POST body exactly once
- Double-tap and response-loss retry scenarios produce exactly one event and the documented user feedback
- Restarting the app does not promise to restore the detail screen; the persisted event remains available for S-04 discovery

**Implementation Note**: After completing this phase and all automated verification passes, pause for final human confirmation of the end-to-end Android flow.

---

## Testing Strategy

### Unit Tests:

- No unit-test project is added in this slice, per the planning decision.
- Shared validation is exercised through the authoritative API matrix for text, UTC offset, participant boundaries, past start, end ordering, and 24-hour duration.
- The implementer must keep the validator pure and clock-injected so a later test project can cover it without refactoring production code.

### Integration Tests:

- No committed integration-test harness or Testcontainers dependency is added.
- The AI agent runs operational checks against the configured development database: migration/model checks, unauthenticated 401, valid 201, request-id replay 200 with the same id, stale-reference 409, validation 400, payload privacy, UTC values, row count, and five-second timing.
- Database constraint behavior is checked through the generated idempotent SQL and manual Supabase SQL editor probes because `psql` is not installed.

### Manual Testing Steps:

1. Apply `AddSportsEvents` to the development Supabase database through the session-mode 5432 connection; inspect constraints and indexes.
2. On an Android Google APIs emulator, select a venue and open Create event; confirm only supported sports appear.
3. Exercise title/description limits, capacity 1/2/300/301, past start, end ordering, overnight, exactly 24 hours, over 24 hours, DST gap, and DST ambiguity.
4. Create a valid event and confirm immediate busy feedback, long-wait feedback after five seconds when delayed, one persisted row, and the response-backed detail screen.
5. Confirm UTC persistence and local display after changing the device time zone.
6. Simulate stale venue/sport data; confirm targeted catalog refresh and draft preservation.
7. Simulate response loss and retry; confirm the same event id and one row.
8. Exercise an expired access token with a valid refresh token; confirm the POST body survives refresh and is not duplicated.
9. Use Back/Close from detail and Cancel from the form; confirm the map and selected venue context remain correct.
10. Inspect every response and detail surface for absence of login/contact/communicator data.

## Performance Considerations

Creation is one indexed idempotency lookup, one venue/sport lookup, and one insert; no event-list query or participant aggregation is introduced. The `(OrganizerUserId, ClientRequestId)` unique index keeps retries constant-time. No listing index is added here: S-04 filters by venue, sport, and availability together, so it must choose the index its real query shape needs rather than inherit a guess from this slice.

The five-second target is measured under normal local-client-to-development-API connectivity. The MAUI client does not fail at five seconds; it changes waiting copy while the request continues. Reference refresh fetches venues and sports concurrently and swaps the cache atomically, matching the existing catalog-loading performance pattern.

## Migration Notes

`AddSportsEvents` is additive and has no existing event data to transform. Generate and inspect it locally, then apply it explicitly with `dotnet ef database update --project server --connection "<AppDbMigrations session-mode connection>"`; never run migrations on app startup or through the transaction-mode runtime pooler.

Before S-04 or production data depends on the table, rollback can use the migration's `Down` operation to drop `SportsEvents`. After downstream event/participant slices land, rollback must be forward-fixed or backed up rather than dropping event data.

## References

- Roadmap S-03: `context/foundation/roadmap.md`
- Product rules and access control: `context/foundation/prd.md`
- Shared-project and validation rules: `context/foundation/lessons.md`
- UI form, event, feedback, and accessibility rules: `context/foundation/ui-guidelines.md`
- Protected API group: `server/Program.cs:123-125`
- Current-user identity: `server/Auth/CurrentUser.cs:12-32`
- EF model conventions: `server/Data/ChoNaBojoContext.cs`
- Typed API result pattern: `app/ChoNaBojoApp/Services/ApiService.cs`
- Venue catalog locking pattern: `app/ChoNaBojoApp/Services/Venues/VenueCatalog.cs`
- Selected venue and form entry point: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:261-273`, `app/ChoNaBojoApp/Views/MapPage.xaml:304-323`
- Modal handoff pattern: `app/ChoNaBojoApp/Views/AddressSearchPage.xaml.cs:45-75`
- Protected POST replay implementation: `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:51-100,193-210`
- Migration workflow: `context/archive/2026-07-12-data-layer-foundation/plan.md`
- Prior protected-body retry finding: `context/archive/2026-07-18-account-and-session/reviews/impl-review.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared Event Contract and Validation

#### Automated

- [x] 1.1 Shared contracts and validation compile — fe98a18
- [x] 1.2 Whole solution compiles against the new contract — fe98a18
- [x] 1.3 Contract privacy scan over the whole shared DTO folder finds no forbidden fields — fe98a18

#### Manual

- [x] 1.4 Request fields, limits, stable error keys, UTC requirement, and response summaries match the approved product decisions — fe98a18
- [x] 1.5 CreatedEventResponse contains everything the read-only detail screen needs and no private user data — fe98a18

### Phase 2: Sports Event Persistence and Migration

#### Automated

- [ ] 2.1 Solution builds with the event model
- [ ] 2.2 EF resolves the context
- [ ] 2.3 Migration is generated and listed
- [ ] 2.4 Model and snapshot agree
- [ ] 2.5 Idempotent SQL script contains the table, checks, foreign keys, and indexes

#### Manual

- [ ] 2.6 Apply the migration through the Supabase session-mode 5432 connection and confirm runtime still uses transaction-mode 6543
- [ ] 2.7 Supabase schema inspection confirms all event constraints, foreign keys, and indexes
- [ ] 2.8 Direct invalid inserts are rejected by the database constraints

### Phase 3: Authenticated Idempotent Create API

#### Automated

- [ ] 3.1 Server and solution build
- [ ] 3.2 API runs in the background on http://localhost:5100 and stays up for 3.3-3.8, which use a JWT from /auth/login
- [ ] 3.3 Unauthenticated POST /api/events returns 401 and creates no row
- [ ] 3.4 Valid authenticated request returns 201 with safe complete detail data
- [ ] 3.5 Repeating the request id returns 200 with the same event and only one row
- [ ] 3.6 Invalid and stale-reference requests return the agreed 400/409 outcomes without creating rows
- [ ] 3.7 Normal valid create call completes within five seconds
- [ ] 3.8 Sub-microsecond-precision timestamps round-trip consistently and replay returns the same event id

#### Manual

- [ ] 3.9 Supabase row confirms JWT-derived organizer, normalized text, and UTC timestamps
- [ ] 3.10 First-create and replay serialized response bodies contain no login or contact data

### Phase 4: MAUI Create Form and Recovery Flow

#### Automated

- [ ] 4.1 Whole solution builds
- [ ] 4.2 Android head builds
- [ ] 4.3 Windows head builds
- [ ] 4.4 Venue create-event placeholder is removed

#### Manual

- [ ] 4.5 Form opens for the selected venue with only supported sports and correct preselection
- [ ] 4.6 Text and participant boundaries validate while values 2 and 300 submit
- [ ] 4.7 Same-day, overnight, 24-hour, past-start, and DST cases follow the agreed device-time rules
- [ ] 4.8 Submit shows immediate and long-wait feedback and never double-submits
- [ ] 4.9 Network or timeout preserves the exact request snapshot for safe retry
- [ ] 4.10 Stale sport refreshes the catalog, marks the sport selection invalid, and preserves the draft
- [ ] 4.11 Stale venue refreshes the catalog, explains, closes the form, and returns to the map
- [ ] 4.12 Hardware Back and swipe-dismiss are refused while a create request is in flight

### Phase 5: Created Event Detail and End-to-End Handoff

#### Automated

- [ ] 5.1 Whole solution builds
- [ ] 5.2 Android and Windows heads build
- [ ] 5.3 Event detail privacy scan finds no contact binding
- [ ] 5.4 EF model remains synchronized after all source changes

#### Manual

- [ ] 5.5 Valid creation opens complete device-local detail within the five-second normal target
- [ ] 5.6 Detail is contact-free and Back/Close returns to the same map and venue without resubmission
- [ ] 5.7 Canceling an unsubmitted form returns to the venue and creates nothing
- [ ] 5.8 Expired-token refresh replays the complete protected POST body exactly once
- [ ] 5.9 Double-tap and response-loss retry produce exactly one event and correct feedback
- [ ] 5.10 App restart makes no detail-restoration promise while the event remains persisted for S-04
