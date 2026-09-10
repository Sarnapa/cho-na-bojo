# Push Notifications (S-06) Implementation Plan

## Overview

Deliver FCM push notifications around the matchmaking loop: the organizer is notified within 30 seconds when someone requests to join their event (FR-008), and the requester is notified within 30 seconds when the organizer accepts or rejects (FR-010). Delivery is server-owned — a transactional outbox written inside the existing join-request transaction, drained by a hosted worker that calls the Firebase Admin SDK. The notification carries only generic text plus opaque identifiers; the app refetches authoritative state from the authenticated API, so push never becomes a side channel around the contact-reveal privacy boundary.

## Current State Analysis

**The domain triggers already exist and converge on one code path.** `TransitionJoinRequestAsync` (`server/Events/EventEndpoints.cs:641-810`) serves join, accept, and reject. It opens an explicit transaction, takes `SELECT 1 FROM "SportsEvents" ... FOR UPDATE`, validates state and capacity, mutates the request, and ends with a single `SaveChangesAsync` + `CommitAsync` (`server/Events/EventEndpoints.cs:792-793`). Auto-accept flows through the same path via `effectiveTargetStatus` (`server/Events/EventEndpoints.cs:753-757`). This is the outbox insertion point.

**Three early-return paths return without a state change** and must not enqueue anything:

- an existing request found before the transition (`server/Events/EventEndpoints.cs:317-321` and `:684-691`),
- a resolution whose `Status` already equals `targetStatus` (`server/Events/EventEndpoints.cs:725-732`),
- the unique-constraint recovery path that **rolls back** (`server/Events/EventEndpoints.cs:799-821`, rollback at `:802`) and resolves to a pre-existing request.

**Nothing push-related exists yet.** The server has no Firebase dependency (`server/server.csproj:1-27`) and no `PushInstallations`/outbox tables (`server/Data/ChoNaBojoContext.cs:9-70`). The app has no Firebase package (`app/ChoNaBojoApp/ChoNaBojoApp.csproj:69-78`), no `POST_NOTIFICATIONS` permission and no Firebase metadata (`app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml:1-14`), and a bare `MainActivity` with no `OnCreate`/`OnNewIntent` override (`app/ChoNaBojoApp/Platforms/Android/MainActivity.cs:1-10`).

**Route prefix `/me` already exists** under the authenticated `/api` group: `MapGet("/me/events", ...)` (`server/Events/EventEndpoints.cs:49-50`), mapped through `apiGroup` in `server/Program.cs:126-129`. Push installation endpoints follow that existing prefix rather than introducing a new grouping concept.

**Logout sequencing constrains where the unlink can live.** `SessionService.SignOutAsync` sets `_signedOut`, nulls `Current`, and calls `_tokenStore.ClearAsync()` *before* its best-effort server call (`app/ChoNaBojoApp/Services/Auth/SessionService.cs:88-125`). Any unlink issued after that point has no bearer token — but the surviving call, `_authTokenClient.LogoutAsync` (`:117`), carries the **refresh token**, which is credential enough. Critically, the class carries an explicit invariant in its doc comment (`app/ChoNaBojoApp/Services/Auth/SessionService.cs:3-8`): it depends only on `ITokenStore` and `IAuthTokenClient`, **never** on `IApiService`, so it cannot recurse into the `AuthenticatingHttpMessageHandler`-wrapped `"ChoNaBojoApi"` client. Any unlink hook must respect that invariant.

**The Windows TFM still builds.** `TargetFrameworks` includes `net10.0-windows10.0.19041.0` on Windows hosts (`app/ChoNaBojoApp/ChoNaBojoApp.csproj:11-12`), so every Firebase item must be Android-conditional or the Windows build breaks.

**`minSdk` is 21 while the PRD claims Android 10+.** `SupportedOSPlatformVersion` for Android is `21.0` (`app/ChoNaBojoApp/ChoNaBojoApp.csproj:38`).

**The binding's FID surface is unverified.** `Xamarin.Firebase.Messaging 125.1.1.1` wraps Firebase Messaging 25.1.1; Context7 could not confirm the generated C# signatures for `Register`/`OnRegistered` (`context/changes/push-notifications/xamarin-firebase-messaging-context7.md:23-45`).

### Key Discoveries:

- The shared transition already owns the transaction the outbox needs — no new transaction scope, no restructuring of `EventEndpoints` (`server/Events/EventEndpoints.cs:641-660`).
- Idempotent replay is a first-class concept in this codebase and already returns early on commit — the "don't double-notify" requirement maps onto existing branches rather than new logic (`server/Events/EventEndpoints.cs:317-321`).
- `SessionService`'s no-`IApiService` invariant rules out unlinking through `IApiService`; the existing `_authTokenClient.LogoutAsync` call on the un-handled `"ChoNaBojoAuth"` client is the natural carrier, since it already authenticates with the refresh token (`app/ChoNaBojoApp/Services/Auth/SessionService.cs:3-8`, `app/ChoNaBojoApp/MauiProgram.cs:68-72`).
- `EventJoinRequest` demonstrates the required enum guarding pattern — `CK_EventJoinRequests_Status ... IN (1, 2, 3)` alongside application validation (`server/Data/ChoNaBojoContext.cs:277-282`), which `context/foundation/lessons.md` requires for every persisted enum.
- `Guid` primary keys use `HasDefaultValueSql("gen_random_uuid()")` and timestamps use `timestamp with time zone` (`server/Data/ChoNaBojoContext.cs:252-268`).
- The Maps API key already establishes the "build-time secret with a graceful empty fallback" pattern (`app/ChoNaBojoApp/ChoNaBojoApp.csproj:3-8`), which the Firebase server credential deliberately does **not** follow — it is a server-side secret, never a client build input.
- 400 responses use the existing `ValidationProblemResponse`; per `context/foundation/lessons.md` no parallel error shape may be introduced.

## Desired End State

An organizer with the app installed and notification permission granted receives a heads-up notification within 30 seconds of someone requesting to join their event. A requester receives one within 30 seconds of the organizer accepting or rejecting. Tapping any of them opens the app on **My events**, refreshed from the server. The notification body never contains a name, contact detail, or event title. Explicitly logging out while online stops that device receiving the previous account's notifications; a session that simply expires leaves the row active until the next login on that device re-claims it. A user with two devices gets the notification on both.

Verification: with the API deployed and a real device, perform join → accept and join → reject with a stopwatch; both notifications arrive and the measured commit-to-display latency is under 30 seconds. `dotnet build solutions/ChoNaBojo.slnx` succeeds for both the Android and Windows TFMs.

## What We're NOT Doing

- **No S-07 notification types** — cancel, remove-participant, and leave are out of scope. The outbox is built so S-07 adds enum values and payload cases only.
- **No in-app notification centre or history UI** — notifications are transient OS-level only.
- **No deep link to a specific event detail page.** `EventDetailPage` expects a fully materialised object, not an ID to load. All three types route to **My events**.
- **No iOS.** Every Firebase item is Android-conditional.
- **No second Firebase project.** One project serves dev and production for the MVP.
- **No Postgres-backed or device-level automated tests.** No Testcontainers, no Docker, no tests against the Supabase dev database. Concurrency, atomicity, and delivery behaviour are verified manually.
- **No Workload Identity Federation.** Railway gets a sealed service-account JSON variable.
- **No user-facing notification preferences/mute settings.**
- **No topic subscriptions or multicast batching** — per-installation sends only; volume does not justify it.
- **No exactly-once delivery guarantee.** Duplicates collapse into a single system notification because the same `notificationId` is used as the Android notification **tag** on both display paths — server-set in `AndroidConfig` for SDK-displayed (backgrounded) notifications, and client-set by `PushNotificationPresenter` for foreground ones.

## Implementation Approach

Three independent tracks that converge:

1. **Server delivery pipeline** — `PushOutbox` + `PushDeliveries` tables written inside the existing join-request transaction, drained by a `BackgroundService` that claims rows with `FOR UPDATE SKIP LOCKED`, fans out to each active installation, calls Firebase Admin, and records per-installation outcomes with exponential backoff and a dead-letter terminal state.
2. **Registration lifecycle** — `PushInstallations` keyed on the device registration id (one row per app installation, not per user), upserted through `PUT /api/me/push-installations`, atomically reassigned on account switch, and disabled server-side as part of the existing `POST /api/auth/logout` refresh-token revocation on explicit sign-out.
3. **Android client** — Firebase binding, notification channel, permission prompt, foreground handling, and cold/warm tap routing gated on session restoration.

