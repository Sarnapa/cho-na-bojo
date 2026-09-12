<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Event Lifecycle Ops (S-07)

- **Plan**: `context/changes/event-lifecycle-ops/plan.md`
- **Scope**: Phases 1–7 of 7 (full plan)
- **Date**: 2026-09-12
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 3 observations
- **Triage**: completed 2026-09-12 — **every finding below was triaged and decided by @Sarnapa** (6 fixed, 1 skipped). See [Triage](#triage-2026-09-12).

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Automated verification (re-run during this review)

| Check | Command | Result |
|---|---|---|
| Solution builds | `dotnet build solutions/ChoNaBojo.slnx` | PASS — 0 errors, 119 warnings (all `MVVMTK0045`, pre-existing WinRT-AOT pattern) |
| No EF model drift | `dotnet ef migrations has-pending-model-changes --project server` | PASS — "No changes have been made to the model since the last migration" |
| Android app builds | `dotnet build app/ChoNaBojoApp -f net10.0-android` | PASS — 0 errors, 9 warnings |
| API starts with both workers | `dotnet run --project server` | PASS — `PushDeliveryWorker` + `EventAutoCloseWorker` both active, auto-close `UPDATE` parameterized and set-based |
| Health endpoint | `GET /health` | PASS — `200 {"status":"healthy"}` while both workers run |

## Findings

> Each finding's `Decision:` was chosen by @Sarnapa during the 2026-09-12 triage session.

### F1 — Auto-close silently revokes contact access for finished events

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `server/Events/EventEndpoints.cs:655` (contract originates in `plan.md`, Phase 1 item 4)
- **Detail**: `GetEventContactsAsync` now requires `sportsEvent.Status == EventStatus.Active`. The plan justified this solely by the Q8 *cancellation* revocation decision — but `Active` also excludes `Closed`, and Phase 7's `EventAutoCloseWorker` flips every event to `Closed` within 60s of `EstimatedEndsAtUtc`. Before this slice the endpoint had no status or time filter at all, so accepted participants kept contact access indefinitely. Now an accepted pair loses the ability to reach each other the moment the *estimated* end time passes — with no notification and no client-side explanation. Because `EstimatedEndsAtUtc` is an organizer's estimate, a game that starts late or runs long loses coordination exactly when "I'm running late / where are you?" matters most. This is a cross-phase interaction: Phase 1 made the gate, Phase 7 made `Closed` reachable, and no phase reasoned about the combination. The direction fails *closed*, so this is a utility regression against FR-011, not a privacy leak.
- **Fix A ⭐ Recommended**: Gate contacts on `Status != EventStatus.Cancelled` instead of `== Active`, preserving the Q8 cancellation revocation while letting accepted participants keep coordinating through and after a finished event.
  - Strength: One-clause change at a single call site; restores the pre-slice behaviour for the only case the plan never actually argued for revoking, and keeps the cancellation revocation the plan *did* argue for fully intact.
  - Tradeoff: Contact access becomes effectively permanent for events that simply ended, so there is no natural expiry of a revealed contact pair.
  - Confidence: HIGH — the pre-slice revision served contacts with no status filter whatsoever, so this is strictly narrower than the behaviour that already shipped.
  - Blind spot: Not verified how `MyEventsViewModel` renders a contact fetch that 404s for a history event; Fix A makes that path rarer but does not prove it is handled.
- **Fix B**: Keep `== Active` but add an explicit grace window (e.g. `Status == Active || (Status == Closed && StatusChangedUtc > now - 24h)`).
  - Strength: Bounds contact exposure in time, which is closer to the product's privacy-minimising instinct than open-ended access.
  - Tradeoff: Introduces a new tunable constant and a second time comparison on the hottest privacy-critical read path; more logic to get wrong than Fix A.
  - Confidence: MEDIUM — behaviourally sound, but the grace period is an unvalidated product guess with no PRD or plan backing.
  - Blind spot: No user research on how long after a game people still need to coordinate.
- **Decision**: FIXED via Fix A — `GetEventContactsAsync` now gates on `Status != EventStatus.Cancelled` (`server/Events/EventEndpoints.cs:654-657`), and the client mirrors were widened in step with it: `RequestedEventViewData.IsContactAllowed` and `EventJoinRequestViewData.IsContactAllowed` (`app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs`) now drive `HasRevealedContact` / `IsContactLocked` / `CanLoadContact` / `ShowContactLoadAction` and `ApplyOrganizerContacts`. `CanAccept` / `CanReject` / `CanRemove` / `CanLeave` still require `Active`. Verified on device 2026-09-12.

### F2 — 60 manual success criteria marked complete with no recorded evidence

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `context/changes/event-lifecycle-ops/plan.md:589-714` (`## Progress`); expected artifact `context/changes/event-lifecycle-ops/reviews/manual-verification.md` absent
- **Detail**: Every one of the 60 Progress checkboxes across all 7 phases is `- [x]` with a phase commit SHA, but the repository holds no evidence artifact for the manual half. The plan's Testing Strategy declares the privacy regression check *"mandatory before sign-off"*, and several items are genuinely hard to execute and therefore the most likely to be rubber-stamped: 4.13 / 7.6 (concurrent remove-vs-accept never exceeding `ParticipantLimit`, and two API instances against one database), 7.8 (backlog close after a restart gap), and every "within 30 seconds" push-delivery timing claim. The two most recent comparable slices both left a `reviews/manual-verification.md` (`context/archive/2026-08-24-map-venue-discovery/`, `context/archive/2026-09-10-push-notifications/`), and the push slice's `todo.md` entry shows that file is also where genuinely-deferred checks get recorded. Since the Q14 decision means there is no automated test suite to fall back on, an unrecorded manual matrix is the *only* evidence this slice has — and right now it is not written down anywhere.
- **Fix A ⭐ Recommended**: Create `reviews/manual-verification.md` recording what was actually executed, and demote any item that was not genuinely run back to `- [ ]` with a `todo.md` entry, following the `push-notifications` precedent.
  - Strength: Matches the established convention exactly, and makes the concurrency and 30-second-timing claims auditable instead of assertions; a future `/10x-archive` then closes on real evidence.
  - Tradeoff: Requires honestly revisiting 60 checkboxes, and may re-open phases that currently look closed.
  - Confidence: HIGH — the precedent file, its structure, and the deferral mechanism all already exist in this repository.
  - Blind spot: Only the implementer knows which items were truly executed; this review cannot distinguish a real pass from a rubber stamp.
- **Fix B**: Record evidence only for the high-risk subset (4.13, 7.6, 7.8, the privacy regression check) and explicitly accept the rest as observed-in-passing.
  - Strength: Much cheaper, and concentrates effort on the claims that are both hardest to run and most costly if wrong.
  - Tradeoff: Leaves the majority of the matrix unevidenced, so the next reviewer faces this same gap.
  - Confidence: MEDIUM — defensible for an MVP slice, but weaker than the convention the last two slices set.
  - Blind spot: The privacy regression check spans all three actions; verifying it partially may miss the one path that leaks.
- **Decision**: SKIPPED — user judged a formal manual-evidence artifact not important for the MVP. The 60 Progress checkboxes stay as-is and no `reviews/manual-verification.md` is produced for this slice.

### F3 — `DestructiveButtonStyle` restyled globally, changing untouched screens

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: `app/ChoNaBojoApp/Resources/Styles/Styles.xaml:147-155`
- **Detail**: Phases 5 and 6 were told to *use* `DestructiveButtonStyle` for the new Cancel / Remove / Leave buttons. Instead the shared style was rewritten from a filled `ErrorColor` button with white text into a transparent outlined button (`BackgroundColor` → `Transparent`, added `BorderWidth`/`BorderColor`, changed disabled colours). That reskins every pre-existing consumer outside this slice — notably the logout confirm button in `ConfirmDialog.xaml`, which is now outlined rather than the MD3-typical filled destructive button. The style's own doc comment at `:147-148` is now stale on both counts: it still describes "the ErrorColor confirm button in a destructive MD3 dialog" and asserts "Never used for a page-level primary action", while the style is now applied to inline card actions. `Value="Transparent"` is also a literal colour rather than a `{StaticResource}` token, a minor departure from the ui-guidelines "no hardcoded colours" rule. Touch target stays at 48pt, so no accessibility rule is broken.
- **Fix A ⭐ Recommended**: Keep the outlined treatment for the new inline card actions but restore the filled variant for dialog confirms — e.g. add a separate `DestructiveOutlineButtonStyle` — then refresh the stale comment on both styles.
  - Strength: Stops this slice from silently redesigning the logout dialog, and gives each context the MD3-appropriate emphasis; the plan only ever asked for a card-level button.
  - Tradeoff: Adds a second style to `Styles.xaml` and requires re-pointing `ConfirmDialog.xaml` at it.
  - Confidence: MEDIUM — visual intent is inferred from the stale comment and MD3 convention, not from a stated requirement.
  - Blind spot: Not verified on a device how the outlined button reads against the card background; the original filled style may genuinely have looked wrong inline.
- **Fix B**: Keep the single restyled shared style and just update the stale comment plus replace the `Transparent` literal with a token, accepting the logout dialog's new look as an intentional app-wide change.
  - Strength: Preserves one destructive style for the whole app, which is simpler to keep consistent as more destructive actions appear.
  - Tradeoff: Ships an unplanned visual change to a screen outside this slice without it ever being reviewed as a design decision.
  - Confidence: MEDIUM — depends entirely on whether the app-wide restyle was deliberate.
  - Blind spot: No screenshots or design reference in the repo to judge the intended look against.
- **Decision**: FIXED via Fix A — `DestructiveButtonStyle` restored to its pre-slice filled form (reverting 918c70a) and reserved for dialog confirms, so `ConfirmDialog.xaml:40` (logout) looks as it did before this slice. New `DestructiveOutlineButtonStyle` carries the outlined treatment for inline card actions and is now used by the three new buttons in `MyEventsPage.xaml` (Leave / Cancel event / Remove). Both style comments refreshed; `Value="Transparent"` kept as a literal to match the existing `SecondaryButtonStyle`. Verified on device 2026-09-12.

### F4 — Outbox `EventKey` deviates from the plan's explicit "do not change" contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `server/Push/PushIntentFactory.cs:230` (`BuildEventKey`)
- **Detail**: Phase 4 item 4 stated the per-attempt `UtcTicks` discriminator was for the revived `JoinRequestCreated` notification only, and instructed: *"Do not change keys for initial joins or the three new lifecycle types."* The implementation routes **every** caller through one `BuildEventKey` that always appends `:{joinRequest.CreatedUtc.Ticks}`, so initial joins and all three lifecycle types now use 5-part keys. The implementation is **more correct than the plan**: `PushOutbox.EventKey` carries a unique index (`server/Data/ChoNaBojoContext.cs:474-475`), and a user who joins → is accepted → leaves → re-joins → is accepted → leaves again would emit a second `ParticipantLeft` with an identical 4-part key and fail the insert. The plan's narrower rule would have produced a 500 on that path. The code's own XML remarks document exactly this reasoning. The only real problem is that the plan still says the opposite, so the next reader may "fix" it back.
- **Fix**: Amend the Phase 4 item 4 contract in `plan.md` to record that the attempt stamp applies to all join-request-derived keys, citing the repeat-leave collision that makes it necessary.
- **Decision**: FIXED — Phase 4 item 4 in `plan.md` now carries an "Amended 2026-09-12 during implementation review (F4)" note stating the attempt stamp applies to every join-request-derived key, with the repeat-leave collision and the `PushOutbox.EventKey` unique index cited. No code change; the implementation was already correct.

### F5 — Venue map changed despite the "no change to the venue map" guardrail

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:93-96,119-134`
- **Detail**: "What We're NOT Doing" states *"No change to the reject flow, the contact-reveal endpoint's shape, or the venue map."* `EventCardViewData` was nonetheless changed so a `Left` request is re-requestable (`IsRequestable`, `CanJoin` previously required `status is null`) and so `Removed` / `Cancelled` render status labels. This is necessary rather than gratuitous: the venue listing is the only join surface, so without it Phase 4's re-request-after-leaving rule would be unreachable from the UI and manual criterion 6.5 could not pass. The change is small and enabling, not a redesign — but a guardrail was crossed without being recorded.
- **Fix**: Add a one-line addendum to the plan's "What We're NOT Doing" noting that the venue-map guardrail was narrowed to exclude the re-request affordance required by Phase 4.
- **Decision**: FIXED — the venue-map bullet in the plan's "What We're NOT Doing" now carries an "Amended 2026-09-12 during implementation review (F5)" note recording the narrowed guardrail and why Phase 4 required it.

### F6 — EF Core sentinel warning logged on every API startup

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `server/Data/ChoNaBojoContext.cs` (`SportsEvent.Status` default-value configuration)
- **Detail**: Configuring `Status` with a database-generated default of `1` while `EventStatus` has no member equal to the CLR default `0` makes EF emit warning `20601` on every startup and on every `dotnet ef` invocation: *"configured with a database-generated default, but has no configured sentinel value."* Behaviour is currently correct — `0` is not a valid `EventStatus`, so falling through to the database default of `Active` is exactly what is wanted — but the warning is permanent log noise that trains readers to ignore EF model-validation output, and it would mask a genuine future warning on the same channel.
- **Fix**: Declare the sentinel explicitly (`.HasSentinel(0)` on the `Status` property) so the intent is stated and the warning stops.
- **Decision**: FIXED — `.HasSentinel(default(EventStatus))` declared before `.HasDefaultValue(EventStatus.Active)` in `server/Data/ChoNaBojoContext.cs` with a comment stating the intent. Verified: `dotnet build server` succeeds and `dotnet ef migrations has-pending-model-changes --project server` now reports "No changes have been made to the model since the last migration" with no 20601 warning.

### F7 — Cancel and participation transactions omit the template's `DbUpdateException` handling

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `server/Events/EventEndpoints.cs:411` (`CancelEventAsync`), `:1042` (`TransitionParticipationAsync`)
- **Detail**: Both new transactional handlers rely on `await using var transaction` auto-rollback with no explicit `try`/`catch`, whereas the template they were told to copy — `TransitionJoinRequestAsync` — wraps its save in a `DbUpdateException` handler. The divergence is defensible: the template's handler exists to absorb a unique-index collision when two sessions insert the same join-request row, and neither new path inserts a uniquely-keyed join row, while the `FOR UPDATE` event lock serialises concurrent cancels and removes so the outbox `EventKey` cannot collide either. Rollback-on-dispose is correct on every failure path. Noting it only so the divergence is a recorded decision rather than an oversight the next author copies blindly.
- **Fix**: No code change required; if the divergence should be explicit, add a short comment at each handler explaining why no `DbUpdateException` guard is needed.
- **Decision**: FIXED — explanatory comments added at `CancelEventAsync` and `TransitionParticipationAsync` in `server/Events/EventEndpoints.cs`, each stating why the template's `DbUpdateException` guard is unnecessary (no uniquely-keyed join-row insert; the `FOR UPDATE` event lock serialises concurrent transitions) and that other failures roll back on transaction dispose. No behavioural change.

## Verified clean

Checked and confirmed correct during this review — recorded so a future reviewer need not re-derive them:

- **Every load-bearing detail the plan called out is implemented correctly**: roster read before the status rewrite in cancel; `UpdatedUtc` reset to `NULL` on revive (required by `CK_EventJoinRequests_StatusUpdatedUtc`); `CreatedUtc` reset to the new attempt time; the same `FOR UPDATE` event lock on cancel, remove, leave and join; contact guards additionally requiring `EventStatus.Active` on the client; `Enum.IsDefined` fail-closed validation into `MyEventsResult.Unknown()`; the trailing `EstimatedEndsAtUtc <= now` client-side derivation; and the 60-second poll interval.
- **Raw SQL is parameterized.** All three `FOR UPDATE` locks use `Database.ExecuteSqlAsync($"…")` (the `FormattableString` overload). No `FromSqlRaw`/`ExecuteSqlRaw` string concatenation anywhere in `server/`.
- **Mutation + push-outbox write commit in one transaction**, so the system can never notify about an uncommitted change or commit one nobody is told about.
- **Authorization is inside the query and non-owners receive 404, never 403** — no "not yours" vs "does not exist" distinction. All new routes sit under `app.MapGroup("/api").RequireAuthorization()`.
- **The hard privacy boundary holds.** Contact columns are read only in `GetEventContactsAsync`; push bodies and data payloads carry no name, contact detail or event title; the client scrubs revealed contact payloads on cancel, remove and leave.
- **Enums guarded at both layers** per `lessons.md`: `CK_SportsEvents_Status IN (1,2,3)`, `CK_EventJoinRequests_Status IN (1..6)` and `CK_PushOutbox_Type IN (1..6)` match the shared Contracts enums exactly; migrations backfill `Status = 1` with `StatusChangedUtc` NULL, consistent with `CK_SportsEvents_StatusChangedUtc`.
- **`EventAutoCloseWorker` mirrors `PushDeliveryWorker`**: `AsyncServiceScope` per iteration (no captured scoped `DbContext`), clean shutdown on cancellation without a spurious error log, and log-and-swallow so a transient database error cannot terminate the host.
- **One 409 shape**: every new conflict returns the typed `EventConflictResponse` with a stable `EventConflictCodes` value; no anonymous bodies, no second shape.
- **No shared-project contamination**: the new shared additions are pure DTO/enum/const with no ASP.NET Core, EF Core, Npgsql or MAUI references.
- **All other "What We're NOT Doing" guardrails held**: no event editing, no un-cancel, no auto-close push, no organizer hand-over, no test project, no outstanding-request cap (correctly deferred into `context/foundation/todo.md`), no reject-flow or contact-endpoint shape change, no iOS code.
- **Unplanned-but-justified incidental changes**: `PushNotificationPresenter.cs` and `IPushNavigationRouter.cs` had to learn the three new push types or lifecycle notifications would be dropped or crash presentation; `FeedbackService.cs` + `ConfirmDialog.xaml.cs` fix a latent dismiss-result ambiguity that this slice's three new confirm dialogs exposed; `solutions/ChoNaBojo.slnx` and `context/foundation/todo.md` are documentation bookkeeping.

## Triage (2026-09-12)

> **Triaged by @Sarnapa.** Every `Decision:` field in this report records a call made by the repository owner during an interactive `/10x-impl-review` triage session — not an automated or agent-chosen outcome. The agent proposed the fix options and applied the approved edits; the decisions, including the one skip and the on-device verification of F1 and F3, are the user's.

All seven findings triaged. Six fixed, one skipped.

| Finding | Decision (by @Sarnapa) |
|---|---|
| F1 — contact access revoked on auto-close | FIXED via Fix A (server gate + client mirrors) |
| F2 — manual criteria without evidence | SKIPPED — not important for the MVP |
| F3 — `DestructiveButtonStyle` restyled globally | FIXED via Fix A (filled style restored + new outline style) |
| F4 — outbox `EventKey` vs. plan contract | FIXED — plan amended, code was already correct |
| F5 — venue-map guardrail crossed | FIXED — guardrail addendum added to the plan |
| F6 — EF sentinel warning | FIXED — `.HasSentinel(default(EventStatus))` |
| F7 — missing `DbUpdateException` guard | FIXED — divergence documented at both handlers |

Post-triage verification:

| Check | Command | Result |
|---|---|---|
| Solution builds | `dotnet build solutions/ChoNaBojo.slnx` | PASS — 0 errors, 119 warnings (unchanged `MVVMTK0045` baseline) |
| Android app builds | `dotnet build app/ChoNaBojoApp -f net10.0-android` | PASS — 0 errors, 9 warnings (unchanged baseline) |
| No EF model drift | `dotnet ef migrations has-pending-model-changes --project server` | PASS — no pending changes, and the 20601 sentinel warning is gone |

Manual re-verification of the F1 and F3 changes: **DONE on device (2026-09-12)** — contact details remain reachable on a finished (`Closed`) event and still disappear on a cancelled one; the logout confirm dialog renders filled while the Cancel / Remove / Leave card buttons render outlined.

## Out-of-scope note

`server/server.csproj` builds with `NU1903`: `Microsoft.OpenApi 2.0.0` carries a known high-severity advisory. Pre-existing and unrelated to this change — flagged only so it is not lost; address out of band.
