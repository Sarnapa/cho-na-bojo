<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Push Notifications (S-06)

- **Plan**: `context/changes/push-notifications/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-10
- **Verdict**: REVISE → **SOUND** after triage (all 7 findings fixed 2026-09-10)
- **Findings**: 2 critical, 4 warnings, 1 observation — all FIXED

> Both FAIL dimensions are localized mechanism fixes, not approach errors. The architecture (outbox-after-commit, structural privacy boundary, Phase 1 binding spike) is sound.

## Verdicts

| Dimension | Verdict | After triage |
|-----------|---------|--------------|
| End-State Alignment | FAIL | PASS — logout promise narrowed to online explicit sign-out and backed by a working mechanism (F2) |
| Lean Execution | PASS | PASS — Fix A for F2 additionally removed an abstraction, a named client and an endpoint |
| Architectural Fitness | WARNING | PASS — due-time filter removes head-of-line blocking (F5) |
| Blind Spots | FAIL | PASS — outbox ordering (F1), permission sequencing (F3), background dedup (F4), hijack risk recorded (F6) |
| Plan Completeness | WARNING | PASS — citations refreshed, brief dependency corrected (F7) |

## Grounding

16/16 paths ✓, 8/8 symbols ✓ (4 with line-number drift), Progress block well-formed (7/7 phases matched, 62/62 success criteria mapped, no stray checkboxes outside `## Progress`), brief↔plan ⚠️ (one dependency mismatch). `docs/reference/contract-surfaces.md` does not exist — contract-surface check skipped.

## Findings

### F1 — Outbox row references a join-request id that doesn't exist yet

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Critical Implementation Details ("Outbox insert ordering") + Phase 4, change 6
- **Detail**: The plan mandates adding the `PushOutbox` row to the change tracker *before* `await dbContext.SaveChangesAsync(...)`, and builds `EventKey` as `join-request:{joinRequestId}:{type}:{recipientUserId}` plus an `EventJoinRequestId` FK. But `EventJoinRequest.Id` is database-generated — `HasDefaultValueSql("gen_random_uuid()")` at `server/Data/ChoNaBojoContext.cs:316` (same convention at `:163` and `:248`). On the `createIfMissing` path the entity is `Add`ed at `server/Events/EventEndpoints.cs:700-705` and its `Id` stays `Guid.Empty` until `SaveChanges` reads back the `RETURNING` value. So for every `JoinRequestCreated` intent — the most common notification — the plan writes `EventJoinRequestId = Guid.Empty` (immediate FK violation) and `EventKey = join-request:00000000-0000-0000-0000-000000000000:1:{organizerId}`, which collides on the unique index the second time the same organizer receives a join request. The plan states that a `DbUpdateException` on that index "propagates as today" — meaning a 500 and a **rolled-back join request**, directly violating the plan's own rule that "notification failure must never affect the domain response." Secondary: the two `DbUpdateException` catch blocks at `server/Events/EventEndpoints.cs:800-825` and `:826-845` detach `joinRequest` but would leave the `Added` `PushOutboxItem` in the change tracker. Also note the plan calls `:800-825` a path that "commits without a state change" — it actually *rolls back* at `:802`.
- **Fix A ⭐ Recommended**: Insert the outbox row *after* `SaveChangesAsync` and *before* `CommitAsync`
  - Strength: `joinRequest.Id` is populated by then, so the FK and `EventKey` are correct; still one transaction, one commit, fully atomic. Also removes the change-tracker hazard in both catch blocks for free.
  - Tradeoff: One extra round-trip inside the `FOR UPDATE` row lock — exactly the cost the plan considered and rejected.
  - Confidence: HIGH — the plan already documents this ordering as atomic; only its cost objection stands, and that cost is sub-millisecond.
  - Blind spot: Npgsql will not batch the two `SaveChanges` calls; it is a genuine second round-trip, just a cheap one.
- **Fix B**: Client-generate the join-request id before `Add` and wire the outbox via the navigation property
  - Strength: Single round-trip preserved.
  - Tradeoff: Introduces a second, divergent id-generation convention against all three `gen_random_uuid()` PKs in `ChoNaBojoContext.cs`; still needs an explicit detach of the outbox entity on the recovery paths.
  - Confidence: MEDIUM — EF honours explicit values for `ValueGeneratedOnAdd`, but this is subtle behaviour to rely on.
  - Blind spot: Not verified against the existing `ClientRequestId` idempotency index on `SportsEvents`.
- **Decision**: FIXED via Fix A — outbox row now inserted after the domain `SaveChangesAsync` and before `CommitAsync` (Critical Implementation Details "Outbox insert ordering" + Phase 4 change 6 rewritten; change-tracker hazard on both recovery paths noted as resolved)