Two decisions shape the structure:

**The registration identifier is opaque to everything except the gateway.** Contracts, persistence, and the client coordinator all call it a `DeviceRegistrationId` — a bounded string. Only `FirebasePushGateway` decides whether it goes into `Message.Fid` (FID mode) or `Message.Token` (legacy mode). Phase 1's compile spike picks the mode; if the binding lacks the FID surface, the fallback is a one-line gateway change plus a different service override, with no impact on the schema, the contracts, or any other phase.

**Notification failure must never affect the domain response.** The outbox insert is the only push work inside the transaction. Everything else happens after commit, in a different process loop. A Firebase outage degrades to "no notification"; it never fails or rolls back a join, accept, or reject.

## Critical Implementation Details

**Outbox insert ordering.** The `PushOutbox` row must be added and persisted *after* the existing `await dbContext.SaveChangesAsync(cancellationToken)` at `server/Events/EventEndpoints.cs:792-793` and *before* `CommitAsync`, via a second `SaveChangesAsync` on the same open transaction. This is atomic — both writes land in one commit — at the cost of one extra round-trip inside the `FOR UPDATE` lock (sub-millisecond, and the lock is already held for the duration of the transaction).

Adding the outbox row to the change tracker *before* the first `SaveChangesAsync` is **not** viable: `EventJoinRequest.Id` is database-generated (`HasDefaultValueSql("gen_random_uuid()")`, `server/Data/ChoNaBojoContext.cs:316`), so on the `createIfMissing` path (`server/Events/EventEndpoints.cs:700-705`) the id is still `Guid.Empty` until `SaveChanges` reads back the `RETURNING` value. Enqueuing before the save would write `EventJoinRequestId = Guid.Empty` (FK violation) and an `EventKey` of `join-request:00000000-...:{type}:{recipientUserId}` that collides on the unique index on the organizer's second join request — producing a 500 and a rolled-back join request, violating the rule that notification failure must never affect the domain response.

Inserting after `CommitAsync` loses the intent if the process dies. Do not add a nested transaction — `BeginTransactionAsync` is already active on this path.

**`SessionService` may not take `IApiService`.** The unlink rides on the logout call `SessionService` already makes: `_authTokenClient.LogoutAsync` runs on the un-handled `"ChoNaBojoAuth"` client (`app/ChoNaBojoApp/MauiProgram.cs:68-72`) and authenticates with the **refresh token**, so no access token and no extra named client are needed. `SessionService` therefore gains no new dependency beyond the existing `IPushRegistrationStore` read for the registration id. Injecting `IApiService` (directly or transitively) would recreate the recursion the handler comment warns about (`app/ChoNaBojoApp/Services/Auth/SessionService.cs:3-8`).

**The logout unlink covers explicit sign-out only — by design.** There are three `SignOutAsync` call sites. Only `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:675` passes `revokeServer: true`; `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:167` and `:171` pass `revokeServer: false` because they *are* the refresh-failure path (`app/ChoNaBojoApp/Services/Auth/ISessionService.cs:28-31`) — by definition no usable credential exists, so no server call of any kind is possible. Session expiry and offline explicit logout therefore leave the installation row active. That is accepted: the row is re-claimed (`UserId` reassigned, `DisabledUtc` cleared) by the next `PUT /api/me/push-installations` on that device, and the stale-installation cleanup job removes rows that are never reused. The end-state promise is scoped accordingly.

**`OnMessageReceived` runs on a worker thread with a hard 20-second budget** and must not touch MAUI UI. Marshal to the main thread via `MainThread.BeginInvokeOnMainThread` for any state refresh, and never call Shell navigation from the service.

**Auto-init fires before authentication.** Firebase may deliver the registration callback during app startup, before `LoadingPage` has restored the session (`app/ChoNaBojoApp/Views/LoadingPage.xaml.cs:26-33`). The registration id must be persisted locally as pending and uploaded later — a registration coordinator that assumes an authenticated session will silently drop the first registration on a fresh install.

**Notification channel importance is immutable after first creation.** Creating `event_updates` with the wrong importance cannot be corrected programmatically; it would require a new channel id. Create it once with `Default` importance (heads-up requires `High` — use `High` since these are time-sensitive alerts) before any notification is posted.

## Phase 1: Firebase Setup & Android Binding Spike

### Overview

Stand up the Firebase project, wire the Android binding into the build, raise `minSdk`, and resolve empirically whether the binding exposes the FID registration surface — before any other phase depends on the answer.

### Changes Required:

#### 1. Firebase Console (manual, one-time)

**Intent**: Create the single Firebase project that serves the MVP and download the Android client configuration. Create the narrowly-scoped server service account that Phase 5 and Phase 7 consume.

**Contract**: One Firebase project; one Android app registered with package name exactly `com.cho_na_bojo` (case-sensitive match to `ApplicationId` in `app/ChoNaBojoApp/ChoNaBojoApp.csproj:30`); FCM HTTP v1 API enabled; `google-services.json` downloaded unrenamed; one service account holding only the **Firebase Cloud Messaging API Admin** role, with a JSON key generated and stored outside the repository. No legacy server key is created. No Android signing SHA-1 is required for FCM.

#### 2. Android build wiring

**File**: `app/ChoNaBojoApp/ChoNaBojoApp.csproj`

**Intent**: Add the Firebase messaging binding and the client config file, both Android-only so the Windows TFM keeps building, and align the Android minimum API with the PRD's Android 10+ support claim.

**Contract**: A new `ItemGroup` guarded by `$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'` containing a pinned `PackageReference Include="Xamarin.Firebase.Messaging" Version="125.1.1.1"` and a `GoogleServicesJson` item pointing at the committed config file. The Android `SupportedOSPlatformVersion` changes from `21.0` to `29.0` (`app/ChoNaBojoApp/ChoNaBojoApp.csproj:38`). The version is pinned, never floated.

#### 3. Firebase client configuration

**File**: `app/ChoNaBojoApp/Platforms/Android/google-services.json`

**Intent**: Commit the client configuration so a fresh clone builds without a setup step. This is client config, not a credential — its identifiers ship inside the APK regardless.

**Contract**: The file is committed verbatim as downloaded. Confirm no `.gitignore` rule excludes it. The server service-account JSON is a different artefact and is never committed.

#### 4. Manifest metadata and permission

**File**: `app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml`

**Intent**: Declare the notification permission and the Firebase metadata that selects FID registration mode and sets default notification presentation for SDK-displayed notifications.

**Contract**: Adds `<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />` and, inside `<application>`, four `meta-data` entries: `firebase_messaging_installation_id_enabled` = `true`, `com.google.firebase.messaging.default_notification_icon` → `@drawable/ic_stat_notification`, `default_notification_color` → `@color/notification_color`, `default_notification_channel_id` → `event_updates`. Existing Maps metadata and permissions are preserved unchanged.

#### 5. Notification icon and colour resources

**Files**: `app/ChoNaBojoApp/Platforms/Android/Resources/drawable/ic_stat_notification.xml`, `app/ChoNaBojoApp/Platforms/Android/Resources/values/notification_colors.xml`

**Intent**: Supply the monochrome status-bar icon and accent colour Android requires; without a dedicated silhouette icon the launcher icon renders as a white square on API 21+.

**Contract**: A single-colour vector drawable (alpha-only silhouette) and a `<color name="notification_color">` matching the existing brand green `#2E7D32` used for the app icon and splash (`app/ChoNaBojoApp/ChoNaBojoApp.csproj:55-58`).

#### 6. Spike messaging service

**File**: `app/ChoNaBojoApp/Platforms/Android/Push/ChoNaBojoMessagingService.cs`

**Intent**: A minimal non-exported `FirebaseMessagingService` that logs the registration identifier and any received message. Its only job in this phase is to prove which registration API the binding actually generates.

**Contract**: `[Service(Exported = false)]` with `[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]`, subclassing `FirebaseMessagingService`. Overrides the registration callback that compiles — `OnRegistered(string)` if FID mode is available, otherwise `OnNewToken(string)` — plus `OnMessageReceived(RemoteMessage)`. Logging only; no upload, no notification posting, no navigation. If FID mode is unavailable, the `firebase_messaging_installation_id_enabled` metadata added in change 4 must be removed, because the SDK throws `IllegalStateException` when the two modes are mixed.

