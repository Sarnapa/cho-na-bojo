<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Push Notifications (S-06)

- **Plan**: `context/changes/push-notifications/plan.md`
- **Scope**: Phases 1–7 of 7 (full plan)
- **Date**: 2026-09-11
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | FAIL |

### Automated verification re-run

- `dotnet build solutions/ChoNaBojo.slnx` → **PASS** (0 errors; 117 warnings, all pre-existing `MVVMTK0045` WinUI-AOT warnings in `MapViewModel`/`MyEventsViewModel`, unrelated to this change).
- Merged Android manifest (`obj/Debug/net10.0-android/android/manifest/AndroidManifest.xml`) → **PASS**: `minSdkVersion="29"`, `POST_NOTIFICATIONS`, `ChoNaBojoMessagingService` with `com.google.firebase.MESSAGING_EVENT`, and all four Firebase `meta-data` entries including `firebase_messaging_installation_id_enabled=true`.
- Shared projects gained no framework dependency → **PASS** (`Contracts`/`Utils` are leaves; `Validation` → Contracts + Utils only).
- `dotnet ef database update` and deployed `/health` → **not re-run** (require the Supabase dev database and the Railway deployment; accepted as verified during implementation).

## Findings

### F1 — FCM sends run inside the outbox transaction; a non-Firebase throw makes the item a poison pill

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `server/Push/PushOutboxProcessor.cs:37-88`, `:145-158`
- **Detail**: `ProcessItemAsync` performs every `pushGateway.SendAsync` round-trip *inside* the `BeginTransactionAsync` scope that holds `FOR UPDATE` on the outbox row (`:37-40`), and all delivery outcomes are persisted by the single `SaveChangesAsync`/`CommitAsync` at `:64-65`. Consequences:
  1. **Lost outcomes on partial failure.** If delivery #3 throws something the gateway does not catch (it only catches `FirebaseMessagingException` and `ArgumentException` — `server/Push/FirebasePushGateway.cs:73-84`), or shutdown cancels mid-loop (`:69-74`), the rollback discards the `AcceptedUtc` already written for siblings #1/#2 that FCM actually accepted. They are re-sent on the next claim. Blast radius is small because the server-set `AndroidNotification.Tag = notificationId` collapses duplicates into one system notification, exactly as the plan intends.
  2. **Poison-pill hot loop (the sharper risk).** On rollback, `AttemptCount` is never incremented and `NextAttemptUtc` is never advanced, so the item still satisfies `CompletedUtc IS NULL AND NextAttemptUtc <= now()`. A consistently-throwing item is re-claimed **every 2-second poll forever**, never backing off and never dead-lettering — the exact failure mode `MaxAttempts` exists to prevent. It logs an error each pass (`:76-84`) so it is observable, and because each item has its own transaction it does not starve siblings.
  3. A DB connection and row lock are held open across every FCM network round-trip.
- **Fix A ⭐ Recommended**: Persist each delivery outcome in its own short transaction — call `SendAsync` outside the claim transaction and commit the single `PushDelivery` row per attempt, so an accepted delivery is durable the moment FCM accepts it.
  - Strength: Removes both the duplicate window and the poison-pill loop at the source, and takes network I/O out of the span holding the outbox row lock. The claim already re-checks `CompletedUtc`/`NextAttemptUtc` under the lock (`:45-55`), so shortening the transaction does not weaken the double-send guard.
  - Tradeoff: Restructures the processor's transaction boundaries — the most intricate file in the change, verified only manually per the plan's chosen test scope.
  - Confidence: MEDIUM — the claim/completion logic is otherwise correct, but the rework touches concurrency behaviour that has no automated coverage.
  - Blind spot: Have not measured whether committing per delivery changes observed p95 queue age under the 2-second poll.
