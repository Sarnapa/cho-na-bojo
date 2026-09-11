# Push Notifications Manual Verification

Date: 2026-09-11
Change: `push-notifications`
Deployment: `https://cho-na-bojo-production.up.railway.app`
Status: partial; device/state matrix deferred

## Verified During Implementation

- `GET /health` returned `{"status":"healthy"}` from the deployed API on 2026-09-11.
- Earlier phase verification covered real join, accept, and reject delivery; foreground/background handling; cold/warm tap routing; duplicate collapse; two-device fan-out; logout reassignment; retry isolation; and stale-installation cleanup.
- Notification payloads and presentation use fixed generic text and opaque identifiers rather than names, contact details, or event titles.
- The user confirmed on 2026-09-11 that commit-to-display latency stayed under 30 seconds in observed use. The contracted five recorded runs (commit → outbox claim → FCM acceptance → observed display) were not captured; see the deferred follow-up below.
- The user confirmed that Firebase credentials do not appear in logs, health responses, or built artifacts.
- The user confirmed that the Firebase section in `AGENTS.md` is sufficient for a fresh clone to reach a working push setup.

## Deferred Follow-up

The full device/state matrix was intentionally deferred because there was not enough time to run and record it in this implementation cycle. It is an optional additional verification step rather than a release blocker, and is tracked as unchecked Progress item 7.5 and in `context/foundation/todo.md`.

Worth verifying when device time allows:

1. Test Android 10/API 29 and Android 13+/API 33 in foreground, background, removed-from-recents cold start, and warm `SingleTop` states.
2. Test permission denied and later enabled, channel disabled, offline/reconnect, Doze/App Standby, two devices, account switching, clear data, reinstall, stale-installation cleanup, and queued work across an API restart.
3. Run two API instances or rapid worker passes to confirm no double-send, and confirm a successful device is not re-sent when a sibling delivery retries.
4. Record at least five join, accept, and reject runs capturing commit → outbox claim → FCM acceptance (from the worker's `QueueAgeMilliseconds` log) and observed display time (former Progress item 7.3).

Offline, force-stopped, Doze-restricted, and permission-denied devices are excluded from the under-30-second population, but their behavior still belongs in the compatibility matrix.