#### 7. Spike outcome record

**File**: `context/changes/push-notifications/binding-spike.md`

**Intent**: Record which registration mode the binding supports so later phases and any reviewer can see the basis for the gateway's `Fid` vs `Token` choice without re-running the spike.

**Contract**: Records the resolved package version, the exact generated signatures found, the chosen mode (FID or legacy token), and whether the installation-id metadata is present or removed.

### Success Criteria:

#### Automated Verification:

- Android build succeeds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Windows TFM still builds (proves Firebase items are correctly Android-conditional): `dotnet build app/ChoNaBojoApp -f net10.0-windows10.0.19041.0`
- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Merged Android manifest contains the messaging service with the `MESSAGING_EVENT` intent filter, `POST_NOTIFICATIONS`, and `minSdkVersion="29"`

#### Manual Verification:

- App deploys to a device/emulator with Google Play services and logs a non-empty registration identifier on first launch
- A test message sent from the Firebase Console reaches the device and displays while the app is backgrounded
- `binding-spike.md` records the resolved registration mode

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding. The registration mode recorded here determines the gateway implementation in Phase 5 — do not start Phase 5 without it.

---

## Phase 2: Push Installation Persistence & Registration API

### Overview

Persist one row per app installation, expose the authenticated endpoint the client uses to link it, and disable it on logout. No sending, no client changes.

### Changes Required:

#### 1. Wire contracts

**File**: `shared/ChoNaBojo.Contracts/DTOs/PushDTOs.cs`

**Intent**: Define the request/response shapes for installation registration, in the dependency-free Contracts project per `context/foundation/lessons.md`.

**Contract**: `RegisterPushInstallationRequest(string DeviceRegistrationId, string? AppVersion)` and `RegisterPushInstallationResponse(Guid InstallationId)`. No `UserId` field in either direction — the server derives it from the JWT. The response id is the server's opaque row identifier, stored locally for diagnostics; the registration id itself is never echoed back. Logout-time unlinking uses the `LogoutRequest` shape in `AuthDTOs.cs` (change 7b), keyed on the registration id rather than this installation id, because the logout call has no bearer token to scope an id lookup with.

#### 2. Push constants

**File**: `shared/ChoNaBojo.Contracts/Consts/PushPolicy.cs`

**Intent**: Centralise the bounds and payload vocabulary shared by client and server so the two cannot drift.

**Contract**: `DeviceRegistrationIdMaxLength` (512), `AppVersionMaxLength` (64), `SchemaVersion` ("1"), and the data-payload key names (`schemaVersion`, `type`, `eventId`, `joinRequestId`, `notificationId`, `sentAtUtc`).

#### 3. Registration validation

**File**: `shared/ChoNaBojo.Validation/PushValidation.cs`

**Intent**: Validate the registration request on the write path, mirroring the existing shared-validation pattern.

**Contract**: A static validator over `RegisterPushInstallationRequest` returning the framework-neutral `ValidationResult` (`shared/ChoNaBojo.Validation/ValidationResult.cs`). Rejects blank/whitespace registration ids and values exceeding the `PushPolicy` bounds. Returns `IResult`? No — never; the endpoint maps the result to `ValidationProblemResponse`.

#### 4. Installation entity

**File**: `server/Data/Entities/PushInstallation.cs`

**Intent**: One row per app installation, owned by whichever user most recently authenticated on that device.

**Contract**: `Id` (Guid), `UserId` (Guid, FK → `Users`), `DeviceRegistrationId` (string, unique), `AppVersion` (string?), `CreatedUtc`, `LastSeenUtc`, `DisabledUtc` (nullable). A navigation property to `User`. Deliberately no registration id on the `User` entity — one user may hold several installations.

#### 5. Entity configuration

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Register the DbSet and configure the table following the established conventions in this file.

**Contract**: `DbSet<PushInstallation> PushInstallations`; `Id` defaulted via `gen_random_uuid()`; all timestamps `timestamp with time zone`; unique index on `DeviceRegistrationId`; index on `UserId`; FK to `User` with `OnDelete(DeleteBehavior.Cascade)`; check constraints `CK_PushInstallations_DeviceRegistrationId_NotBlank` and `CK_PushInstallations_LastSeenUtc` (`"LastSeenUtc" >= "CreatedUtc"`).

#### 6. Migration

**File**: `server/Migrations/<timestamp>_AddPushInstallations.cs`

**Intent**: Apply the new table.

**Contract**: Generated via `dotnet ef migrations add AddPushInstallations`. Additive only — no changes to existing tables. Applied with `dotnet ef database update`; the app never auto-migrates.

#### 7. Registration endpoints

**File**: `server/Push/PushEndpoints.cs`

**Intent**: Let an authenticated device link its registration id.

**Contract**: `MapPushEndpoints(this IEndpointRouteBuilder)` registering `PUT /me/push-installations` → `RegisterPushInstallationAsync` (name `MePushInstallationsRegister`), matching the existing `/me/events` prefix (`server/Events/EventEndpoints.cs:49-50`). The upsert derives `UserId` from `httpContext.GetUserId()`, matches on `DeviceRegistrationId`, and on match **reassigns** `UserId` (account switch), clears `DisabledUtc`, and refreshes `LastSeenUtc` — one row, no duplicates. Returns `200` with the installation id for both create and update; `400` with the existing `ValidationProblemResponse` on invalid input.

There is **no** `DELETE` endpoint. Unlinking is a side effect of logout (change 7 below), because that is the only sign-out path with a usable credential.

#### 7b. Logout-time unlink

**Files**: `shared/ChoNaBojo.Contracts/DTOs/AuthDTOs.cs`, `shared/ChoNaBojo.Validation/AuthValidation.cs`, `server/Auth/AuthEndpoints.cs`

**Intent**: Disable the installation on the one sign-out path that can authenticate — the refresh-token revocation the client already performs.

**Contract**: `POST /auth/logout` currently binds `RefreshRequest` (`shared/ChoNaBojo.Contracts/DTOs/AuthDTOs.cs:16`) and is handled by `LogoutAsync` (`server/Auth/AuthEndpoints.cs:172-185`). Introduce a dedicated `LogoutRequest(string RefreshToken, string? DeviceRegistrationId)` and bind logout to it; `/auth/refresh` keeps `RefreshRequest` unchanged. The new field is optional and additive, so an older client posting only `refreshToken` still binds and still validates — `ValidateLogoutRequest` reuses the existing refresh-token rule and only bounds `DeviceRegistrationId`'s length when present.

After `RevokeFamilyAsync` succeeds and when `DeviceRegistrationId` is non-blank, set `DisabledUtc` on the matching `PushInstallations` row **only if it belongs to the user whose refresh-token family was just revoked** — so a stolen registration id cannot be used to silence another user's device. Still returns `204` whether or not a row was affected, so repeated logout is idempotent and the call cannot be used to probe other users' rows. A failure to disable the row is logged and swallowed: logout must not fail because of push bookkeeping.

#### 8. Endpoint registration

**File**: `server/Program.cs`

**Intent**: Expose the endpoints under the authenticated group.

**Contract**: `apiGroup.MapPushEndpoints();` added alongside the existing `MapVenueEndpoints`/`MapEventEndpoints` calls (`server/Program.cs:126-129`), inheriting `RequireAuthorization()`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Migration is generated and applies cleanly: `dotnet ef database update --project server`
- No shared project gained a framework dependency (Contracts/Validation still reference nothing beyond each other)

#### Manual Verification:

- `PUT /api/me/push-installations` with a bearer token creates one row and returns an installation id
- Repeating the same call updates `LastSeenUtc` without creating a second row
- The same registration id sent with a second user's token reassigns `UserId` — still exactly one row
- The endpoint returns `401` without a bearer token
- `POST /api/auth/logout` with `deviceRegistrationId` sets `DisabledUtc` on that row and still returns `204`
- `POST /api/auth/logout` with another user's `deviceRegistrationId` returns `204` and changes nothing
- `POST /api/auth/logout` with no `deviceRegistrationId` behaves exactly as before
- A blank registration id returns `400` shaped as `ValidationProblemResponse`

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Client Registration Lifecycle & Logout Unlink

### Overview

Carry the registration id from the Firebase callback to the authenticated API, surviving the fact that registration can fire before a session exists — and stop a device that was explicitly logged out while online from receiving the previous account's notifications.

### Changes Required:

#### 1. Local registration state

**Files**: `app/ChoNaBojoApp/Services/Push/IPushRegistrationStore.cs`, `app/ChoNaBojoApp/Services/Push/PushRegistrationStore.cs`

**Intent**: Persist the pending registration id and what has already been uploaded, so a registration arriving before login is not lost and an unchanged registration is not re-uploaded on every launch.

**Contract**: Reads/writes `Preferences` (not `SecureStorage` — a registration id is not a credential, and `SecureStorage` on Android requires main-thread access on first read, which the Firebase worker thread cannot guarantee). Stores: latest registration id, last successfully uploaded registration id, the server installation id, and the user id it was uploaded for. Synchronous, safe to call from a background thread.

#### 2. Firebase callback bridge

**File**: `app/ChoNaBojoApp/Platforms/Android/Push/ChoNaBojoMessagingService.cs`

**Intent**: Hand the registration id from the Android service to app-level code. The service is instantiated by Android, not by DI, so it cannot take constructor dependencies.

**Contract**: The registration callback resolves the registration coordinator from the MAUI service provider (`IPlatformApplication.Current?.Services`) and calls it fire-and-forget, guarded so a null provider during early startup is a no-op after persisting to the store. The store write happens first and unconditionally — that is what makes a pre-authentication registration recoverable.

#### 3. Registration coordinator

**Files**: `app/ChoNaBojoApp/Services/Push/IPushRegistrationService.cs`, `app/ChoNaBojoApp/Services/Push/PushRegistrationService.cs`

**Intent**: The single place that decides whether an upload should happen and performs it idempotently.

**Contract**: `Task SyncAsync(CancellationToken)` — no-ops when unauthenticated, when no registration id is stored, or when the stored id and user already match what was uploaded; otherwise calls the API and records the result. `Task OnRegistrationIdChangedAsync(string, CancellationToken)` — persists then attempts a sync. Failures are swallowed and retried on the next trigger; push registration must never surface an error to the user. Non-Android targets get a no-op implementation so the Windows build has a registration for the interface.

#### 4. Sync triggers

**Files**: `app/ChoNaBojoApp/Views/LoadingPage.xaml.cs`, `app/ChoNaBojoApp/ViewModels/LoginViewModel.cs`, `app/ChoNaBojoApp/ViewModels/RegisterViewModel.cs`

**Intent**: Upload the registration id at every point a session becomes available.

**Contract**: After `SetAppRoot()` on successful session restoration (`app/ChoNaBojoApp/Views/LoadingPage.xaml.cs:43`) and after a successful login/registration, fire `SyncAsync` without awaiting it on the navigation path — registration must never delay or block entering the app.

#### 5. API client methods

**Files**: `app/ChoNaBojoApp/Services/IApiService.cs`, `app/ChoNaBojoApp/Services/ApiService.cs`

**Intent**: Call the Phase 2 registration endpoint through the standard authenticated client.

**Contract**: `RegisterPushInstallationAsync(RegisterPushInstallationRequest, CancellationToken)` returning the existing typed-result pattern used by the other methods in this file, over the `"ChoNaBojoApi"` named client so bearer attachment and transparent refresh apply.

#### 6. Logout carries the registration id

**Files**: `app/ChoNaBojoApp/Services/Auth/IAuthTokenClient.cs`, `app/ChoNaBojoApp/Services/Auth/AuthTokenClient.cs`

**Intent**: Send the registration id on the one sign-out call that can authenticate, without introducing a second HTTP client or a second endpoint.

**Contract**: `LogoutAsync(string refreshToken, CancellationToken)` (`app/ChoNaBojoApp/Services/Auth/IAuthTokenClient.cs:68`) gains a `string? deviceRegistrationId` parameter and posts the Phase 2 `LogoutRequest` instead of `RefreshRequest`. It keeps running on the existing un-handled `"ChoNaBojoAuth"` named client (`app/ChoNaBojoApp/MauiProgram.cs:68-72`), so the documented no-recursion invariant holds unchanged. No new `IPushInstallationUnlinker`, no `"ChoNaBojoPush"` client, no `DELETE` call — the refresh token already authenticates this request, so no access token is needed.

#### 7. Sign-out passes the registration id and clears local push state

**File**: `app/ChoNaBojoApp/Services/Auth/SessionService.cs`

**Intent**: Supply the registration id to the logout call and reset local push markers, without breaking the `IApiService`-free invariant.

**Contract**: `SessionService` takes `IPushRegistrationStore` (a `Preferences`-backed synchronous store — not an API client, so the invariant at `app/ChoNaBojoApp/Services/Auth/SessionService.cs:3-8` is preserved). Inside the `_signOutLock` critical section, the stored registration id is read alongside the refresh token; the existing best-effort `_authTokenClient.LogoutAsync(refreshTokenToRevoke, ...)` call (`app/ChoNaBojoApp/Services/Auth/SessionService.cs:117`) becomes `LogoutAsync(refreshTokenToRevoke, deviceRegistrationId, ...)`. Regardless of whether that call succeeds, the uploaded-state markers (uploaded registration id, installation id, uploaded-for user id) are cleared locally so the next login re-uploads and re-claims the row; the registration id itself is kept, since it survives logout on the device. Any logout failure stays swallowed as today; local sign-out always completes.

No new ordering constraint is introduced: the unlink now happens *inside* the revocation request, so it cannot be rejected by a revocation that already ran.

The two `revokeServer: false` call sites (`app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:167`, `:171`) are deliberately unchanged — they are the refresh-failure path and have no usable credential. See "The logout unlink covers explicit sign-out only" in Critical Implementation Details.

#### 8. Service registration

**File**: `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Register the new services as singletons alongside the existing ones.

**Contract**: `IPushRegistrationStore` and `IPushRegistrationService` added as singletons near the existing registrations (`app/ChoNaBojoApp/MauiProgram.cs:75-87`), with `#if ANDROID` selecting the real implementations and no-ops elsewhere, mirroring the existing `IAddressSearchService` pattern. `IPushRegistrationStore` must be registered *before* `ISessionService`, which now depends on it. No new named `HttpClient` is added.

### Success Criteria:

#### Automated Verification:

- Solution builds for both TFMs: `dotnet build solutions/ChoNaBojo.slnx`
- Android build succeeds: `dotnet build app/ChoNaBojoApp -f net10.0-android`

#### Manual Verification:

- Fresh install → log in → exactly one `PushInstallations` row appears for that user
- Force-close and relaunch → no duplicate row; `LastSeenUtc` refreshes
- Log out (online) → the row's `DisabledUtc` is set
- Log in as a second user on the same device → the same row is reassigned to the new `UserId` and re-enabled; still exactly one row for that registration id
- Log out with airplane mode on → sign-out completes promptly (no hang), the app returns to the login screen, and the row stays active until the next login on that device re-claims it
- Let the refresh token expire so the handler force-signs-out → sign-out completes and the row is knowingly left active (documented scope limit, not a defect)
- Install on a second device with the same account → two rows, both active

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 4: Transactional Outbox & Intent Creation

### Overview

Record notification intent atomically with the join-request state change — still without sending anything.

### Changes Required:

#### 1. Notification type enum

**File**: `shared/ChoNaBojo.Contracts/Enums/PushNotificationType.cs`

**Intent**: Name the three S-06 notification types as a cross-boundary enum, in Contracts because both the payload builder and the client's tap router read it.

**Contract**: `JoinRequestCreated = 1`, `JoinRequestAccepted = 2`, `JoinRequestRejected = 3`. Explicit values; new S-07 types append. Per `context/foundation/lessons.md`, this enum is persisted and therefore requires both `Enum.IsDefined` validation in application code and a database `CHECK` constraint.

#### 2. Outbox entities

**Files**: `server/Data/Entities/PushOutboxItem.cs`, `server/Data/Entities/PushDelivery.cs`

**Intent**: Separate the logical notification (one per recipient per transition) from its per-installation delivery attempts, so a success on one device is never retried because another device failed.

**Contract**: `PushOutboxItem` — `Id`, `EventKey` (string, unique), `RecipientUserId`, `Type` (`PushNotificationType`), `SportsEventId`, `EventJoinRequestId`, `NotificationId` (Guid, the client-side dedup key), `OccurredUtc`, `NextAttemptUtc`, `ClaimedUtc?`, `CompletedUtc?`. `PushDelivery` — `Id`, `PushOutboxItemId`, `PushInstallationId`, `AttemptCount`, `NextAttemptUtc`, `AcceptedUtc?`, `DeadLetteredUtc?`, `LastErrorCode?`, `FcmMessageId?`.

