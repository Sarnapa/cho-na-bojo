# Deferred Work

## Push notification device and state matrix

- **Source**: `context/changes/push-notifications/plan.md`, Progress item 7.5 (restored as unchecked)
- **Status**: Deferred — an optional additional verification step, **not a release blocker**. Worth doing when device time allows; a release may ship without it.
- **Reason**: The implementation cycle did not have enough time for the full physical-device and operating-state matrix.
- **Worth verifying (not required to release)**:
  1. Test Android 10/API 29 and Android 13+/API 33 in foreground, background, removed-from-recents cold start, and warm `SingleTop` states.
  2. Test permission denied and later enabled, channel disabled, offline/reconnect, Doze/App Standby, two devices, account switching, clear data, reinstall, stale-installation cleanup, and queued work across an API restart.
  3. Run two API instances or rapid worker passes to confirm no double-send, and confirm a successful device is not re-sent when a sibling delivery retries.
4. Record at least five join, accept, and reject runs capturing commit → outbox claim → FCM acceptance (from the worker's `QueueAgeMilliseconds` log) and observed display time, to give the queue-age tuning rule a baseline (former Progress item 7.3).
- **Evidence location**: Record outcomes in `context/changes/push-notifications/reviews/manual-verification.md`.
