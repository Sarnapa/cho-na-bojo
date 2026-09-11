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

## Bound outstanding event join requests

- **Source**: `context/changes/event-lifecycle-ops/reviews/plan-review.md`, finding F5
- **Status**: Deferred — accepted for the MVP lifecycle slice; address before broader rollout or raising expected event traffic.
- **Reason**: `ParticipantLimit` bounds only accepted participants. Pending requests are unlimited, so cancelling an event can update and enqueue notifications for an unbounded pending-plus-accepted roster in one transaction.
- **Required follow-up**:
  1. Define a product-level cap for outstanding `Pending` + `Accepted` requests per event.
  2. Enforce the cap under the existing event row lock in the join path and return a named HTTP 409 conflict.
  3. Define whether rejected or departed requests reopen capacity and add boundary/concurrency coverage.