`PushOutboxItem.NextAttemptUtc` is the item's **due time** — the minimum `NextAttemptUtc` across its still-pending deliveries, initialised to `OccurredUtc` on insert and recomputed at the end of every worker pass. Without it the claim would re-select a backing-off item every poll, and because that item is also the *oldest*, it would sit at the front of the `OccurredUtc` ordering and hold batch slots ahead of fresh work — head-of-line blocking that bites hardest exactly when FCM is already degraded.

`EventKey` is the idempotency guard and carries the recipient, because an accept produces one notification for one recipient but S-07 will fan a single transition to many: `join-request:{joinRequestId}:{type}:{recipientUserId}`.

#### 3. Entity configuration

**File**: `server/Data/ChoNaBojoContext.cs`

**Intent**: Configure both tables per existing conventions, with the enum guarded at the database layer.

**Contract**: DbSets for both; `gen_random_uuid()` defaults; `timestamp with time zone` throughout; unique index on `PushOutboxItem.EventKey`; unique composite index on `(PushOutboxItemId, PushInstallationId)`; a filtered index over incomplete items (`WHERE "CompletedUtc" IS NULL`) keyed on `(NextAttemptUtc, OccurredUtc)` so the claim's due-time filter and its ordering are both served; `CK_PushOutbox_Type` restricting `Type` to `IN (1, 2, 3)` per the `CK_EventJoinRequests_Status` precedent (`server/Data/ChoNaBojoContext.cs:277-282`); `CK_PushDeliveries_AttemptCount` (`>= 0`). FK from `PushDelivery` → `PushOutboxItem` cascades; FK → `PushInstallation` restricts, so installation cleanup cannot silently erase delivery history.

#### 4. Migration

**File**: `server/Migrations/<timestamp>_AddPushOutbox.cs`

**Intent**: Apply both tables.

**Contract**: `dotnet ef migrations add AddPushOutbox`; additive only.

#### 5. Intent mapping (pure)

**File**: `server/Push/PushIntentFactory.cs`

**Intent**: Decide, from a completed transition, whether a notification is owed and to whom, without coupling the mapping to database access.

**Contract**: Given the join request, its owning event, the actor, the effective target status, and whether the request was newly created, returns zero or one intent descriptor (recipient user id, type, deterministic event key, notification id). The rules:

| Situation | Recipient | Type |
|---|---|---|
| New request left `Pending` | Organizer | `JoinRequestCreated` |
| New request auto-accepted | Organizer | `JoinRequestCreated` |
| `Pending` → `Accepted` (manual) | Requester | `JoinRequestAccepted` |
| `Pending` → `Rejected` | Requester | `JoinRequestRejected` |
| No state change (replay) | — | none |

Auto-accept notifies the **organizer only** — the requester already received `Accepted` synchronously in the HTTP response.

#### 6. Transition integration

**File**: `server/Events/EventEndpoints.cs`

**Intent**: Write the intent inside the transaction that performs the state change.

**Contract**: Between the existing `await dbContext.SaveChangesAsync(cancellationToken)` (`server/Events/EventEndpoints.cs:792`) and `await transaction.CommitAsync(cancellationToken)` (`:793`), call the intent factory and, when it yields an intent, `dbContext.PushOutbox.Add(...)` followed by a second `await dbContext.SaveChangesAsync(cancellationToken)`. This ordering is required so that `joinRequest.Id` — database-generated, `Guid.Empty` until the first save returns — is populated before it is used as the outbox `EventJoinRequestId` and in `EventKey`. No new transaction; both saves share the open transaction and the single `CommitAsync`.

Because the outbox row is only added after the domain save succeeds, the two `DbUpdateException` recovery paths (`server/Events/EventEndpoints.cs:799-821` unique-constraint, `:822-834` FK) cannot leave an orphaned `Added` `PushOutboxItem` in the change tracker — they roll back and detach `joinRequest` before any outbox entity exists. The three early-return paths that commit without a state change (`server/Events/EventEndpoints.cs:317-321`, `:684-691`, `:725-732`) are deliberately left untouched — none of them may enqueue.

A `DbUpdateException` on the outbox unique index is not expected within a locked transaction; if it occurs it propagates and rolls back the transaction. This is the one residual case where a notification failure affects the domain response, and it is accepted because `EventKey` is derived from `(joinRequestId, type, recipientUserId)`, all of which are unique per state transition under the row lock.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Migration applies cleanly: `dotnet ef database update --project server`

#### Manual Verification:

- A join request against a manual-accept event creates exactly one `PushOutbox` row addressed to the organizer, typed `JoinRequestCreated`
- Repeating that identical request creates **no** additional row
- Accepting creates one row addressed to the requester; accepting again creates none
- Rejecting creates one row addressed to the requester
- A join against an auto-accept event creates exactly one row — organizer, `JoinRequestCreated` — and none for the requester
- A join that fails on capacity or an ended event creates no row and the failure response is unchanged

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 5: Firebase Gateway & Delivery Worker

### Overview

Drain the outbox: claim work, fan out to each active installation, send through Firebase Admin, classify outcomes, retry with backoff, dead-letter what cannot succeed, and disable installations FCM reports as dead.

### Changes Required:

#### 1. Server package

**File**: `server/server.csproj`

**Intent**: Add the Firebase Admin SDK.

**Contract**: `PackageReference Include="FirebaseAdmin" Version="3.6.0"`, pinned.

#### 2. Firebase options

**File**: `server/Push/FirebasePushOptions.cs`

**Intent**: Bind and validate Firebase configuration at startup, following the `JwtOptions` pattern.

**Contract**: `SectionName = "Firebase"`; properties `ProjectId` and `ServiceAccountJson`, both required. Registered with `.ValidateDataAnnotations().ValidateOnStart()` so a misconfigured deployment fails fast rather than silently never sending. `ToString()`/logging must never include `ServiceAccountJson`.

#### 3. Gateway abstraction

**Files**: `server/Push/IPushGateway.cs`, `server/Push/PushSendOutcome.cs`

**Intent**: Isolate Firebase behind a gateway so delivery orchestration and payload construction do not depend directly on Firebase credentials or SDK details.

**Contract**: `Task<PushSendOutcome> SendAsync(PushMessage message, CancellationToken)`. `PushSendOutcome` carries a result kind (`Accepted`, `RetryableFailure`, `TerminalFailure`, `UnregisteredDestination`), an optional FCM message id, an optional error code string, and an optional retry-after hint.

#### 4. Firebase gateway

**File**: `server/Push/FirebasePushGateway.cs`

**Intent**: Own the single `FirebaseApp` instance and the only line in the codebase that knows whether the destination is a FID or a legacy token.

**Contract**: Singleton. Creates one `FirebaseApp` from `GoogleCredential.FromJson(options.ServiceAccountJson)` — parsed in memory, never written to disk and never via `GOOGLE_APPLICATION_CREDENTIALS`. Targets the destination with `Message.Fid` or `Message.Token` per the mode recorded in `binding-spike.md`. Sets `AndroidConfig` with `Priority = High`, `TimeToLive = TimeSpan.FromMinutes(30)`, and `Notification = new AndroidNotification { Tag = notificationId }`. The tag is **required for correctness, not cosmetics**: when the app is backgrounded the FCM SDK displays the notification itself and `OnMessageReceived` is never called, so the client-side tag applied by `PushNotificationPresenter` (Phase 6 change 4) does not apply. Each retry produces a distinct FCM message id, so without the server-set tag an application-level retry after a send that actually succeeded stacks a second system notification. Maps `FirebaseMessagingException` to `PushSendOutcome` and never rethrows into the worker loop.

#### 5. Payload construction (pure)

**File**: `server/Push/PushPayloadFactory.cs`

**Intent**: Build the notification and data payload — the enforcement point for the privacy boundary.

**Contract**: Given an outbox item, returns title, body, and the data dictionary. Title is the constant app name. Body is one of exactly three fixed strings, one per type — for example "Someone asked to join your event." / "Your join request was accepted." / "Your join request was declined." Data contains only `schemaVersion`, `type`, `eventId`, `joinRequestId`, `notificationId`, `sentAtUtc`, using the `PushPolicy` keys. This function takes **no** `User`, `SportsEvent`, or contact argument at all — the privacy guarantee is structural, not a matter of remembering not to include something.

