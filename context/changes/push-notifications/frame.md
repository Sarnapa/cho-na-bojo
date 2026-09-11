# Frame Brief: Organizer Notification Appears Missing

> Framing step during Phase 5 manual verification. This document separates
> server delivery from Android presentation.

## Reported Observation

After a join request, the event organizer did not see a notification while
the app was foregrounded. A notification later appeared while the app was
backgrounded and seemed delayed by approximately 5-10 minutes. The matching
`PushOutbox` and `PushDelivery` rows existed.

## Initial Framing (preserved)

- **User's stated cause or approach**: No cause was asserted; the database
  state appeared correct.
- **User's proposed direction**: Verify why manual criterion 5.3 appeared to
  fail.
- **Pre-dispatch narrowing**: The delivery had `AcceptedUtc` and
  `FcmMessageId`, no error or dead-letter state, notification permission was
  granted, and behavior differed between foreground and background.

## Dimension Map

The observation could originate at either remaining dimension:

1. **Server-to-FCM timing** — the outbox could be claimed or accepted late
   despite eventually recording success.
2. **FCM-to-Android presentation** — Firebase could accept promptly while the
   current app state and notification-channel setup determine whether the user
   sees an alert.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Server queue or retry delayed acceptance | `OccurredUtc` was `08:03:10.605673Z`, `ClaimedUtc` was `08:03:11.259087Z`, and `AcceptedUtc` was `08:03:11.594513Z`; `AttemptCount` was 1. The server completed its controllable path in under one second. | NONE |
| Foreground delivery was lost | Firebase routes notification-plus-data messages to `OnMessageReceived` while foregrounded. The current override only logs receipt and does not post a local notification. | STRONG, expected before Phase 6 |
| Background delivery was delayed | A controlled background join produced an FCM log entry and an active Android notification record immediately. The emulator was active, outside Doze, and in the active standby bucket. | NONE in controlled reproduction |
| Background notification looked missing | Android reported that `event_updates` had not been created and used `fcm_fallback_notification_channel` at importance 3 (`DEFAULT`). The notification was in the system tray but lacked the planned high-importance channel behavior. | STRONG |

## Narrowing Signals

- `AcceptedUtc - OccurredUtc` was approximately 0.99 seconds on the first
  attempt, ruling out queue polling, retry backoff, and slow Firebase
  acceptance for the reported row.
- API 36 emulator state was `ACTIVE`; deep and light Doze were both inactive.
- `POST_NOTIFICATIONS` was granted and the package was not stopped.
- Logcat recorded foreground delivery through `ChoNaBojoMessagingService`.
- A controlled background request produced a notification immediately under
  `fcm_fallback_notification_channel`.
- Android explicitly logged: `Notification Channel set in
  AndroidManifest.xml has not been created by the app. Default value will be
  used.`

## Cross-System Convention

Firebase notification-plus-data messages call `OnMessageReceived` in the
foreground and are displayed by the SDK in the system tray in the background.
Android 8+ requires a created notification channel. Firebase falls back to its
generic channel when the manifest's configured channel does not exist.

The approved plan already assigns both missing presentation pieces to Phase 6:
foreground notification presentation and creation of `event_updates` with
`NotificationImportance.High`.

## Reframed Problem Statement

> **The actual problem is**: Phase 5 server delivery succeeds promptly, but
> manual verification was interpreted as requiring Phase 6 Android
> presentation behavior.

Foreground silence is expected because the current service only logs messages.
Background delivery is immediate in the controlled reproduction, but it uses a
default-importance fallback channel and can therefore look silent until the
notification drawer is opened. Phase 6 supplies the foreground presenter and
the high-importance `event_updates` channel needed for heads-up behavior.

## Confidence

- **HIGH** — first-attempt database timestamps, live emulator state, logcat,
  and Android's active notification record all agree.

## What Changes for the Existing Plan

No Phase 5 server redesign is needed. Criterion 5.3 should be evaluated using a
backgrounded app and notification-drawer delivery; heads-up and foreground
presentation remain Phase 6 acceptance criteria.

## References

- Server claim and outcome recording:
  `server/Push/PushOutboxProcessor.cs`
- High-priority notification-plus-data send:
  `server/Push/FirebasePushGateway.cs`
- Foreground logging-only callback:
  `app/ChoNaBojoApp/Platforms/Android/Push/ChoNaBojoMessagingService.cs`
- Manifest channel declaration:
  `app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml`
- Phase boundary:
  `context/changes/push-notifications/plan.md` (Phases 5 and 6)
- Firebase receive behavior:
  https://firebase.google.com/docs/cloud-messaging/android/receive-messages
- Android channels:
  https://developer.android.com/develop/ui/views/notifications/channels
- Investigation tasks: `diagnose-server-timing`,
  `diagnose-android-display`
