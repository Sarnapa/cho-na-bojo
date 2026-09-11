---
date: 2026-09-10T15:28:13.846+02:00
researcher: GitHub Copilot
git_commit: d30615784ce75706a237da9f426255cbbbc37976
branch: master
repository: Sarnapa/cho-na-bojo
topic: "Firebase Cloud Messaging implementation and configuration for S-06 push notifications"
tags: [research, firebase-cloud-messaging, dotnet-maui, aspnet-core, android, railway]
status: complete
last_updated: 2026-09-10
last_updated_by: GitHub Copilot
---

# Research: Firebase Cloud Messaging implementation and configuration for S-06

**Date**: 2026-09-10T15:28:13.846+02:00  
**Researcher**: GitHub Copilot  
**Git Commit**: `d30615784ce75706a237da9f426255cbbbc37976`  
**Branch**: `master`  
**Repository**: `Sarnapa/cho-na-bojo`

## Research Question

How should ChoNaBojo implement Firebase Cloud Messaging for roadmap slice S-06, including the required Firebase, Android, ASP.NET Core, Railway, CI, security, lifecycle, reliability, and testing configuration?

## Summary

Implement S-06 as an Android-specific FCM client plus a server-owned delivery pipeline:

1. Use the Microsoft-maintained `Xamarin.Firebase.Messaging` Android binding directly, currently `125.1.1.1`, rather than `Plugin.Firebase.CloudMessaging`. The app is Android-only, and the direct binding exposes Firebase's newest installation-based API without an unnecessary cross-platform abstraction.
2. Use **Firebase Installation IDs (FIDs)** as delivery targets. Firebase Admin .NET 3.6.0 introduced `Message.Fid`/`MulticastMessage.Fids` and deprecated `Message.Token`/`Tokens` in July 2026. Do not build a new permanent registration-token model.
3. Store one push installation per app installation, not one value on `User`. A user may have multiple devices, and an installation may rotate or be reassigned after logout/account switching.
4. Persist notification intent in a PostgreSQL transactional outbox in the same transaction as the join-request state change. A hosted worker sends after commit. Never call FCM inside the HTTP/database transaction.
5. Use notification-plus-data messages: generic lock-screen-safe text plus opaque IDs (`eventId`, `joinRequestId`, `notificationId`). Fetch authoritative state from the authenticated API after a tap. Never put contact details, credentials, event descriptions, or full DTOs in the payload.
6. Configure the Android app with `google-services.json`, the FID opt-in metadata, `POST_NOTIFICATIONS`, a stable notification channel, and tap handling in both cold and warm starts. Ask for notification permission contextually after authentication, not during the loading screen.
7. Initialize one `FirebaseApp`/`FirebaseMessaging` instance in ASP.NET Core. On Railway, keep the dedicated service-account JSON in a sealed, service-scoped environment variable and parse it in memory; never commit it or put raw JSON into `GOOGLE_APPLICATION_CREDENTIALS`.
8. Treat the 30-second requirement as an observable SLO for online, permission-enabled devices. A successful Admin SDK call proves only that FCM accepted the message, not that Android displayed or received it.

The main version risk is new: FID support landed in Firebase during 2026, while the Microsoft binding currently wraps Firebase Messaging 25.1.1 and trails the latest native SDK. The first implementation phase should compile-spike the generated `OnRegistered`, `OnUnregistered`, `Register`, and `Unregister` C# signatures before building the rest of the client.

## Detailed Findings

### 1. Current integration points

S-06 already has stable domain triggers:

- `POST /api/events/{eventId}/join-requests` creates either a pending request or an immediately accepted request when auto-accept is enabled.
- `POST /api/events/{eventId}/join-requests/{requestId}/accept` and `/reject` resolve pending requests.
- All domain endpoints are below the authenticated `/api` group ([`server/Program.cs:97-103`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Program.cs#L97-L103)).
- The request, accept, and reject handlers converge on the same transition code ([`server/Events/EventEndpoints.cs:304-370`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Events/EventEndpoints.cs#L304-L370)).
- That transition locks the event row, validates state/capacity, writes the request, and commits one EF transaction ([`server/Events/EventEndpoints.cs:650-810`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Events/EventEndpoints.cs#L650-L810)).

The outbox record must be inserted before the existing `SaveChangesAsync` and committed with the join-request change. Sending before commit risks a push for rolled-back work; sending after commit without a durable outbox risks losing the push if the process exits between commit and FCM.

Recommended trigger mapping:

| Domain transition | Recipient | Push type |
|---|---|---|
| New request remains `Pending` | Event organizer | `join_request_created` |
| Existing `Pending` becomes `Accepted` | Requester | `join_request_accepted` |
| Existing `Pending` becomes `Rejected` | Requester | `join_request_rejected` |
| Exact API replay/no state change | Nobody | No new outbox record |

Auto-accept needs an explicit product decision. Recommended behavior is one organizer push such as "A new participant joined your event" and no requester self-push because the requester receives the accepted result synchronously. Whichever policy is selected, encode it as a unique transition key so retries do not create duplicates.

### 2. Client SDK choice and FID migration

**Recommendation:** pin `Xamarin.Firebase.Messaging` `125.1.1.1` for the Android target and use a small Android-specific service.

Why:

- It is Microsoft-maintained and contains a `net10.0-android36.0` asset.
- It wraps Firebase Messaging 25.1.1.
- The app is Android-only for the MVP, so a cross-platform plugin adds little value.
- `Plugin.Firebase.CloudMessaging` 4.0.1 still exposes the legacy `GetToken`/`OnNewToken` lifecycle in its published implementation and depends on an older messaging baseline.
- Old packages such as `Plugin.FirebasePushNotification` should not be introduced into a new .NET 10 app.

Firebase's current Android flow invokes `FirebaseMessagingService.OnRegistered(installationId)` and uses `FirebaseMessaging.Register()` when manual registration is needed. It requires:

```xml
<meta-data
    android:name="firebase_messaging_installation_id_enabled"
    android:value="true" />
```

Use FID mode consistently. Do not combine FID registration with `GetToken`/`OnNewToken`: the official API treats them as different modes. On the server, target the installation through `Message.Fid`.

The direct binding currently trails native Firebase Messaging 25.1.3. In particular, native 25.1.2 fixed an `FID_ALREADY_USED` case. Pin the binding instead of floating it, test reinstall/restore/account switching, and check for a newer Microsoft binding immediately before release.

Primary evidence:

- [Firebase: access the Firebase Installation ID](https://firebase.google.com/docs/cloud-messaging/android/get-started#access-the-firebase-installation-id)
- [FirebaseMessagingService Android reference](https://firebase.google.com/docs/reference/android/com/google/firebase/messaging/FirebaseMessagingService)
- [Firebase Admin .NET 3.6.0 release notes](https://firebase.google.com/support/release-notes/admin/dotnet)
- [Xamarin.Firebase.Messaging 125.1.1.1](https://www.nuget.org/packages/Xamarin.Firebase.Messaging/125.1.1.1)
- [Firebase Android SDK release notes](https://firebase.google.com/support/release-notes/android)

### 3. Firebase Console configuration

Create separate Firebase projects for development/staging and production if schedule permits. At minimum, never let development sends target production installations.

For each Firebase environment:

1. Create/select the Firebase project.
2. Register an Android app whose package name is exactly `com.cho_na_bojo`. This must case-sensitively match the existing MAUI `ApplicationId` ([`app/ChoNaBojoApp/ChoNaBojoApp.csproj:25-34`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/ChoNaBojoApp.csproj#L25-L34)).
3. Enable the Firebase Cloud Messaging HTTP v1 API. Do not use a legacy FCM server key; the legacy APIs were retired.
4. Download `google-services.json` for the Android app without renaming it.
5. Create a dedicated server service account with the narrow **Firebase Cloud Messaging API Admin** role rather than Owner/Editor.
6. Generate a service-account key only if Workload Identity Federation is not practical on Railway. Keep development and production credentials separate.

`google-services.json` is client configuration, not the service-account private key. Its identifiers are embedded in the APK and it is not sufficient to send server messages. It may be committed under Firebase's normal model, but using a CI-injected file keeps environment selection explicit. The service-account JSON must never be committed or bundled into the app.

FCM itself does not require an Android signing SHA-1. Do not conflate its setup with the existing Maps API key restrictions.

### 4. MAUI project and Android configuration

The project currently has no Firebase package/config item ([`app/ChoNaBojoApp/ChoNaBojoApp.csproj:48-82`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/ChoNaBojoApp.csproj#L48-L82)). Add Android-conditional items conceptually as follows:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
  <PackageReference Include="Xamarin.Firebase.Messaging"
                    Version="125.1.1.1" />
  <GoogleServicesJson Include="google-services.json" />
</ItemGroup>
```

If `google-services.json` is CI-injected, make its path/property conditional but fail release builds clearly when absent. Inspect the merged Debug and Release manifests because MAUI combines the source manifest, assembly attributes, and dependency manifests.

The current source manifest has only network/location permissions and Maps metadata ([`AndroidManifest.xml:1-14`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml#L1-L14)). Add:

```xml
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />

<application ...>
  <meta-data
      android:name="firebase_messaging_installation_id_enabled"
      android:value="true" />
  <meta-data
      android:name="com.google.firebase.messaging.default_notification_icon"
      android:resource="@drawable/ic_stat_notification" />
  <meta-data
      android:name="com.google.firebase.messaging.default_notification_color"
      android:resource="@color/notification_color" />
  <meta-data
      android:name="com.google.firebase.messaging.default_notification_channel_id"
      android:value="event_updates" />
</application>
```

Provide a monochrome Android small icon. Create one stable channel before posting:

| Setting | Value |
|---|---|
| ID | `event_updates` |
| Name | `Event updates` |
| Importance | `Default` |
| Purpose | Join requests and request decisions |

Channel creation is idempotent, but its importance/sound cannot be changed programmatically after creation. A materially different behavior later requires a new channel ID.

The app declares API 21 while the product supports Android 10/API 29+ ([`ChoNaBojoApp.csproj:36-43`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/ChoNaBojoApp.csproj#L36-L43)). S-06 does not require changing the minimum, but raising it to 29 in a separate explicit decision would align packaging with the stated support boundary.

Sources:

- [Firebase Android setup](https://firebase.google.com/docs/android/setup)
- [Google Services plugin and JSON processing](https://firebase.google.com/docs/android/google-services-plugin-and-file)
- [MAUI Android manifest merging](https://learn.microsoft.com/en-us/dotnet/maui/android/manifest?view=net-maui-10.0)
- [Android notification channels](https://developer.android.com/develop/ui/views/notifications/channels)

### 5. Android permission, registration, and lifecycle

On Android 13/API 33+, a fresh install cannot display notifications until `POST_NOTIFICATIONS` is granted. Android 10-12 needs no runtime prompt. Use MAUI's `Permissions.PostNotifications` only after the first authenticated page is visible and after explaining the benefit. Denial must not block the app.

This matters because startup currently restores `SecureStorage` from `LoadingPage` and only then selects the authenticated root ([`LoadingPage.xaml.cs:20-44`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Views/LoadingPage.xaml.cs#L20-L44)). Do not show the permission prompt from `MauiProgram` or before that flow completes.

Implement an Android `FirebaseMessagingService`:

```csharp
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class ChoNaBojoMessagingService : FirebaseMessagingService
{
    public override void OnRegistered(string installationId)
    {
        // Store as pending locally and schedule authenticated upload.
    }

    public override void OnUnregistered(string installationId)
    {
        // Disable the server association when authentication/network permits.
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        // Validate data and show/update foreground UI.
    }

    public override void OnDeletedMessages()
    {
        // Request a full server-state refresh.
    }
}
```

Confirm the exact generated method signatures against the pinned binding before treating this sample as compilable code.

Registration lifecycle:

1. `OnRegistered` may run before authentication; persist the FID locally as pending.
2. After session restoration or login, idempotently upload the FID to the authenticated API.
3. Upload again whenever registration fires and refresh `LastSeenUtc`.
4. On account switching, atomically reassign that installation to the newly authenticated user.
5. On logout, unlink the current installation before or as part of server logout. The current session service clears local auth before its best-effort server call ([`SessionService.cs:77-125`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Services/Auth/SessionService.cs#L77-L125)); S-06 must deliberately redesign this sequence so a logged-out phone does not remain subscribed to the previous account.
6. Do not rely on uninstall callbacks. Remove dead installations when FCM returns a terminal unregistered error and prune stale records.

Firebase recommends storing a registration/update timestamp. It treats one month of inactivity as stale for operational purposes and expires Android registrations after 270 days of inactivity.

Sources:

- [Android notification runtime permission](https://developer.android.com/develop/ui/views/notifications/notification-permission)
- [.NET MAUI notification permission](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.applicationmodel.permissions.postnotifications?view=net-maui-10.0)
- [Firebase registration management](https://firebase.google.com/docs/cloud-messaging/manage-tokens)
- [Firebase installation lifecycle](https://firebase.google.com/docs/projects/manage-installations)

### 6. Client/server registration contract

Add contracts to the dependency-free shared Contracts project, following the repository rule for wire DTOs:

```text
PUT /api/me/push-installations
Authorization: Bearer <JWT>
{
  "firebaseInstallationId": "...",
  "appVersion": "..."
}

DELETE /api/me/push-installations/{opaqueInstallationRecordId}
Authorization: Bearer <JWT>
```

The server must derive `UserId` from the JWT and must not accept one in the body. The upsert should:

- validate a bounded non-empty FID;
- create or update one row identified by unique FID;
- atomically reassign the FID when the device signs into another account;
- update `LastSeenUtc`;
- return an opaque server record ID for later unlinking;
- never echo or log the full FID.

Recommended minimal table:

```text
PushInstallations
- Id uuid primary key
- UserId uuid foreign key Users
- FirebaseInstallationId text unique
- AppVersion text nullable
- CreatedUtc timestamp
- LastSeenUtc timestamp
- DisabledUtc timestamp nullable
```

Do not store a single FID on `User`. One user can legitimately have several active app installations. Android is the only platform in scope, so a persisted platform enum is unnecessary now. If a platform/status enum is added, apply the repository lesson: validate it in application code and enforce the defined values with a database `CHECK` constraint.

### 7. Server SDK, credentials, and DI

Add `FirebaseAdmin` `3.6.0` to the .NET 10 server. The current server has no Firebase dependency and already uses environment-bound configuration and DI ([`server/server.csproj:1-27`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/server.csproj#L1-L27), [`server/Program.cs:14-95`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Program.cs#L14-L95)).

Initialize one `FirebaseApp` for the process and wrap `FirebaseMessaging` behind a testable singleton such as `IPushGateway`. Do not create Firebase clients per request and do not call `FirebaseMessaging.DefaultInstance` throughout endpoint code. A singleton hosted worker must create a DI scope for each EF Core unit of work rather than capturing the scoped `ChoNaBojoContext`.

Conceptual configuration:

```text
Firebase:ProjectId
Firebase:ServiceAccountJson
```

Credential strategy:

- **Local development:** prefer Application Default Credentials, or put development-only JSON in ASP.NET Core user-secrets. User-secrets are not encrypted and must never hold production material.
- **Railway MVP:** add `Firebase__ProjectId` and a sealed, service-scoped multiline `Firebase__ServiceAccountJson`. Parse the JSON directly in memory using the typed Google credential factory.
- **Do not** place raw JSON in `GOOGLE_APPLICATION_CREDENTIALS`; ADC interprets that variable as a path to a credentials file.
- **Longer term:** Workload Identity Federation is preferred for non-Google workloads because it avoids long-lived service-account keys, but Railway does not currently document a turnkey Google federation integration.

Keep the Firebase sender account distinct from `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` in the Android publishing workflow. They serve different APIs and should have different roles.

Sources:

- [Firebase Admin SDK setup](https://firebase.google.com/docs/admin/setup)
- [Firebase Admin send API](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk)
- [Google Application Default Credentials](https://cloud.google.com/docs/authentication/application-default-credentials)
- [Railway variables](https://docs.railway.com/variables)
- [Railway sealed variables](https://docs.railway.com/guides/managing-secrets-on-railway)

### 8. Transactional outbox and delivery worker

Use a logical outbox plus per-installation delivery rows:

```text
PushOutbox
- Id
- EventKey unique
- RecipientUserId
- Type
- EventId
- JoinRequestId
- OccurredUtc
- AvailableUtc
- CompletedUtc nullable

PushDeliveries
- Id
- PushOutboxId
- PushInstallationId
- AttemptCount
- NextAttemptUtc
- AcceptedUtc nullable
- LastErrorCode nullable
- FcmMessageId nullable
```

Flow:

1. The join-request transition inserts one `PushOutbox` record with a deterministic key, for example `join-request:{requestId}:accepted`.
2. The domain mutation and outbox record commit atomically.
3. A `BackgroundService` polls approximately every 1-2 seconds.
4. It claims work transactionally (`FOR UPDATE SKIP LOCKED` is suitable for future multiple Railway replicas).
5. On first processing, it snapshots currently active installations into unique delivery rows.
6. It sends only pending/retryable delivery rows, records individual outcomes, and completes the logical outbox item when every delivery is successful or terminal.

The delivery table prevents retrying successful devices when only one device failed. The unique event key prevents duplicate logical notifications on idempotent HTTP replays.

Use high FCM priority only for these genuinely time-sensitive, user-visible alerts. Set a bounded TTL so a stale request update is not delivered days later; 15-60 minutes is a reasonable S-06 starting range, with the exact value decided in planning.

Do not claim exactly-once delivery. If FCM accepts a send but the response is lost, a retry can duplicate it. Put deterministic `notificationId` in the data payload, use it as the Android notification tag/deduplication key, and make tap handling idempotent.

### 9. Message payload and privacy boundary

Use notification-plus-data:

```json
{
  "notification": {
    "title": "Cho Na Bojo",
    "body": "Your join request was accepted."
  },
  "data": {
    "schemaVersion": "1",
    "type": "join_request_accepted",
    "eventId": "opaque-guid",
    "joinRequestId": "opaque-guid",
    "notificationId": "opaque-guid",
    "sentAtUtc": "2026-09-10T13:28:13Z"
  }
}
```

Never include:

- phone numbers, email addresses, or messenger handles;
- access/refresh tokens;
- user-authored event title or description;
- participant names unless a later privacy review explicitly approves lock-screen exposure;
- full event, user, or request DTOs.

The payload is an untrusted hint. On tap, parse only known schema/type values, validate GUIDs, require an authenticated session, and fetch current state from the API. Server authorization remains authoritative. This keeps push delivery from bypassing the contact-reveal boundary.

Notification-plus-data is preferred over data-only for S-06 because Android can display the user-visible notification while the app is backgrounded. In the foreground, `OnMessageReceived` should refresh visible state and optionally post a local notification. Longer work belongs in WorkManager, not the messaging callback.

Sources:

- [FCM message types](https://firebase.google.com/docs/cloud-messaging/customize-messages/set-message-type)
- [Receiving Android messages](https://firebase.google.com/docs/cloud-messaging/android/receive-messages)
- [Android message priority](https://firebase.google.com/docs/cloud-messaging/android/message-priority)

### 10. Notification tap and app navigation

The launcher activity already uses `LaunchMode.SingleTop` ([`MainActivity.cs:1-10`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Platforms/Android/MainActivity.cs#L1-L10)). Handle notification extras in:

- `MainActivity.OnCreate` for a cold start;
- `MainActivity.OnNewIntent` for a running/suspended activity.

Do not navigate from `FirebaseMessagingService`. Store a validated pending navigation request and release it only after session restoration and Shell creation. Register the push registration/routing services alongside the existing singleton services in `MauiProgram` ([`MauiProgram.cs:54-106`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/MauiProgram.cs#L54-L106)).

For the MVP, route all three S-06 types to **My events**, refresh from the server, and focus the relevant event/request when practical. The existing event detail modal expects a full object and is not an ID-loading deep-link target. A dedicated event details route can be a later enhancement.

Clear pending notification navigation during logout/account switching. Unknown types, versions, malformed IDs, or unauthorized resources must be ignored safely and logged without payload contents.

### 11. Failure handling and cleanup

Classify Firebase failures:

| Error | Handling |
|---|---|
| `Unregistered` / HTTP 404 | Disable the installation immediately |
| `InvalidArgument` | Terminal; disable installation only if the payload is independently known valid |
| `SenderIdMismatch` / auth errors | Terminal configuration alert; do not retry as a device problem |
| `QuotaExceeded` / HTTP 429 | Reschedule with exponential backoff; use at least 60 seconds where Firebase requires it |
| `Unavailable` / HTTP 503 | Honor `Retry-After`; exponential backoff plus jitter |
| `Internal` / HTTP 500 | Retry with exponential backoff plus jitter |
| Cancellation/ambiguous transport failure | Retry, accepting duplicate-delivery risk |

Firebase Admin already retries some transport/503 failures internally. Do not stack an aggressive immediate retry loop around `SendAsync`. Application-level retries belong in the outbox schedule and need maximum attempts plus an expiry/dead-letter state.

Log only outbox/delivery IDs, notification type, attempt, duration, FCM message ID, error code, queue age, and a one-way hash of the FID. Never log service-account JSON, raw FIDs, or payload bodies.

Sources:

- [FCM error codes](https://firebase.google.com/docs/cloud-messaging/error-codes)
- [FCM retry and scaling guidance](https://firebase.google.com/docs/cloud-messaging/scale-fcm)

### 12. Railway and GitHub Actions

Railway API service:

- `Firebase__ProjectId`: non-secret project identifier.
- `Firebase__ServiceAccountJson`: sealed multiline secret, scoped only to the API service.
- Keep development/staging/production values separate.
- Do not expose the sender credential to PR environments, MAUI builds, logs, or health endpoints.
- Ensure the API service/worker is continuously running; queue delivery cannot meet the 30-second SLO if the service sleeps or is scaled to zero.

Android workflow:

- The existing workflow currently passes Maps and Google Play publishing secrets but no Firebase client config ([`.github/workflows/android-deploy.yml:43-113`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/.github/workflows/android-deploy.yml#L43-L113)).
- If `google-services.json` is not committed, store its base64 form as a GitHub environment/repository secret, decode it into the MAUI project before restore/build, and fail clearly when absent.
- The Android build does **not** need the Firebase server service-account JSON.
- Unit/integration tests must use a fake `IPushGateway`; only opt-in staging tests should receive real Firebase credentials.

### 13. Verifying the 30-second requirement

FCM acceptance is not delivery. Measure four timestamps:

1. join-request transaction committed;
2. outbox item claimed;
3. FCM accepted the message;
4. client callback/notification observed.

For an actionable SLO, define the measured population as online Android devices with Google Play services, granted app notification permission, and an enabled `event_updates` channel. Offline, force-stopped, Doze/OEM-restricted, or permission-denied devices cannot have a guaranteed 30-second display time.

Recommended operational measures:

- outbox depth and oldest age;
- commit-to-FCM-acceptance p50/p95/p99;
- client acknowledgement latency for staging/manual SLO runs;
- success/failure by Firebase error code;
- invalid installation cleanup count;
- retry/dead-letter count.

Firebase delivery reporting is delayed and cannot validate a live 30-second threshold by itself.

Source: [FCM delivery reporting](https://firebase.google.com/docs/cloud-messaging/understand-delivery)

### 14. Test strategy

There is no FCM emulator in the Firebase Local Emulator Suite, so final delivery tests require a real Firebase project.

Automated tests:

- notification intent is created for each real transition and not for idempotent replay;
- domain change and outbox intent commit or roll back together;
- payloads contain only approved generic text and IDs;
- one user fans out to multiple installations;
- account switching reassigns an installation;
- logout unlinks only the current installation;
- concurrent workers cannot claim the same delivery;
- successful installations are not resent when another installation retries;
- `Unregistered` disables a destination;
- transient errors reschedule with bounded backoff;
- duplicate `notificationId` is ignored on the client;
- malformed or unauthorized tap data cannot navigate to private content.

Manual/staging matrix:

- Android 10/API 29, Android 13/API 33, and the current target API emulator;
- emulator image with Google Play services plus at least one real device;
- foreground, background, removed-from-recents, cold start, and warm `SingleTop` tap;
- permission granted, denied, later enabled, and channel disabled;
- offline/reconnect, Doze/App Standby, and OEM battery restrictions;
- one account on two devices;
- logout then another account login on the same device;
- clear data, uninstall/reinstall, and backup/restore;
- invalid/stale FID cleanup;
- measured join/accept/reject latency.

Use Admin SDK dry-run for payload validation, but do not count it as delivery validation.

## Proposed Implementation Shape

Likely additions for `/10x-plan push-notifications`:

| Area | Likely artifacts |
|---|---|
| Shared contracts | Installation upsert/delete DTOs; push type/schema constants |
| Server persistence | `PushInstallation`, `PushOutbox`, `PushDelivery`; EF mappings; migration and constraints |
| Server delivery | Firebase options, singleton gateway, scoped outbox processor, hosted worker, retry classification |
| Domain integration | Outbox insertion in the join-request transition before the existing commit |
| Server endpoints | Authenticated `/api/me/push-installations` upsert/unlink |
| Android integration | Firebase package/JSON, manifest metadata/permission, notification icon/color/channel |
| Client services | FID registration coordinator, authenticated API registration, foreground handling, pending navigation router |
| Activity/startup | `OnCreate`/`OnNewIntent`; defer routing until authenticated root is ready |
| Logout | Server unlink coordinated with auth revocation before local identity is discarded |
| Deployment | Railway Firebase variables; optional CI injection of `google-services.json` |
| Validation | Unit/integration tests plus staged device matrix and latency measurement |

Suggested implementation order:

1. Compile-spike FID APIs in the current binding and lock package versions.
2. Add Firebase projects/configuration and prove one staging FID send.
3. Add installation contracts, persistence, and authenticated registration lifecycle.
4. Add transactional outbox and fake gateway tests.
5. Add Firebase gateway/worker and failure cleanup.
6. Add Android permission/channel/message/tap handling.
7. Integrate logout/account switching.
8. Run end-to-end state and latency matrix.

## Code References

- [`context/foundation/roadmap.md:156-167`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/context/foundation/roadmap.md#L156-L167) - S-06 outcome, prerequisites, configuration unknown, and latency risk.
- [`server/Events/EventEndpoints.cs:304-370`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Events/EventEndpoints.cs#L304-L370) - join, accept, and reject entry points.
- [`server/Events/EventEndpoints.cs:650-810`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Events/EventEndpoints.cs#L650-L810) - shared transaction and correct outbox insertion boundary.
- [`server/Program.cs:14-103`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/Program.cs#L14-L103) - current configuration, EF, DI, authentication, and protected API group.
- [`server/server.csproj:1-27`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/server/server.csproj#L1-L27) - .NET 10 server and current dependencies.
- [`app/ChoNaBojoApp/ChoNaBojoApp.csproj:10-82`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/ChoNaBojoApp.csproj#L10-L82) - Android target, package ID, minimum API, and current package set.
- [`app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml:1-14`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml#L1-L14) - current permissions and metadata.
- [`app/ChoNaBojoApp/Platforms/Android/MainActivity.cs:1-10`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Platforms/Android/MainActivity.cs#L1-L10) - `SingleTop` activity requiring cold/warm intent handling.
- [`app/ChoNaBojoApp/Views/LoadingPage.xaml.cs:20-44`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Views/LoadingPage.xaml.cs#L20-L44) - session restoration boundary before notification navigation.
- [`app/ChoNaBojoApp/Services/Auth/SessionService.cs:77-125`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/app/ChoNaBojoApp/Services/Auth/SessionService.cs#L77-L125) - logout sequencing that S-06 must integrate with.
- [`.github/workflows/android-deploy.yml:43-113`](https://github.com/Sarnapa/cho-na-bojo/blob/d30615784ce75706a237da9f426255cbbbc37976/.github/workflows/android-deploy.yml#L43-L113) - release build and existing secret injection.

## Architecture Insights

- The shared join-request transition is an unusually strong integration point: it already centralizes manual and auto-accept behavior and supplies the transaction needed by a reliable outbox.
- Push delivery is eventually consistent with domain state. The API response remains authoritative; notification failure must never roll back a join/accept/reject action.
- Installation identity belongs to an app installation and authenticated association, not to the user aggregate itself.
- A notification is a signal to refetch, not a transport for protected state.
- S-06 should establish reusable outbox/delivery infrastructure because S-07 needs the same channel for cancel/remove/leave notifications.
- Persisted notification-type enums require both application validation and database constraints under the repository's existing lessons.

## Historical Context (from prior changes)

- `context/archive/2026-09-06-event-listing-and-join-request/` established idempotent join-request creation and the shared transition path consumed by S-06.
- `context/archive/2026-09-07-approval-and-contact-reveal/` established manual accept/reject, transactional slot claims, and the strict contact privacy boundary.
- `context/foundation/lessons.md` requires shared wire DTOs to remain dependency-free and persisted enums to be guarded in both application and database layers.
- `context/foundation/roadmap.md` originally referred to an "FCM Server Key"; current Firebase uses Admin SDK/HTTP v1 OAuth credentials, so planning must not follow legacy server-key tutorials.

## Related Research

No other push-notification research artifact exists in `context/changes/**/research.md` or `context/archive/**/research.md`.

## Open Questions

1. **Binding verification:** Does `Xamarin.Firebase.Messaging` 125.1.1.1 expose the new FID methods with the expected generated C# signatures in this exact .NET 10 MAUI project? Resolve with the first compile spike.
2. **Environment split:** Will MVP use one Firebase project or separate development/staging and production projects? Separate projects are recommended; one project is faster but increases accidental cross-environment send risk.
3. **Auto-accept notification:** Should an auto-accepted request notify only the organizer that a participant joined, or also push the requester despite the synchronous accepted response? Organizer-only is recommended.
4. **Permission UX:** Which authenticated screen should explain and request notifications? The first stable app screen after login/session restoration is recommended, never the loading page.
5. **Client configuration storage:** Commit `google-services.json` as non-secret client configuration or inject it through GitHub Actions? Injection is recommended if separate Firebase environments are adopted.

## External Sources

- [Firebase Cloud Messaging: Android setup](https://firebase.google.com/docs/cloud-messaging/android/get-started)
- [Firebase Cloud Messaging: receive Android messages](https://firebase.google.com/docs/cloud-messaging/android/receive-messages)
- [Firebase Cloud Messaging: message types](https://firebase.google.com/docs/cloud-messaging/customize-messages/set-message-type)
- [Firebase Cloud Messaging: manage registrations](https://firebase.google.com/docs/cloud-messaging/manage-tokens)
- [Firebase Cloud Messaging: Admin SDK sending](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk)
- [Firebase Admin SDK setup](https://firebase.google.com/docs/admin/setup)
- [Firebase Cloud Messaging error codes](https://firebase.google.com/docs/cloud-messaging/error-codes)
- [Firebase Cloud Messaging scaling and retries](https://firebase.google.com/docs/cloud-messaging/scale-fcm)
- [Firebase Cloud Messaging delivery reporting](https://firebase.google.com/docs/cloud-messaging/understand-delivery)
- [Firebase Admin .NET release notes](https://firebase.google.com/support/release-notes/admin/dotnet)
- [Android notification runtime permission](https://developer.android.com/develop/ui/views/notifications/notification-permission)
- [Android notification channels](https://developer.android.com/develop/ui/views/notifications/channels)
- [Xamarin.Firebase.Messaging NuGet package](https://www.nuget.org/packages/Xamarin.Firebase.Messaging/125.1.1.1)
- [FirebaseAdmin NuGet package](https://www.nuget.org/packages/FirebaseAdmin/3.6.0)
- [Railway variables](https://docs.railway.com/variables)
- [Railway sealed variables](https://docs.railway.com/guides/managing-secrets-on-railway)