#### 6. Failure classification (pure)

**File**: `server/Push/PushFailureClassifier.cs`

**Intent**: Turn a Firebase error into a retry decision, and compute the backoff schedule.

**Contract**: `Unregistered`/404 → `UnregisteredDestination` (disable the installation). `InvalidArgument`, `SenderIdMismatch`, and auth errors → `TerminalFailure`. `QuotaExceeded`/429, `Unavailable`/503, `Internal`/500 → `RetryableFailure`, honouring `Retry-After` when present and enforcing a 60-second floor where Firebase requires it. Backoff is exponential with jitter from attempt count, capped; `MaxAttempts = 5`, after which the delivery is dead-lettered. No retry loop wraps `SendAsync` — Firebase Admin already retries some transport failures internally, and application retries belong to the outbox schedule.

#### 7. Outbox processor

**File**: `server/Push/PushOutboxProcessor.cs`

**Intent**: Process one bounded pass over due work so delivery behaviour remains deterministic and can be invoked by the hosted worker or diagnostics.

**Contract**: Scoped. Claims a bounded batch (20) of **due** outbox items — `CompletedUtc IS NULL AND NextAttemptUtc <= now()`, ordered by `OccurredUtc` — with `FOR UPDATE SKIP LOCKED` inside a transaction, so future Railway replicas cannot double-send. The due-time predicate is what keeps backing-off items out of the batch; without it the oldest failing item would be re-claimed every poll and starve fresh work. On first claim, snapshots the recipient's currently active installations (`DisabledUtc IS NULL`) into `PushDelivery` rows — the snapshot is what makes the send set stable across retries. Sends only deliveries that are pending and due, records each outcome individually, disables the installation on `UnregisteredDestination`, and completes the outbox item once every delivery is accepted, dead-lettered, or terminal. Before the pass ends, the item's `NextAttemptUtc` is recomputed as the minimum `NextAttemptUtc` across its still-pending deliveries, so the item next becomes visible exactly when its earliest delivery is due. An outbox item with zero active installations completes immediately — a user with no device is not an error.

#### 8. Hosted worker

**File**: `server/Push/PushDeliveryWorker.cs`

**Intent**: Run the processor continuously.

**Contract**: `BackgroundService` polling every 2 seconds (fast enough that queue wait is a small fraction of the 30-second budget). Singleton, so it creates a **new DI scope per iteration** and resolves the processor there — it must never capture the scoped `ChoNaBojoContext`. Every iteration is wrapped in try/catch so one failure cannot kill the loop; failures are logged and the loop continues.

#### 9. Structured logging

**File**: `server/Push/PushDeliveryWorker.cs`, `server/Push/PushOutboxProcessor.cs`

**Intent**: Make the 30-second SLO measurable and failures diagnosable without leaking anything sensitive.

**Contract**: Logs outbox id, delivery id, notification type, attempt count, queue age (claim time − `OccurredUtc`), send duration, FCM message id, and error code. Never logs the service-account JSON, the raw registration id (a one-way hash only), the payload body, or any user identity beyond opaque ids.

#### 10. DI registration

**File**: `server/Program.cs`

**Intent**: Wire options, gateway, processor, and worker.

**Contract**: Options bound from the `Firebase` section with `ValidateOnStart`; `IPushGateway` → `FirebasePushGateway` singleton; processor scoped; `AddHostedService<PushDeliveryWorker>()`. Registered alongside the existing service registrations (`server/Program.cs:92-95`).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- API starts with Firebase configuration present and fails fast with a clear message when `Firebase:ServiceAccountJson` is missing

#### Manual Verification:

- With the app registered on a device, a real join request produces a delivered notification within 30 seconds
- Accept and reject each deliver to the requester's device
- The same account on two devices receives the notification on both
- Sending to a manually corrupted registration id disables that installation and does not retry it
- Stopping the API mid-queue and restarting it delivers the pending notification (durability)
- A backing-off item (forced by a corrupted registration id) is not re-claimed on every poll, and a fresh notification queued behind it still arrives within 30 seconds
- Worker logs show queue age and send duration, and contain no registration ids, payload bodies, or credentials

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 6: Android Delivery, Permission & Tap Routing

### Overview

Make the notification behave correctly on the device: a stable channel, a permission prompt asked at the right moment, sane foreground behaviour, deduplication, and a tap that lands on refreshed My events — in both cold and warm starts.

### Changes Required:

#### 1. Notification channel

**Files**: `app/ChoNaBojoApp/Platforms/Android/Push/NotificationChannels.cs`, `app/ChoNaBojoApp/Platforms/Android/MainApplication.cs`

**Intent**: Create the `event_updates` channel before any notification is posted, so SDK-displayed background notifications land in a properly configured channel.

**Contract**: Channel id `event_updates` (matching the manifest default from Phase 1), user-visible name "Event updates", `NotificationImportance.High` so join-request alerts surface as heads-up. Created in `MainApplication.OnCreate`, guarded for API 26+. Creation is idempotent, but importance is immutable after first creation — this value is a one-way decision.

#### 2. Permission request

**Files**: `app/ChoNaBojoApp/Services/Push/IPushPermissionService.cs`, `app/ChoNaBojoApp/Services/Push/PushPermissionService.cs`, `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`

**Intent**: Ask for `POST_NOTIFICATIONS` once, on the first authenticated screen, with a rationale — never from `MauiProgram` or `LoadingPage`.

**Contract**: Requests `Permissions.PostNotifications` on `MapPage`'s first appearance after the authenticated root is set, showing a short in-app rationale before the system dialog. Asks at most once per install (tracked in `Preferences`); a denial is recorded and never re-prompted automatically, and must not block or degrade any other feature. On API < 33 the permission is implicitly granted and the prompt is skipped.

**Sequencing is load-bearing and the call site is not obvious.** The location prompt is *not* on `MapPage`: `app/ChoNaBojoApp/Views/MapPage.xaml.cs:226` only inspects status, while the actual `Permissions.RequestAsync<Permissions.LocationWhenInUse>` runs in `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:1019`, reached asynchronously from `AppearingCommand` (fired at `app/ChoNaBojoApp/Views/MapPage.xaml.cs:61`). Firing the notification request from `OnAppearing` would therefore race an in-flight location request, and MAUI's Android permission implementation does not queue concurrent requests. The notification request must be **chained inside the existing `AppearingCommand` flow in `MapViewModel`, awaited only after the location request has resolved** — not issued independently from the page.

#### 3. Foreground message handling

**File**: `app/ChoNaBojoApp/Platforms/Android/Push/ChoNaBojoMessagingService.cs`

**Intent**: Handle messages arriving while the app is in the foreground, where the SDK does not display anything.

**Contract**: `OnMessageReceived` validates the data payload (known `schemaVersion`, known `type`, well-formed GUIDs) and ignores anything else silently. It posts a local notification through the presenter and requests a My events refresh on the main thread. Total work stays well inside the 20-second callback budget; no UI, no navigation, and no Shell access from this thread. `OnDeletedMessages` triggers a full refresh from the API rather than attempting to reconstruct missed transitions.

#### 4. Notification presenter

**File**: `app/ChoNaBojoApp/Platforms/Android/Push/PushNotificationPresenter.cs`

**Intent**: Post the foreground notification with deduplication and a tap intent that carries the payload.

**Contract**: Builds a notification on the `event_updates` channel using `notificationId` as the notification **tag**, so a duplicate delivery of the same logical notification replaces rather than stacks. This covers the foreground path only; the backgrounded path is covered by the identical tag set server-side in `AndroidConfig` (Phase 5 change 4). The content intent targets `MainActivity` with the validated ids as extras, using `PendingIntentFlags.Immutable | UpdateCurrent`.

#### 5. Tap routing

**Files**: `app/ChoNaBojoApp/Services/Push/IPushNavigationRouter.cs`, `app/ChoNaBojoApp/Services/Push/PushNavigationRouter.cs`

**Intent**: Hold a validated pending navigation until the app is actually able to navigate, then consume it exactly once.

**Contract**: `TryEnqueue(payload)` accepts only known types and well-formed ids; `Task ConsumePendingAsync()` navigates to My events and forces a server refresh, and is a no-op when nothing is pending or the user is unauthenticated. `Clear()` is called on sign-out so a pending notification cannot navigate under a different account. Navigation happens on the main thread after the authenticated Shell root exists — never before session restoration completes (`app/ChoNaBojoApp/Views/LoadingPage.xaml.cs:35-44`).

