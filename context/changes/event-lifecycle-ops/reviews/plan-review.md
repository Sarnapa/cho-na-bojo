<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Event Lifecycle Ops (S-07)

- **Plan**: `context/changes/event-lifecycle-ops/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-11
- **Verdict**: REVISE
- **Findings**: 4 critical, 3 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

Grounding: 14/14 existing paths ✓, 8/8 existing symbols ✓, brief↔plan ✓, Progress contract ✓ (7 phases / 65 checks)

## Findings

### F1 — Cancel can leave revoked contacts visible in memory

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 5 — Client organizer lifecycle actions
- **Detail**: The plan updates a cancelled organizer card in place without a reload, but does not clear or invalidate the selected request queue. Existing accepted rows can retain revealed `ContactRows` because visibility is based only on `EventJoinRequestStatus.Accepted` (`app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs:99-108,153-179`). The server rewrites those rows to `Cancelled`, but the client never receives those row states in `CancelEventResponse`. API revocation therefore does not immediately remove already-rendered contact data, contradicting the plan's privacy promise.
- **Fix**: On successful cancel, update the event card, close/clear the selected detail via the existing `ClearSelectedEvent` path (which purges contact state), and require event `Active` status in every contact visibility/load guard. Add a manual check that previously revealed contacts disappear immediately without a refetch.
  - Strength: Reuses existing cache-clearing behavior and fails closed.
  - Tradeoff: The organizer is returned from detail to the list after cancel.
  - Confidence: HIGH — `ClearSelectedEvent`/`ClearContactState` already clear both organizer and participant contact payloads.
  - Blind spot: Reopening a cancelled detail must not re-enable contact actions.
- **Decision**: FIXED — applied the proposed fix

### F2 — RequestedEventResponse cannot contain the planned Status member

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 — Response contracts; Phase 6 — View data
- **Detail**: `RequestedEventResponse` already has `EventJoinRequestStatus Status` (`shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs:55-68`), and `RequestedEventViewData` uses `response.Status` as that request state (`app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs:75-146`). Appending `EventStatus Status` creates a duplicate positional member and cannot compile.
- **Fix**: Name the new member `EventStatus EventStatus` in both response DTOs and view-data records, then use `EventStatus` only for event lifecycle logic.
- **Decision**: FIXED — applied the proposed fix

### F3 — Leave and removal do not move request cards out of the live section

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 6 — Status badges and Past & cancelled section
- **Detail**: The plan defines `IsPast` only from event status/end time. Leaving or being removed changes only `EventJoinRequest.Status` while the event remains `Active`, so those cards remain in the live collection after refresh. This directly contradicts criterion 6.3 and also leaves `Rejected`/`Removed`/`Left` presentation unspecified. `GetMyEventsAsync` keeps all requester rows (`server/Events/EventEndpoints.cs:400-461`), so refresh does not hide the problem.
- **Fix**: Define live requested events as active/unexpired events whose request status is `Pending` or `Accepted`; route `Rejected`, `Left`, `Removed` and `Cancelled` request rows to a clearly named history/inactive section with explicit badges.
  - Strength: Preserves history and makes every terminal reason legible.
  - Tradeoff: The section should be renamed from "Past & cancelled" to cover inactive participation on otherwise-live events.
  - Confidence: HIGH — the API already returns all terminal request rows.
  - Blind spot: Product wording for the broader history section needs approval.
- **Decision**: FIXED — applied the proposed fix

### F4 — Re-request notification key has no implementable contract

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 4 — Re-request after leaving
- **Detail**: `PushOutbox.EventKey` is globally unique and currently contains only request id, type and recipient (`server/Data/ChoNaBojoContext.cs:446-447`; `server/Push/PushIntentFactory.cs:91-95`). A revived row retains its request id, so its second `JoinRequestCreated` key collides with the first. The proposed "UpdatedUtc-era discriminator" cannot work because the same contract requires `UpdatedUtc = NULL` when the row returns to `Pending`. Existing replay handling returns the old row and does not enqueue a fresh notification.
- **Fix A ⭐ Recommended**: Reset `CreatedUtc` to the new request-attempt time and include its UTC ticks in the revived `JoinRequestCreated` `EventKey`.
  - Strength: No schema change; keeps request timestamp and idempotency aligned.
  - Tradeoff: `CreatedUtc` becomes "current attempt created," not immutable first-ever creation.
  - Confidence: HIGH — duplicate retries see `Pending` after the locked transition and therefore do not enqueue another occurrence.
  - Blind spot: Document the revised `CreatedUtc` semantics in the DTO contract.
- **Fix B**: Add a persisted `AttemptNumber` and include it in the `EventKey`.
  - Strength: Preserves original `CreatedUtc` and gives explicit attempt identity.
  - Tradeoff: Adds a column, migration and state invariant for one replay case.
  - Confidence: HIGH — a monotonic generation makes each legitimate rejoin unique.
  - Blind spot: The counter increment must occur under the event lock.
- **Decision**: FIXED — applied Fix A

### F5 — ParticipantLimit does not bound cancellation fan-out

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Key Discoveries; Performance Considerations; Phase 3
- **Detail**: The 300 cap applies to `ParticipantLimit`, while capacity counts only `Accepted` requests (`server/Events/EventEndpoints.cs:886-902`). Pending requests are unlimited; the unique index merely allows one row per user/event (`server/Data/ChoNaBojoContext.cs:314-319`). Cancellation therefore tracks and writes one request plus one outbox item for an unbounded roster, invalidating the stated 300-row safety bound.
- **Fix A ⭐ Recommended**: Add an explicit cap on outstanding `Pending` + `Accepted` requests in the locked join path and a named 409 conflict.
  - Strength: Makes cancellation predictably bounded and limits abuse.
  - Tradeoff: Introduces a new product rule and rejection case.
  - Confidence: HIGH — the same event lock can enforce the cap consistently.
  - Blind spot: The cap value and reopening behavior after rejection need definition.
- **Fix B**: Preserve unlimited pending requests, remove the 300-row claim, use set-based request updates/batched outbox insertion, and test a declared worst-case roster size.
  - Strength: Preserves current request semantics and notify-everyone behavior.
  - Tradeoff: Cancellation remains linearly expensive and not strictly bounded.
  - Confidence: MEDIUM — safe scale depends on measured PostgreSQL/Railway limits.
  - Blind spot: No production transaction-time budget is documented.
- **Decision**: ACCEPTED — deferred to `context/foundation/todo.md`

### F6 — Missing event status silently deserializes as enum value 0

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Migration Notes; Phase 1 response contracts
- **Detail**: Appending a non-nullable positional enum does not make a newer client reject an older API response. With the current `JsonSerializerOptions` (`app/ChoNaBojoApp/Services/ApiService.cs:17`), a missing property deserializes successfully as `0`. The rollback note can therefore leave the new app with an undefined lifecycle value and unsafe action/grouping decisions.
- **Fix**: Validate every deserialized event status with `Enum.IsDefined` and reject zero/unknown values as an explicit API-result failure; document that API rollback while the new app is deployed is not wire-compatible.
  - Strength: Fails closed instead of presenting cancelled/unknown data as live.
  - Tradeoff: My events becomes unavailable during a bad deploy pairing.
  - Confidence: HIGH — current serialization has no required-member validation.
  - Blind spot: None significant.
- **Decision**: FIXED — applied the proposed fix

### F7 — “Automated” endpoint criteria have no executable harness

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3/4 Automated Verification; Testing Strategy
- **Detail**: The plan explicitly excludes test infrastructure, and `server/server.http` contains only a health request. Yet Phases 3 and 4 classify authenticated, stateful HTTP/DB assertions as automated without a command or script capable of running them. `/10x-implement` cannot verify those checks as written.
- **Fix**: Reclassify the endpoint assertions as manual and add concrete setup, authenticated HTTP requests and SQL observations to `server/server.http` or the phase instructions; keep build/migration commands as automated.
- **Decision**: SKIPPED

### F8 — Drift verification creates an unwanted empty migration

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 Automated Verification
- **Detail**: A second `dotnet ef migrations add` emits another migration artifact; the plan gives no cleanup step and may leave it to be committed.
- **Fix**: Replace it with `dotnet ef migrations has-pending-model-changes --project server`.
- **Decision**: FIXED — applied the proposed fix