### F2 — The logout unlink cannot run on two of the three sign-out paths

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: End-State Alignment
- **Location**: Desired End State + Success Criteria (Summary) + Phase 3, changes 6-7
- **Detail**: Desired End State and Success Criteria both assert: *"Logging out stops that device receiving the previous account's notifications."* Phase 3 backs this with `IPushInstallationUnlinker`, which requires a **valid access token** captured during `SignOutAsync`. Verification found three sign-out call sites, and two cannot supply one: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:675` uses `SignOutAsync(revokeServer: true)` ✅; `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:167` and `:171` both use `revokeServer: false` ❌. Both `false` sites are the refresh-failure path documented at `app/ChoNaBojoApp/Services/Auth/ISessionService.cs:28-31` — by definition there is no usable credential. Every routine session expiry therefore leaves the installation row active and the device receiving the previous account's pushes indefinitely. The offline explicit-logout case (plan criterion 3.7) has the same hole, and the plan asserts sign-out "completes promptly" without ever asserting the row is later disabled. This is a systematic gap, not an edge case, and the plan offers no recovery path.
- **Fix A ⭐ Recommended**: Carry the unlink on the existing logout call, and narrow the promise
  - Strength: `SessionService` already injects `IAuthTokenClient`, already runs on the un-handled `"ChoNaBojoAuth"` client (`app/ChoNaBojoApp/MauiProgram.cs:53-59`), and already sends the **refresh token** — which authenticates logout with no access token needed. Adding the registration id to that call and disabling the installation during refresh-token revocation deletes `IPushInstallationUnlinker`, the `"ChoNaBojoPush"` client, and the `DELETE` endpoint from Phases 2-3 entirely. Separately, restate the end-state to exclude the `revokeServer: false` path and note that a stale row is re-claimed by the next `PUT` on that device.
  - Tradeoff: Changes the contract of an already-shipped `/auth/logout` endpoint and couples auth to push.
  - Confidence: MEDIUM-HIGH — the mechanism is verified; the contract change is not.
  - Blind spot: Haven't confirmed the logout request DTO can take an additive optional field without breaking in-flight clients.
- **Fix B**: Keep Phase 3's design, make the promise honest, add a deferred-recovery marker
  - Strength: No auth-contract change; much smaller diff from the current plan.
  - Tradeoff: Retains the extra named client, endpoint and abstraction, and the gap stays real until someone next authenticates on that device.
  - Confidence: HIGH — purely additive to what's already planned.
  - Blind spot: A device that is logged out and never reused is never cleaned up.
- **Decision**: FIXED via Fix A — unlink now rides the existing `POST /auth/logout` refresh-token call (`LogoutRequest.DeviceRegistrationId`); `IPushInstallationUnlinker`, the `"ChoNaBojoPush"` client and the `DELETE` endpoint are removed from Phases 2-3; end-state promise narrowed to online explicit sign-out with the `revokeServer: false` gap documented as an accepted, re-claimable scope limit

### F3 — Permission prompt targets the wrong file and can collide with an in-flight location request

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 6, change 2
- **Detail**: The plan lists `app/ChoNaBojoApp/Views/MapPage.xaml.cs` and says the prompt must be "sequenced so it does not collide with the location permission prompt already on this page." It isn't on that page. `MapPage.xaml.cs:224-229` only *checks* status; the actual `Permissions.RequestAsync<Permissions.LocationWhenInUse>` lives in `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:1019`, reached asynchronously via `AppearingCommand` fired from `OnAppearing` (`MapPage.xaml.cs:54`). A `POST_NOTIFICATIONS` request issued from `OnAppearing` therefore races an in-flight location request in the ViewModel, and MAUI's Android implementation does not queue concurrent permission requests.
- **Fix**: Retarget the change to `MapViewModel`, chaining the notification request after the location request resolves inside the existing `AppearingCommand` flow rather than firing it independently from the page.
- **Decision**: FIXED — Phase 6 change 2 retargeted to `MapViewModel` with the sequencing rationale and corrected call sites; criterion 6.4 now asserts the ordering

### F4 — `notificationId` dedup doesn't apply to background notifications

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: What We're NOT Doing + Phase 5 change 4 + Phase 6 change 4
- **Detail**: "No exactly-once delivery guarantee. Duplicates are deduplicated client-side by `notificationId`" — but that tag is only applied by `PushNotificationPresenter`, which runs on the **foreground** path. When the app is backgrounded the FCM SDK displays the notification itself and `OnMessageReceived` is never called. Since each outbox retry produces a distinct FCM message id, an application-level retry after a send that actually succeeded stacks two system notifications. Phase 6's criterion 6.10 would pass in the foreground and fail in the background — the state users are actually in when notifications matter.
- **Fix**: Set `AndroidNotification.Tag = notificationId` in `AndroidConfig` inside `FirebasePushGateway`, so SDK-displayed notifications replace rather than stack, and re-scope criterion 6.10 to test the backgrounded case.
- **Decision**: FIXED — server-set tag added to Phase 5 change 4, "What We're NOT Doing" restated, Phase 6 change 4 scoped to foreground, criterion 6.10 now covers both states

### F5 — Outbox claim query has no due-time filter; backed-off items starve the batch

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 4 change 3 + Phase 5 change 7
- **Detail**: `NextAttemptUtc` lives only on `PushDelivery`; `PushOutboxItem` has no due-time column, and the claim is specified as "a bounded batch (20) of **incomplete** outbox items … ordered by `OccurredUtc`" over "a filtered index … over incomplete items." An item whose deliveries are all backing off stays incomplete and is re-claimed every 2 seconds — and because it is the *oldest*, it sits at the front of the ordering. With a 30-minute TTL and 5 attempts, a handful of failing recipients can hold all 20 slots ahead of fresh work, which attacks the 30-second SLO precisely when FCM is already degraded. The Performance Considerations section reasons about poll interval and batch size but never about head-of-line blocking.
- **Fix**: Add `NextAttemptUtc` (min across pending deliveries) to `PushOutboxItem`, maintained on each pass, and filter the claim on `CompletedUtc IS NULL AND NextAttemptUtc <= now()` with the index rebuilt to match.
- **Decision**: FIXED — column added in Phase 4 change 2, index keyed `(NextAttemptUtc, OccurredUtc)` in change 3, claim predicate and per-pass recompute specified in Phase 5 change 7, head-of-line blocking addressed in Performance Considerations, new criterion 5.9

### F6 — `PUT` reassignment is an unrecorded installation-hijack primitive

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2, change 7
- **Detail**: The upsert "matches on `DeviceRegistrationId` and on match **reassigns** `UserId`" with no proof of device ownership. Any authenticated user who obtains another user's registration id redirects that user's notifications to themselves and denies the victim theirs. The plan explicitly reasons about this class of problem for `DELETE` ("unlinking cannot be used to probe other users' rows") but not for the strictly more powerful `PUT` — and criterion 2.6 tests the reassignment as desired behaviour. Reassignment is genuinely required for account switching, so this is an accepted risk rather than a bug — but on a product whose hard rule is a contact-privacy boundary, an unrecorded accepted risk is the problem.
- **Fix**: Record it explicitly in "Open Risks" — registration ids are secrets, never logged or echoed by client or server (already true server-side per Phase 5 change 9; extend the rule to the client store) — and note the residual hijack risk with its account-switch justification.
- **Decision**: FIXED — new "Open Risks" section added to the plan recording the accepted reassignment risk, the two mandatory non-disclosure mitigations (server and client store), the bounded blast radius, and the post-MVP proof-of-possession route

### F7 — Line-number citations drift, and the brief understates Phase 3's dependencies

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Current State Analysis, Critical Implementation Details, References, `plan-brief.md`
- **Detail**: Every cited *value* checked out, but several line ranges point a few lines off, which will cost the implementer time in a 1,096-line file:

  | Plan cites | Actual |
  |---|---|
  | `EventEndpoints.cs:787` (SaveChanges) | `:792-793` |
  | `MauiProgram.cs:68-73` (`"ChoNaBojoAuth"`) | `:53-59` |
  | `MauiProgram.cs:78-89` (singletons) | `:75-89` |
  | `LoadingPage.xaml.cs:38-40` (`SetAppRoot()`) | `:43` |
  | `ChoNaBojoApp.csproj:38` (`SupportedOSPlatformVersion`) | `:36` |
  | `ChoNaBojoApp.csproj:31` (`ApplicationId`) | `:29-30` |
  | `ChoNaBojoApp.csproj:12-13` (`TargetFrameworks`) | `:10-11` |

  Separately, `plan-brief.md` states "3 needs 2", but Phase 3 change 2 edits `Platforms/Android/Push/ChoNaBojoMessagingService.cs`, which Phase 1 creates — Phase 3 needs **1 and 2**.
- **Fix**: Refresh the cited ranges and correct the brief's dependency line to "3 needs 1 and 2".
- **Decision**: FIXED — citations refreshed against the current files (`EventEndpoints.cs:792-793`, `:799-821` with the rollback noted, `LoadingPage.xaml.cs:43`, `MapPage.xaml.cs:61`/`:226`, `MauiProgram.cs:68-72`/`:75-87`, `csproj:11-12`/`:30`; `csproj:38` was already correct) and the brief now reads "3 needs 1 and 2"

## What the plan gets right

So the fixes don't obscure it: the outbox-after-commit decomposition; the structural privacy guarantee (a payload factory that takes no `User`/`SportsEvent` argument at all, locked by test); the `SessionService` no-`IApiService` invariant being honoured rather than broken; the Phase 1 binding spike de-risking the FID unknown before anything depends on it; and a `## Progress` block that parses cleanly for `/10x-implement`.