#### 6. Activity intent handling

**File**: `app/ChoNaBojoApp/Platforms/Android/MainActivity.cs`

**Intent**: Capture the launch intent in both start modes. The activity is `LaunchMode.SingleTop` (`app/ChoNaBojoApp/Platforms/Android/MainActivity.cs:7`), so a tap while the app is running delivers to `OnNewIntent`, not `OnCreate`.

**Contract**: Overrides `OnCreate` (cold start: read extras from `Intent`) and `OnNewIntent` (warm start: read extras and call `SetIntent`), routing both into `TryEnqueue`. `OnNewIntent` additionally triggers `ConsumePendingAsync`, since the app is already past session restoration. Cold start defers consumption to the router's post-authentication trigger.

#### 7. Consumption and clearing triggers

**Files**: `app/ChoNaBojoApp/Views/LoadingPage.xaml.cs`, `app/ChoNaBojoApp/ViewModels/LoginViewModel.cs`, `app/ChoNaBojoApp/Services/Auth/SessionService.cs`

**Intent**: Consume pending navigation once the app is authenticated and ready; discard it on sign-out.

**Contract**: `ConsumePendingAsync` is invoked after `SetAppRoot()` and after a successful login. `Clear()` is invoked during sign-out alongside the Phase 3 clearing of the push uploaded-state markers.

### Success Criteria:

#### Automated Verification:

- Solution builds for both TFMs: `dotnet build solutions/ChoNaBojo.slnx`
- Merged manifest shows `minSdkVersion="29"`, `POST_NOTIFICATIONS`, and the messaging service

#### Manual Verification:

- Fresh install on Android 13+: the rationale and system permission prompt appear on first MapPage view *after* the location prompt resolves, not earlier and never simultaneously; denying leaves the app fully usable
- Android 10 device/emulator: no prompt appears and notifications still display
- App foregrounded: notification appears and My events reflects the change without a manual pull
- App backgrounded: notification appears; tapping opens My events with fresh data
- App removed from recents (cold start): tapping opens the app, restores the session, and lands on My events — never flashing protected content before authentication
- App running on a different page (warm `SingleTop`): tapping switches to My events
- Two deliveries of the same `notificationId` produce one notification, not two — verified with the app **backgrounded** (SDK-displayed, server tag) and again in the foreground (presenter tag)
- Notification content on the lock screen shows no name, contact detail, or event title
- Notification channel disabled in system settings: no crash; app remains usable
- Log out while a notification is pending, log in as another user: no navigation to the previous account's context

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 7: Deployment, SLO Measurement & Verification Matrix

### Overview

Configure production, measure the 30-second requirement with real numbers rather than impressions, and record the manual matrix.

### Changes Required:

#### 1. Railway configuration (manual)

**Intent**: Give the deployed API its Firebase credentials without putting them in the repository.

**Contract**: `Firebase__ProjectId` (plain) and `Firebase__ServiceAccountJson` (sealed, multiline), both scoped to the API service only. Never exposed to PR environments, MAUI builds, logs, or `/health`. The service must stay continuously running — a sleeping or scale-to-zero service cannot meet the 30-second SLO. This credential is distinct from `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` in the Android workflow; the two serve different APIs and must not be interchanged.

#### 2. Local development configuration

**Intent**: Let a developer run the pipeline locally without committing anything.

**Contract**: `Firebase:ProjectId` and `Firebase:ServiceAccountJson` via `dotnet user-secrets` on the server project. User-secrets are not encrypted and must hold development-only material.

#### 3. Setup documentation

**File**: `AGENTS.md`

**Intent**: Document Firebase setup the way the Google Maps API key is already documented, so the next contributor is not blocked.

**Contract**: A new "Firebase Cloud Messaging" section covering the Firebase project and package-name requirement, the committed `google-services.json`, the service-account role, the two Railway variables and their sealed status, and the user-secrets keys for local development. States explicitly that the service-account JSON is never committed and never passed to an app build.

#### 4. Latency measurement

**File**: `context/changes/push-notifications/reviews/manual-verification.md`

**Intent**: Turn "within 30 seconds" into recorded measurements against a defined population.

**Contract**: Records at least five join, accept, and reject runs against the deployed API with a real device, capturing commit → outbox claim → FCM acceptance (from worker logs) and observed display time. States the measured population explicitly: online Android device with Google Play services, notification permission granted, `event_updates` enabled. Notes that Firebase acceptance is not delivery and that offline, force-stopped, Doze-restricted, and permission-denied devices are excluded by definition.

#### 5. Device and state matrix

**File**: `context/changes/push-notifications/reviews/manual-verification.md`

**Intent**: Record the compatibility and lifecycle checks that build and configuration verification cannot cover.

**Contract**: Results for Android 10/API 29 and Android 13+/API 33; foreground / background / removed-from-recents / warm `SingleTop`; permission granted, denied, later enabled in settings, channel disabled; offline then reconnect; Doze/App Standby; one account on two devices; logout then a different account on the same device; clear data and reinstall; stale-installation cleanup. Includes the concurrency checks that cannot be automated under the chosen test scope: two API instances (or two rapid worker passes) must not double-send, and a successful device must not be re-sent when a sibling delivery retries.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Deployed API `/health` returns healthy after the Firebase variables are set

#### Manual Verification:

- Measured commit-to-display latency is under 30 seconds across all recorded runs
- No Firebase credential appears in any log, health response, or built artifact
- The full device/state matrix is recorded with outcomes
- `AGENTS.md` is sufficient for a fresh clone to reach a working push setup

**Implementation Note**: This is the final phase. After manual verification passes, the change is ready for `/10x-impl-review`.

---

## Verification Strategy

### Integration Tests:

Deliberately out of scope. Transactional atomicity, `FOR UPDATE SKIP LOCKED` claiming, and end-to-end delivery are Postgres- and device-specific and are verified manually per phase (Phase 4 manual criteria, Phase 5 manual criteria, and the Phase 7 matrix).

### Manual Testing Steps:

1. Fresh install, log in, confirm exactly one active installation row.
2. From a second account, request to join the first account's event; measure organizer notification latency.
3. Accept; measure requester notification latency. Repeat with reject.
4. Repeat the join twice identically; confirm exactly one notification and one outbox row.
5. Background, cold-start, and warm-start tap routing to My events.
6. Log out while online, confirm the row is disabled and the device stops receiving notifications.
6b. Log out while offline (and separately, let the refresh token expire), confirm sign-out completes and the row is knowingly left active until the next login re-claims it.
7. Log in as another account on the same device; confirm reassignment and correct targeting.
8. Corrupt a registration id in the database; confirm the installation is disabled after one failed send and not retried.
9. Stop the API with work queued, restart, and confirm delivery.
10. Run the Android 10 and Android 13+ compatibility passes.

## Performance Considerations

The 30-second budget decomposes as: transaction commit (immediate) → worker claim (≤2 s at a 2-second poll) → Firebase acceptance (typically sub-second) → FCM to device (variable, outside our control). The controllable portion is bounded by the poll interval, so queue age is the metric that matters; if p95 queue age approaches the poll interval consistently, the batch size is too small rather than the interval too long.

Batch size is capped at 20 items per pass to bound transaction duration while holding claim locks. Because the claim filters on `NextAttemptUtc <= now()`, retrying items drop out of the batch until they are actually due, so a degraded FCM cannot let a handful of failing recipients hold all 20 slots ahead of fresh work — head-of-line blocking is prevented structurally rather than by tuning. High FCM priority is used for all three types — they are genuinely time-sensitive, user-visible alerts — and the 30-minute TTL prevents a stale "your request was accepted" from arriving after the event.

The outbox and delivery tables grow monotonically. At MVP volume this is irrelevant; a retention/pruning job is deferred to S-07, which will already be touching this infrastructure.

## Migration Notes

Two additive migrations (`AddPushInstallations`, `AddPushOutbox`); no existing table is altered and no data is backfilled. Existing users have no installations until their app updates and registers, and an outbox item with no active installations completes immediately — so the pipeline is safe to deploy before any client update ships.

Deployment order is server-first: deploy the API (Phases 2, 4, 5) and apply migrations before the app release, so the first registering client has an endpoint to call. `POST /auth/logout` gains one optional field (`deviceRegistrationId`); an older client posting only `refreshToken` binds and behaves exactly as before, so the contract change is backward compatible in the server-first order. Rolling back is dropping the migrations; nothing in the join-request path depends on the outbox for its own correctness.

