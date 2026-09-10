<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Approval and Contact Reveal (S-05)

- **Plan**: `context/changes/approval-and-contact-reveal/plan.md`
- **Scope**: Phases 1–5 of 5 (full plan)
- **Date**: 2026-09-10
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS (1 observation) |
| Scope Discipline | PASS (1 observation) |
| Safety & Quality | PASS (1 observation) |
| Architecture | PASS |
| Pattern Consistency | WARNING (1 finding) |
| Success Criteria | PASS |

## Success criteria verification

Re-run independently during this review:

| Check | Result |
|---|---|
| `dotnet build solutions/ChoNaBojo.slnx` | PASS — exit 0, 0 errors, 36 `MVVMTK0045` warnings (pre-existing pattern: 27 from `MapViewModel`, 7 from `CreateEventViewModel`, 2 from `MyEventsViewModel`) |
| `dotnet build app/ChoNaBojoApp -f net10.0-android` | PASS — 0 errors |
| `dotnet ef migrations has-pending-model-changes --project server` | PASS — "No changes have been made to the model since the last migration." |
| `shared/ChoNaBojo.Validation` has no new package references | PASS — `ProjectReference` to `ChoNaBojo.Contracts` + `ChoNaBojo.Utils` only |
| All 5 new routes registered (`EventsJoinRequestsAccept`, `EventsJoinRequestsReject`, `MeEventsList`, `EventsJoinRequestsList`, `EventContactsList`) | PASS — `server/Events/EventEndpoints.cs:38–58` |
| No hardcoded colors / off-grid spacing in new XAML | PASS — 0 hex literals or named colors; spacing values used are only `{4,8,16,24}` |
| No raw contact field bound in XAML | PASS — 0 hits for `ContactPhone`/`ContactEmail`/`CommunicatorHandle`/`LoginEmail` in new/modified XAML |
| Contacts not written to persistent storage | PASS — 0 hits for `TokenStore`/`SecureStorage`/`Preferences` in `MyEventsViewModel.cs`, `ContactRevealView.xaml.cs`, `MyEventsPage.xaml.cs` |
| Contact-column reader audit | PASS — `GetEventContactsAsync` is the only reader of `User.ContactPhone/ContactEmail/CommunicatorPlatform/CommunicatorHandle` in the whole `server/` tree |
| Regex false-positive non-goals | PASS — `"5v5 football, court #2, 18:00-19:30"` and `"court #2 at 18:00"` both return `false`; `:` is deliberately excluded from the `[ ().-]` separator class so no digit run reaches the 9-digit minimum |

The HTTP-level checks (2.5, 3.3–3.5) and the OpenAPI-document fetch require a running API plus the Supabase database and were not re-executed here; they were verified by the implementer at phase time. The underlying authorization logic they assert was re-verified statically in this review and holds — see "Privacy guardrail" below.

**Manual-criteria audit**: all 30 manual checkboxes are `[x]`. Each is supported by observable evidence in the diff; none look rubber-stamped. Notably, `change.md`'s documented `VenueEventsPage` modal-close crash and its two-stage fix are direct evidence that criterion 4.4 ("map, venue sheet, and venue-events modal all still work") was genuinely exercised on-device rather than assumed.

## Privacy guardrail (the slice's whole point) — verified sound

- `GetEventContactsAsync` (`server/Events/EventEndpoints.cs:518–590`) derives the caller from `httpContext.GetUserId()` and computes entitlement **in the same database query**. Non-entitled callers (pending requester, rejected requester, stranger) hit the gate at `:560–564` and receive **404** `request_not_found`, indistinguishable from a nonexistent event.
- **Pairwise reveal is enforced in SQL, not in C#**: the `AcceptedParticipants` subquery (`:540`) is predicated on `sportsEvent.OrganizerUserId == callerUserId`, so for a non-organizer the participant contact columns are never even fetched. Accepted participants cannot see each other.
- Accept/reject verify organizer ownership **inside** the lookup predicate (`TransitionJoinRequestAsync:936–949`), not as a later branch, so request/event ids cannot be probed for existence.
- Raw `FOR UPDATE` SQL (`:878`) uses the `ExecuteSqlAsync` interpolated-handler overload → parameterized. No injection.
- The row lock is taken before the accepted-count recount, inside one transaction, and manual accept + auto-accept share the single `ClaimParticipantSlotUnderLockAsync` helper. The last-spot race the plan set out to close is closed.
- Migration `CK_EventJoinRequests_StatusUpdatedUtc` asserts `(Status = 1 AND UpdatedUtc IS NULL) OR (Status <> 1 AND UpdatedUtc IS NOT NULL)` — exactly the intended transition invariant, bidirectionally, with a `Down` that drops it.

All eight "What We're NOT Doing" boundaries were respected — no push notifications, no cancel/remove/leave, no re-request after rejection, no queue auto-rejection, no full-roster sharing, no new status enum values, no test projects, no messaging/ratings.

## Findings