- **Fix B**: Keep the transaction shape, but in the `catch (Exception)` at `:76` advance `NextAttemptUtc` (and increment the item's attempt count) in a separate short transaction before continuing.
  - Strength: Small, surgical; closes the infinite-hot-loop hole without touching the send loop.
  - Tradeoff: Leaves the duplicate-on-partial-failure window and the lock-across-network-I/O behaviour in place; treats the symptom, not the cause.
  - Confidence: HIGH — a bounded, easily-reasoned change.
  - Blind spot: A separate transaction inside a catch block needs its own failure handling or the loop can still stall.
- **Decision**: PENDING

### F2 — Phase 7 device/state matrix was never recorded

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `context/changes/push-notifications/reviews/manual-verification.md:21-31`
- **Detail**: Phase 7 change 5 requires recorded outcomes for Android 10/API 29 and 13+/API 33; foreground / background / removed-from-recents / warm `SingleTop`; permission denied and later enabled; channel disabled; offline-reconnect; Doze; two devices; account switch; clear data; reinstall; stale-installation cleanup; plus the two-instance no-double-send check. The file records **no outcomes** — only a forward-looking to-do list under "Deferred Release Follow-up", with the deferral mirrored in `context/foundation/todo.md`. The Phase 7 success criterion "The full device/state matrix is recorded with outcomes" is therefore unmet. The deferral is honestly documented and tracked, which is the mitigating factor; the concurrency checks in item 3 (two instances must not double-send) are the ones that most directly interact with F1.
- **Fix**: Keep the deferral, but make it visible where reviewers look — restore a `- [ ] 7.5 Full device/state matrix recorded with outcomes` line in the plan's `## Progress` (see F3) and treat the `todo.md` entry as a release gate rather than general backlog.
- **Decision**: PENDING

### F3 — Progress silently drops item 7.5, so Phase 7 reads 100% complete

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/push-notifications/plan.md` `## Progress`, Phase 7 manual items
- **Detail**: Phase 7's manual checklist lists `7.3`, `7.4`, `7.6` — there is no `7.5`. The numbering gap shows the item was deleted rather than left unchecked. `todo.md` even names it "former Progress item 7.5", confirming the removal was deliberate. The plan's own Progress convention is `- [ ]` pending / `- [x]` done with step titles unchanged; deleting a step makes `count([x]) / count([ ] + [x])` report 100% and lets `/10x-archive` and `/10x-status` treat the change as fully verified when a stated success criterion is not.
- **Fix**: Re-insert `- [ ] 7.5 Full device/state matrix recorded with outcomes` (unchecked) in the Phase 7 Manual block between 7.4 and 7.6, so completion math reflects reality.
- **Decision**: PENDING

### F4 — Latency criterion 7.3 is marked done without the recorded measurements it requires

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/push-notifications/reviews/manual-verification.md:12`
- **Detail**: Phase 7 change 4 contracts for "at least five join, accept, and reject runs … capturing commit → outbox claim → FCM acceptance (from worker logs) and observed display time", explicitly to "turn 'within 30 seconds' into recorded measurements against a defined population". The file contains no runs, no table and no numbers — only "The user confirmed on 2026-09-11 that recorded commit-to-display latency stayed under 30 seconds." Progress item `7.3` is checked `[x] — 5f83118` on that basis. The plan's own Performance section names queue age as the metric that matters and sets a tuning rule against it; without recorded values that rule has no baseline. This is the rubber-stamping pattern the review is meant to catch, not a correctness defect — the pipeline demonstrably works.
- **Fix**: Either record the five runs with their commit → claim → acceptance → display timings (the worker already logs `QueueAgeMilliseconds` at `server/Push/PushOutboxProcessor.cs:98-103`), or downgrade `7.3` to unchecked and fold it into the F2 release gate.
- **Decision**: PENDING

### F5 — Android fire-and-forget tasks are not exception-observed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/Platforms/Android/Push/ChoNaBojoMessagingService.cs:31`, `:66`, `:75-79`
- **Detail**: Three discarded tasks (`_ = registrationService.OnRegistrationIdChangedAsync(...)`, `MainThread.BeginInvokeOnMainThread(() => _ = router.RefreshVisibleMyEventsAsync())`, and the same pattern in `OnDeletedMessages`) have no `try/catch`. Most throw paths are already covered — `ApiService` maps network/JSON exceptions to typed results and `PushRegistrationService` swallows failures by design — but `Preferences` access and `MyEventsPage.RefreshFromPushAsync()` can still throw into an unobserved `Task` on a worker thread, where it is invisible rather than logged. The plan's rule that "push registration must never surface an error to the user" is satisfied; silent-and-unlogged is the gap.
- **Fix**: Wrap each fire-and-forget body in `try/catch` that logs via `Log.Warn(LogTag, ...)`, matching the logging already used in this file.
- **Decision**: PENDING

### F6 — `PushIntentFactory` always returns an intent; the "zero or one" contract lives at the call sites

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `server/Push/PushIntentFactory.cs:15-30`, `server/Events/EventEndpoints.cs:788-812`
- **Detail**: Phase 4 change 5 specifies a factory that "returns zero or one intent descriptor", with the caller adding a row "when it yields an intent"; the plan's rule table includes a "No state change (replay) → none" row. The implementation returns a non-nullable `PushIntentDescriptor` (or throws) and `EventEndpoints` adds the outbox row unconditionally. Net behaviour is correct — replays never reach `Create` because the three early-return paths (`:684-691`, `:725-732`, `:799-821`) return first — but the "none" case is enforced by call-site structure rather than by the type, so a future S-07 caller that forgets a guard gets a spurious notification instead of a `null`.
- **Fix**: No action required for S-06. When S-07 extends the factory, make the return type nullable so the "no notification owed" case is expressed in the signature.
- **Decision**: PENDING

### F7 — Seven files changed outside the plan's file list, all justified consequences

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `server/Auth/RefreshTokenService.cs:174-178`, `app/ChoNaBojoApp/Views/MyEventsPage.xaml.cs`, `app/ChoNaBojoApp/Views/LoginPage.xaml.cs`, `app/ChoNaBojoApp/ChoNaBojoApp.csproj`, `app/ChoNaBojoApp/Services/Push/PushRegistrationResults.cs`, `server/Push/PushMessage.cs`, `context/foundation/todo.md`, `solutions/ChoNaBojo.slnx`
- **Detail**: None of these are named in the plan's "Changes Required", and each is a necessary consequence rather than scope creep: `RevokeFamilyAsync` changed `Task` → `Task<Guid?>` because change 7b's ownership check needs the revoked user id; `MyEventsPage.RefreshFromPushAsync()` backs Phase 6's "forces a server refresh"; `PushRegistrationResults`/`PushMessage` are supporting types for contracted APIs; `Xamarin.AndroidX.Fragment.Ktx` was pinned to `1.9.0` to resolve a duplicate `FragmentKt` D8 failure the Firebase package exposed (documented in `binding-spike.md`); `todo.md` and the `.slnx` file list are housekeeping. One genuine call-site drift: Phase 6 change 7 puts post-login `ConsumePendingAsync()` in `LoginViewModel.cs`, but it landed in `LoginPage.xaml.cs` after `SetAppRoot()` — behaviourally equivalent and arguably better placed, since that is where the authenticated Shell root actually becomes available.
- **Fix**: No code change. Note the `Fragment.Ktx` pin and the `RevokeFamilyAsync` signature change in the plan as an addendum so the next reviewer does not re-investigate them.
- **Decision**: PENDING

## Confirmed clean

Checked and found correct — recorded so the next review does not repeat the work:

- **Outbox insert ordering** (`server/Events/EventEndpoints.cs:788-812`): after the first `SaveChangesAsync`, second `SaveChangesAsync` before `CommitAsync`, one transaction, no nesting — exactly as the plan's Critical Implementation Details require.
- **Replay suppression**: all three early-return paths commit/roll back without touching `PushOutbox`.
- **`SessionService` invariant**: depends on `ITokenStore`, `IAuthTokenClient`, `IPushRegistrationStore`, `IPushNavigationRouter` — no `IApiService` directly or transitively.
- **Logout unlink is user-scoped**: `AuthEndpoints.cs:194-197` disables only rows matching both the revoked `UserId` and the supplied registration id; returns `204` regardless.
- **Both mandated privacy mitigations hold**: the raw registration id is never echoed, logged (only a SHA-256 truncation at `PushOutboxProcessor.cs:260`, and only `.Length` client-side) or surfaced; `PushPayloadFactory` structurally cannot carry a name, contact detail or event title — three fixed bodies plus opaque ids.
- **`ServiceAccountJson`** is redacted in `ToString()`, never logged, never written to disk, never in `/health`. `google-services.json` contains no private key.
- **All SQL parameterized**: named `NpgsqlParameter`s in the upsert, `FromSqlInterpolated` in the claim.
- **Claim/completion concurrency**: `FOR UPDATE SKIP LOCKED` with an in-lock re-check; accepted deliveries excluded from resend; `NextAttemptUtc` recomputed as the pending minimum, preventing re-claim-every-poll and head-of-line blocking; zero-installation items complete immediately.
- **Worker hygiene**: new DI scope per iteration, no captured scoped `DbContext`, per-iteration try/catch, clean cancellation on shutdown.
- **Migrations additive only**; `PushDelivery` → `PushOutbox` cascade, → `PushInstallation` restrict; `CK_PushOutbox_Type IN (1,2,3)` plus `Enum.IsDefined` guards — dual-layer enum rule satisfied.
- **Android**: no MAUI UI or Shell access from `OnMessageReceived`, null-guarded `IPlatformApplication.Current?.Services`, `PendingIntentFlags.Immutable`, channel created in `MainApplication.OnCreate` before any post, `OnNewIntent` sets the intent.
- **FID mode is consistent end-to-end**: `binding-spike.md` records FID, the gateway uses `Message.Fid`, and the manifest sets `firebase_messaging_installation_id_enabled=true` — no mixed-mode `IllegalStateException` risk.
- **Every "What We're NOT Doing" boundary respected**: 3 enum values only, no iOS, no event-detail deep link, one Firebase project, no test infrastructure, no preferences/topics.
- **Pattern compliance**: route/validation/`Results.*` style matches `EventEndpoints`/`AuthEndpoints`; one `ValidationProblemResponse` shape for 400; EF configs follow `gen_random_uuid()`/`timestamp with time zone`/`CK_*`/`IX_*` conventions; `PushValidation` returns `ValidationResult`, never `IResult`; `ApiService` typed-result pattern matched; `#if ANDROID` + no-op DI pattern matched.