The `minSdk` 21→29 change drops devices below Android 10 at the next store release. This is intentional alignment with the PRD's stated support boundary, not a regression.

## Open Risks

**The registration id is a bearer-like secret, and `PUT /api/me/push-installations` is therefore a reassignment primitive.** The upsert matches on `DeviceRegistrationId` and, on match, reassigns `UserId` with no proof of device ownership. Any authenticated user who obtains another user's registration id can redirect that user's notifications to their own account and simultaneously deny the victim theirs. This is **accepted, not fixed**: reassignment is exactly what makes same-device account switching work, and the MVP has no device-attestation mechanism to distinguish a legitimate switch from a hijack.

Two mitigations are mandatory rather than optional, and both are structural:

- **Registration ids are never logged, echoed, or surfaced.** The server never returns the registration id in any response (Phase 2 change 1) and never writes it to logs (Phase 5 change 9). The same rule extends to the client: `PushRegistrationStore` (Phase 3 change 1) must never log or display the stored id, and it stays in `Preferences` — private to the app sandbox — rather than anywhere shared or exportable.
- **The blast radius is bounded by the payload.** Even a successful hijack leaks no contact detail, name, or event title, because the payload factory structurally cannot include them (Phase 5 change 5). The attacker learns only that *some* join-request activity occurred, and the victim notices lost notifications.

Post-MVP, the accepted route to closing this is proof-of-possession on registration (server-issued nonce echoed through an FCM message to the device before the row is reassigned). It is out of scope for S-06 and is recorded here so the decision is visible rather than implicit.

## References

- Research: `context/changes/push-notifications/research.md`
- Context7 upstream API notes: `context/changes/push-notifications/xamarin-firebase-messaging-context7.md`
- Roadmap slice: `context/foundation/roadmap.md` (S-06)
- Repository lessons applied: `context/foundation/lessons.md` (dependency-free shared projects; enum guarded at both layers; one error body shape per status code)
- Outbox insertion point: `server/Events/EventEndpoints.cs:641-810`
- Replay paths that must not enqueue: `server/Events/EventEndpoints.cs:317-321`, `:684-691`, `:725-732`, `:799-821`
- Enum CHECK-constraint precedent: `server/Data/ChoNaBojoContext.cs:277-282`
- Sign-out sequence and its no-`IApiService` invariant: `app/ChoNaBojoApp/Services/Auth/SessionService.cs:3-8`, `:88-125`
- Un-handled named client precedent: `app/ChoNaBojoApp/MauiProgram.cs:68-72`
- Session restoration boundary: `app/ChoNaBojoApp/Views/LoadingPage.xaml.cs:26-44`
- `SingleTop` activity: `app/ChoNaBojoApp/Platforms/Android/MainActivity.cs:7`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Firebase Setup & Android Binding Spike

#### Automated

- [x] 1.1 Android build succeeds — 1b2ff95
- [x] 1.2 Windows TFM builds (Firebase items correctly Android-conditional) — 1b2ff95
- [x] 1.3 Solution builds — 1b2ff95
- [x] 1.4 Merged manifest contains messaging service, POST_NOTIFICATIONS, minSdkVersion 29 — 1b2ff95

#### Manual

- [x] 1.5 Device logs a non-empty registration identifier on first launch — 1b2ff95
- [x] 1.6 Firebase Console test message displays on the device — 1b2ff95
- [x] 1.7 binding-spike.md records the resolved registration mode — 1b2ff95

### Phase 2: Push Installation Persistence & Registration API

#### Automated

- [x] 2.1 Solution builds — d25710b
- [x] 2.2 AddPushInstallations migration applies cleanly — d25710b
- [x] 2.3 Shared projects gained no framework dependencies — d25710b

#### Manual

- [x] 2.4 PUT creates one row and returns an installation id — d25710b
- [x] 2.5 Repeat PUT updates LastSeenUtc without a second row — d25710b
- [x] 2.6 Second user's token reassigns UserId on the same registration id — d25710b
- [x] 2.7 Endpoint returns 401 without a bearer token — d25710b
- [x] 2.8 Logout with deviceRegistrationId sets DisabledUtc and still returns 204 — d25710b
- [x] 2.9 Logout with another user's deviceRegistrationId returns 204 and changes nothing — d25710b
- [x] 2.10 Logout with no deviceRegistrationId behaves exactly as before — d25710b
- [x] 2.11 Blank registration id returns 400 as ValidationProblemResponse — d25710b

### Phase 3: Client Registration Lifecycle & Logout Unlink

#### Automated

- [x] 3.1 Solution builds for both TFMs — 10a6be4
- [x] 3.2 Android build succeeds — 10a6be4

#### Manual

- [x] 3.3 Fresh install and login creates exactly one installation row — 10a6be4
- [x] 3.4 Relaunch creates no duplicate row and refreshes LastSeenUtc — 10a6be4
- [x] 3.5 Online logout sets DisabledUtc — 10a6be4
- [x] 3.6 Second account on the same device reassigns and re-enables the row — 10a6be4
- [x] 3.7 Offline logout completes promptly without hanging and leaves the row active until re-claimed — 10a6be4
- [x] 3.8 Refresh-token expiry force sign-out leaves the row knowingly active — 10a6be4
- [x] 3.9 Same account on two devices yields two active rows — 10a6be4

### Phase 4: Transactional Outbox & Intent Creation

#### Automated

- [ ] 4.1 Solution builds
- [ ] 4.2 AddPushOutbox migration applies cleanly

#### Manual

- [ ] 4.3 Join request creates exactly one organizer-addressed outbox row
- [ ] 4.4 Identical repeat request creates no additional row
- [ ] 4.5 Accept creates one requester-addressed row; repeat accept creates none
- [ ] 4.6 Reject creates one requester-addressed row
- [ ] 4.7 Auto-accept join creates one organizer row and none for the requester
- [ ] 4.8 Failed join (capacity or ended event) creates no row and response is unchanged

### Phase 5: Firebase Gateway & Delivery Worker

#### Automated

- [ ] 5.1 Solution builds
- [ ] 5.2 API fails fast with a clear message when Firebase configuration is missing

#### Manual

- [ ] 5.3 Real join request delivers a notification within 30 seconds
- [ ] 5.4 Accept and reject both deliver to the requester's device
- [ ] 5.5 Same account on two devices receives on both
- [ ] 5.6 Corrupted registration id disables that installation without retry
- [ ] 5.7 API restart mid-queue still delivers pending notifications
- [ ] 5.8 Backing-off item is not re-claimed every poll and does not delay a notification queued behind it
- [ ] 5.9 Worker logs show queue age and duration with no credentials, registration ids, or payload bodies

### Phase 6: Android Delivery, Permission & Tap Routing

#### Automated

- [ ] 6.1 Solution builds for both TFMs
- [ ] 6.2 Merged manifest shows minSdkVersion 29, POST_NOTIFICATIONS, and the messaging service

#### Manual

- [ ] 6.3 Android 13+ prompt appears on first MapPage view after the location prompt resolves; denial leaves the app usable
- [ ] 6.4 Android 10 shows no prompt and still displays notifications
- [ ] 6.5 Foreground delivery refreshes My events without a manual pull
- [ ] 6.6 Background tap opens My events with fresh data
- [ ] 6.7 Cold-start tap restores the session before showing protected content
- [ ] 6.8 Warm SingleTop tap switches to My events
- [ ] 6.9 Duplicate notificationId produces one notification, backgrounded and foregrounded
- [ ] 6.10 Lock-screen content shows no name, contact detail, or event title
- [ ] 6.11 Disabled notification channel causes no crash
- [ ] 6.12 Pending navigation is discarded across a logout and different-account login

### Phase 7: Deployment, SLO Measurement & Verification Matrix

#### Automated

- [ ] 7.1 Solution builds
- [ ] 7.2 Deployed API /health returns healthy with Firebase variables set

#### Manual

- [ ] 7.3 Measured commit-to-display latency under 30 seconds across all recorded runs
- [ ] 7.4 No Firebase credential appears in logs, health responses, or built artifacts
- [ ] 7.5 Full device and state matrix recorded with outcomes
- [ ] 7.6 AGENTS.md is sufficient for a fresh clone to reach a working push setup