### F1 — Case-colliding tracked solution file perpetuated by this change

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `Solutions/ChoNaBojo.slnx` and `solutions/ChoNaBojo.slnx`
- **Detail**: Git tracks two index entries that differ only in case. On this Windows working tree (`core.ignorecase=true`) only `solutions/ChoNaBojo.slnx` physically exists, so an edit updates both blobs in lockstep and the collision stays invisible. On a case-sensitive checkout — including the Linux GitHub Actions runners this project deploys from per `context/foundation/tech-stack.md` — git materializes two separate files that will silently diverge. The collision **predates this change** (`Solutions/` added in `3af9fa2`, `solutions/` in `e66347b`), but this change's plan-doc commits `4b64455` and `fc55386` modified both entries identically and so carried it forward. Both blobs are still identical at HEAD (`c835ce3`), so this is a latent hazard rather than active damage. `AGENTS.md` and every documented build command reference the lowercase `solutions/` path.
- **Fix A ⭐ Recommended**: Drop the capitalised entry from the index with `git rm --cached "Solutions/ChoNaBojo.slnx"` and commit, keeping the lowercase `solutions/ChoNaBojo.slnx` that `AGENTS.md` and the CI workflows already reference.
  - Strength: Removes the divergence class entirely, and picks the path the documentation and build commands already assume, so nothing else needs updating.
  - Tradeoff: A case-only git operation needs care on `core.ignorecase=true`; the commit must be verified with `git ls-files` afterwards rather than by looking at the working tree.
  - Confidence: HIGH — both blobs are byte-identical at HEAD, so no content is lost either way.
  - Blind spot: Have not checked whether any developer's local Visual Studio/Rider `.suo`/`.idea` state or a GitHub Actions workflow pins the capitalised path.
- **Fix B**: Leave it and open a separate repo-hygiene change.
  - Strength: Keeps this slice's diff strictly about the S-05 feature; the issue was not authored here, and the two blobs are currently identical so nothing is broken today.
  - Tradeoff: The hazard survives until someone edits the solution on a case-sensitive machine, at which point the divergence is confusing to diagnose.
  - Confidence: MEDIUM — depends on whether anyone will touch the solution file from Linux/macOS before the hygiene change lands.
  - Blind spot: No estimate of when a case-sensitive checkout will next edit the solution.
- **Decision**: PENDING

### F2 — `mailto:` URI omits the plan-mandated `Uri.EscapeDataString`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs:1410`
- **Detail**: Plan Phase 5 §2 specifies `mailto:{Uri.EscapeDataString(address)}`. The implementation emits `mailto:{address.Address}` unescaped, but compensates with a stricter guard: `MailAddress.TryCreate` must succeed **and** the parsed `address.Address` must equal the input case-insensitively, so only clean round-tripping addresses ever produce a URI. This is arguably better than the plan — `Uri.EscapeDataString` would percent-encode the `@`, yielding a non-standard `mailto:` that some Android mail clients reject. Recorded so the deviation is a decision rather than silent drift.
- **Fix**: Leave the code as-is and append a one-line note to `change.md` recording that the `mailto:` contract was deliberately tightened (round-trip equality check) instead of escaped, and why.
- **Decision**: PENDING

### F3 — Unplanned, undocumented `MapViewModel` auto-accept reflection

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs` (+17/−9)
- **Detail**: When a join request now returns `Accepted` (the auto-accept path Phase 2 made real), the map's event card increments `ParticipantCount` (clamped) and shows "You've joined this event." This is a coherent and necessary client reflection of Phase 2's behaviour on the pre-existing map surface, but `MapViewModel.cs` appears in no phase's "Changes Required" list and, unlike the `SafeStatefulButtonHandler`/`VenueEventsPage` crash fix, it is not recorded in `change.md`'s Notes. Every other unplanned edit in this change was documented; this one is the exception.
- **Fix**: Add a short entry to `change.md`'s Notes section recording that honouring `AutoAccept` server-side required the map card to render the immediate `Accepted` outcome, so a future reader does not read it as untracked scope creep.
- **Decision**: PENDING

### F4 — Manual accept/reject path does not detach on an unexpected `DbUpdateException`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `server/Events/EventEndpoints.cs:800,822` (catch filters in `TransitionJoinRequestAsync`)
- **Detail**: Both `DbUpdateException` catch filters require `createIfMissing`, i.e. they only cover the auto-accept insert path. On the manual organizer path an unexpected `DbUpdateException` would leave the tracked `joinRequest` in `Modified` state after the `await using` rollback. Unreachable today — the handler always writes constraint-satisfying values (`Status <> 1` always accompanied by a non-null `UpdatedUtc`), and the request-scoped `DbContext` is disposed with no further `SaveChangesAsync`. Noted for defence in depth only; the asymmetry with the well-handled create path is what makes it worth recording.
- **Fix**: Optionally detach the modified entry in a general `catch`/`finally` around the manual transition so both paths leave the change tracker clean, matching the create path's discipline.
- **Decision**: PENDING
