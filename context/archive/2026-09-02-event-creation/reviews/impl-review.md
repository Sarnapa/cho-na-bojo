<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Event Creation Implementation Plan

- **Plan**: `context/changes/event-creation/plan.md`
- **Scope**: Phases 1-5 of 5 (full plan)
- **Date**: 2026-09-05
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 5 observations
- **Triage**: complete (2026-09-06) — 10 of 10 findings resolved, 0 skipped

## Triage summary

All ten findings were triaged on 2026-09-06. Eight were fixed in code; two (F2, F10) were plan-documentation deviations recorded as addenda. One deferred item (the Uranium UI field migration behind F4) is queued as **RF-1** in `context/foundation/review-fixes.md`.

| Finding | Decision |
|---------|----------|
| F1 | Fixed (Fix A) — normalize-before-validate + terminal `DbUpdateException` guard |
| F2 | Fixed (Fix A) — Addendum A-1 in `plan.md`; criterion 4.7 restated |
| F3 | Fixed — CHECK interpolates `EventPolicy.MaximumDuration` |
| F4 | Fixed (Fix A) — `HeightRequest="48"`; Uranium migration deferred to RF-1 |
| F5 | Fixed — private helper replaced by shared `TextNormalization` |
| F6 | Fixed — explicit 30 s `HttpClient.Timeout` |
| F7 | Fixed — `ValidationProblemResponse` moved to Contracts (`ErrorDTOs.cs`) |
| F8 | Fixed — default `EndTime` truncated to whole minutes |
| F9 | Fixed — `EventDetailViewModel` derives from `ViewModelBase` |
| F10 | Fixed — Addendum A-2 in `plan.md` |

**Post-triage verification** (2026-09-06):

- `dotnet build solutions\ChoNaBojo.slnx` — 0 errors, 71 warnings (unchanged baseline: pre-existing `MVVMTK0045` AOT advisories on `MapViewModel` plus `NU1903`)
- `dotnet build app\ChoNaBojoApp -f net10.0-android` — 0 errors, 0 warnings
- `dotnet ef migrations has-pending-model-changes --project server` — "No changes have been made to the model since the last migration" (the F3 interpolation renders identical SQL)
- Privacy scan of `EventDetailPage.xaml`, `EventDetailViewModel.cs`, and the event/error Contracts DTOs for `phone|email|messenger|contact|passwordHash|token` — no match

Manual re-verification still owed on device for the F4 touch-target change (picker layout inside the two-column `Grid`) and the F6 timeout behavior.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Success criteria re-verification

All automated criteria were re-run during this review and pass:

- `dotnet build solutions\ChoNaBojo.slnx` — 0 errors (71 warnings, all pre-existing `MVVMTK0045` AOT advisories also present on `MapViewModel`)
- `dotnet build app\ChoNaBojoApp -f net10.0-android` — 0 errors, 0 warnings
- `dotnet ef migrations has-pending-model-changes --project server` — "No changes have been made to the model since the last migration", exit 0
- `dotnet ef migrations list --project server` — includes `20260903191850_AddSportsEvents`
- Privacy scan of `app\ChoNaBojoApp\Views\EventDetailPage.xaml` + `ViewModels\EventDetailViewModel.cs` — no match
- Privacy scan of `shared\ChoNaBojo.Contracts\DTOs` — matches exist only in `AuthDTOs.cs`, none reachable from the event contracts (as the criterion is scoped)
- Placeholder scan of `MapPage.xaml` for `Coming soon|IsEnabled="False"` — no match

Manual criteria 1.4-5.10 are all marked `[x]`. Every manual item has corresponding observable evidence in the diff except **4.7** ("overnight, 24-hour ... follow the agreed device-time rules"), which was checked against behavior the plan does not describe — see F2.

## Findings

### F1 — Sub-microsecond duration passes validation but violates the database CHECK

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: server/Events/EventEndpoints.cs:48, 84-85, 184-189
- **Detail**: `EventValidation.ValidateCreateEventRequest` runs at :48 against the **raw** request values (`duration <= TimeSpan.Zero` rejects only non-positive durations, `EventValidation.cs:77-79`). `NormalizeUtcTimestamp` (:184-189) then truncates start and end to microsecond resolution **independently**, after validation. Two instants less than 1 µs apart therefore pass validation but collapse to equal stored values, violating `CK_SportsEvents_TimeRange` (`"EstimatedEndsAtUtc" > "StartsAtUtc"`, `ChoNaBojoContext.cs:232-236`). Only two constraints have `catch` filters — `IX_SportsEvents_OrganizerUserId_ClientRequestId` (:95) and `FK_SportsEvents_VenueSports_VenueId_SportId` (:110); the four CHECK constraints and the organizer FK have no handler, so the `DbUpdateException` escapes as a bare 500. The client maps 500 to its unknown branch and offers a Retry that resends the identical snapshot — which fails identically, leaving the form permanently stuck. Not reachable from the MAUI form (it emits whole-second values), but `POST /api/events` is an authenticated public surface. Plan criterion 3.8 exercised sub-microsecond precision but evidently not a sub-microsecond *duration*.
- **Fix A ⭐ Recommended**: Normalize both timestamps before validating, and add a terminal `catch (DbUpdateException)` returning a typed `EventConflictResponse`.
  - Strength: Makes the validator and the CHECK constraint judge byte-identical values, closing the class of drift rather than the single instance; the catch-all guarantees no constraint can ever surface as a 500.
  - Tradeoff: Touches the endpoint's ordering, so the Phase 3 replay-before-validation sequencing must be preserved carefully (normalization must stay *after* the idempotency lookup).
  - Confidence: HIGH — the mismatch is arithmetic and directly readable at :48 vs :184-189.
  - Blind spot: Not verified whether any other caller depends on the current normalize-late ordering.
- **Fix B**: Add only the terminal `catch (DbUpdateException)` mapping to a typed 400/409.
  - Strength: Minimal, single-block edit; removes the stuck-form outcome immediately.
  - Tradeoff: Leaves the validator and the database disagreeing about what is valid — the user gets a confusing error for input the client considered acceptable.
  - Confidence: HIGH — the catch pattern already exists twice in the same method.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — `TruncateToMicrosecond` now runs before validation (`EventEndpoints.cs:48-62`), so the validator and `CK_SportsEvents_TimeRange` judge identical values; a terminal `catch (DbUpdateException)` (:120-134) maps any remaining constraint violation to a typed 400 `ValidationProblem` under the `event` key. 400 was chosen over 409 because the client's 409 path triggers a misleading catalog refresh, while the 400 path clears `_pendingRequest` and surfaces the message as `GeneralError`.

### F2 — End-day roll rule replaced by an unplanned explicit End-date picker

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: app/ChoNaBojoApp/Services/Events/EventTimeConversion.cs:78-95; app/ChoNaBojoApp/Views/CreateEventPage.xaml:135-152; app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs:83-88, 295-308
- **Detail**: The plan specified one date plus two times, with the end day **inferred**: "roll end to the next day when its clock value is less than or equal to start" (Phase 4 §4), and "equal clock values represent 24 hours and remain valid". The implementation instead takes a separate `endDate` parameter and never rolls — `if (localEnd <= localStart)` returns "End date and time must be after start date and time." (`EventTimeConversion.cs:87-95`) — backed by a second `DatePicker` bound to `EndDate` with `EndDateMinimum` clamping. Consequence: start 20:00 / end 06:00 on the picked date is now a validation error rather than an overnight event, and the user must discover and change a second field. The capability is not lost (both overnight and the exact-24-hour case remain expressible by choosing the next day), but the interaction model and the two documented inference rules are gone, and manual criterion 4.7 was ticked against undescribed behavior. The DST-invalid/ambiguous rejection and zero-offset UTC output halves of the contract are correctly implemented (`:132-158`).
- **Fix A ⭐ Recommended**: Record the explicit end-date input as a plan addendum and restate criterion 4.7 in terms of the shipped behavior.
  - Strength: An explicit date is arguably less surprising than silent inference and keeps every case reachable; preserves working, manually verified code and realigns the plan before `/10x-archive` and S-04 treat it as ground truth.
  - Tradeoff: The plan becomes a moving target, and the inference-based UX decision is reversed without ever being explicitly weighed.
  - Confidence: HIGH — behavior is fully verified in code and the capability gap is nil.
  - Blind spot: Whether a second date picker measurably slows the common same-day case was never user-tested.
- **Fix B**: Restore the planned single-date form with the roll rule in `EventTimeConversion`.
  - Strength: Delivers exactly the reviewed and approved contract; fewer inputs on the form.
  - Tradeoff: Discards working code, requires re-running the Phase 4 DST/overnight manual matrix on a device, and reintroduces inference that some users find opaque.
  - Confidence: MEDIUM — the roll rule interacts with the DST-ambiguity checks, and that combination was never exercised on hardware.
  - Blind spot: `EndDateMinimum`/`OnEndDateChanged` plumbing and the XAML would both need unwinding.
- **Decision**: FIXED via Fix A — recorded as Addendum A-1 in `plan.md`; the superseded Phase 4 §4 contract paragraph now carries a pointer, and criterion 4.7 was restated (both the Phase 4 manual bullet and the Progress line) in terms of the explicit end-date picker.

### F3 — Duration CHECK hardcodes 24 hours while sibling constraints interpolate `EventPolicy`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: server/Data/ChoNaBojoContext.cs:232-236
- **Detail**: `CK_SportsEvents_TimeRange` embeds the literal `INTERVAL '24 hours'`, while the neighbouring constraints interpolate the shared constants — `ParticipantLimit` at :226-229 uses `{EventPolicy.ParticipantLimitMinimum}`/`{EventPolicy.ParticipantLimitMaximum}`, and title/description lengths use `EventPolicy.TitleMaxLength`/`DescriptionMaxLength`. `EventPolicy.MaximumDuration` is the single source of truth for both `EventValidation.cs:80` and `EventTimeConversion.cs:120`, so changing it would silently leave the database enforcing the old bound — precisely the two-layer drift the recorded "guard at both layers" lesson exists to prevent.
- **Fix**: Interpolate the bound: `$"""... + INTERVAL '{EventPolicy.MaximumDuration.TotalHours:0} hours'"""`. This renders the identical SQL string, so `has-pending-model-changes` stays clean and no new migration is needed.
- **Decision**: FIXED — `CK_SportsEvents_TimeRange` now interpolates `{EventPolicy.MaximumDuration.TotalHours:0}`. Verified: `dotnet ef migrations has-pending-model-changes --project server` still reports "No changes have been made to the model since the last migration".

### F4 — Native date/time pickers hand-wrapped in `Border` instead of Uranium UI fields

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: app/ChoNaBojoApp/Views/CreateEventPage.xaml:92-176
- **Detail**: Four blocks wrap a bare `<DatePicker>`/`<TimePicker>` in `<Border Padding="8,0" BackgroundColor="{StaticResource SurfaceColor}" Stroke="{StaticResource DividerColor}" StrokeShape="RoundRectangle 8">`, with a separate `LabelStyle` caption standing in for a floating label. `context/foundation/ui-guidelines.md:11` explicitly forbids this: "Manually build custom input fields by wrapping native controls. Always use Uranium UI components." The wrappers also set no `HeightRequest="48"`, missing the touch-target rule reaffirmed at `ui-guidelines.md:37, 56, 118`. The rest of the page complies via `material:TextField`/`material:SelectField`.
- **Fix A ⭐ Recommended**: Add `HeightRequest="48"` to the four wrappers now and record the Uranium field migration as a follow-up.
  - Strength: Closes the accessibility violation (the part with real user impact) immediately with a four-attribute edit, without a device re-test of the whole time-entry matrix.
  - Tradeoff: The guidelines violation itself persists until the follow-up lands.
  - Confidence: HIGH — the touch-target rule is unambiguous and stated three times in the guidelines.
  - Blind spot: Not verified whether 48pt wrappers change the picker layout inside the two-column `Grid`.
- **Fix B**: Replace with `material:DatePickerField`/`material:TimePickerField` plus keyed styles alongside `TextFieldStyle`/`SelectFieldStyle`.
  - Strength: Fully complies with §1 and §6A, gets floating labels and native touch targets, and makes the form internally consistent.
  - Tradeoff: Requires new keyed styles, re-binding four controls, and re-running the Phase 4 device matrix for time entry.
  - Confidence: MEDIUM — the guidelines note at :59 that UraniumUI 3.0 dropped `MaterialButton`, so the exact availability of these field controls in the pinned version must be confirmed first.
  - Blind spot: The pinned UraniumUI version's control surface was not checked.
- **Decision**: FIXED via Fix A — `HeightRequest="48"` added to all four `Border` wrappers (`CreateEventPage.xaml:92-180`); Android head rebuilds with 0 warnings. The Uranium field migration is queued as **RF-1** in `context/foundation/review-fixes.md` (kept in `foundation/` rather than the change folder so it survives archiving).

### F5 — Private copy of a shared `ChoNaBojo.Utils` helper

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs:671 (used at :382)
- **Detail**: `private static string? NormalizeOptionalText(string value)` duplicates `ChoNaBojo.Utils.Text.TextNormalization.NormalizeOptionalText` (`shared/ChoNaBojo.Utils/Text/TextNormalization.cs:16`), which the server already calls at `EventEndpoints.cs:82`. The recorded lesson places pure stateless cross-boundary helpers in `ChoNaBojo.Utils` "and nowhere else". Two copies can drift so client and server normalize the description differently, producing spurious blank-description CHECK failures.
- **Fix**: Delete the private method and call `TextNormalization.NormalizeOptionalText(Description)`.
- **Decision**: FIXED — private copy removed; `CreateEventViewModel.cs:383` now calls the shared `TextNormalization.NormalizeOptionalText`, matching `EventEndpoints.cs`. Android head rebuilds with 0 warnings.

### F6 — No explicit HTTP timeout behind a modal the user cannot leave

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: app/ChoNaBojoApp/MauiProgram.cs:50-53; app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs:423, 490
- **Detail**: The create call passes `CancellationToken.None`, and the `ChoNaBojoApi` client sets only `BaseAddress`, leaving `HttpClient.Timeout` at the 100 s default. Meanwhile `CreateEventPage.OnBackButtonPressed` correctly returns `true` while `IsRequestInFlight` and `MapPage._isOpeningCreateForm` stays set. On a black-holed connection the user is therefore locked on a spinner for up to 100 s with Back disabled and the map's Create button inert. This is consistent with the plan's rule that 5 s is a wait-copy change and not a client abort, so it is intentional — but 100 s is an accidental bound rather than a chosen one.
- **Fix**: Set an explicit `client.Timeout` (e.g. 30 s) on the `ChoNaBojoApi` client; the `ClientRequestId` already makes the ensuing retry replay-safe.
- **Decision**: FIXED — `client.Timeout = TimeSpan.FromSeconds(30)` on the `ChoNaBojoApi` client (`MauiProgram.cs:50-59`). A timeout surfaces as `TaskCanceledException` with an unrequested token, which `ApiService.CreateEventAsync` already maps to `CreateEventResult.Network()` — i.e. the retry-safe pending state, not the stuck unknown branch. `ChoNaBojoAuth` was left alone (out of the finding's scope).

### F7 — 400 body DTO is app-local and lives in the Auth area

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: app/ChoNaBojoApp/Services/ApiService.cs:180; app/ChoNaBojoApp/Services/Auth/AuthResults.cs:130
- **Detail**: The Events path deserializes `ValidationProblemResponse`, declared in the app's Auth area rather than in `ChoNaBojo.Contracts`. The 409 shape (`EventConflictResponse`) was correctly placed in Contracts. The recorded lesson requires each status code's body to be "named as a shared Contracts DTO", so the server has no compile-time tie to the shape it emits via `Results.ValidationProblem` (`EventEndpoints.cs:215`), and the Events feature now depends on an Auth-area client record.
- **Fix**: Move `ValidationProblemResponse` to `shared/ChoNaBojo.Contracts/DTOs/` and have both the Auth and Events paths reference it.
- **Decision**: FIXED — moved to the new `shared/ChoNaBojo.Contracts/DTOs/ErrorDTOs.cs` and removed from `AuthResults.cs`. Both `ApiService` call sites (:180 Events, :233 Auth) now bind to the shared Contracts DTO. Android head rebuilds with 0 warnings.

### F8 — Default end time retains sub-minute noise

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: app/ChoNaBojoApp/ViewModels/CreateEventViewModel.cs:214
- **Detail**: `StartTime` is truncated to whole minutes at :212 (`new TimeSpan(suggestedStart.Hour, suggestedStart.Minute, 0)`), but `EndTime = suggestedEnd.TimeOfDay;` keeps the seconds and milliseconds of `DateTime.Now.AddHours(2)`. The `Format="HH:mm"` picker never shows them, so an untouched default submits an `EstimatedEndsAtUtc` a few seconds off from what the user saw and from what `EventDetailPage` renders back.
- **Fix**: `EndTime = new TimeSpan(suggestedEnd.Hour, suggestedEnd.Minute, 0);`.
- **Decision**: FIXED — default `EndTime` now truncated to whole minutes, matching `StartTime` at :212 and the `Format="HH:mm"` picker the user actually sees.

### F9 — `EventDetailViewModel` is the only ViewModel not deriving from `ViewModelBase`

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: app/ChoNaBojoApp/ViewModels/EventDetailViewModel.cs:8
- **Detail**: Declared as `: ObservableObject`, whereas `MapViewModel`, `CreateEventViewModel`, `LoginViewModel`, `RegisterViewModel` and `AddressSearchViewModel` all derive from `ViewModelBase`. The page is read-only today, so nothing is broken — but the shared `IsBusy`/`IsNotBusy` contract that other pages' `IsEnabled` bindings rely on is absent, so the first load or refresh added here (S-04 onward) will re-invent it.
- **Fix**: Derive from `ViewModelBase`.
- **Decision**: FIXED — `EventDetailViewModel` now derives from `ViewModelBase`, inheriting the shared `IsBusy`/`IsNotBusy` contract. Android head rebuilds with 0 warnings.

### F10 — Undocumented change to map re-centering behavior

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: app/ChoNaBojoApp/Views/MapPage.xaml.cs:25, 57, 194
- **Detail**: A new `_hasAppliedMapRegion` guard makes `ShowInitialRegion` apply at most once per page lifetime, so returning from the create/detail modals no longer re-centers the map. Phase 5 only required that map/venue selection be retained beneath the detail modal; this is a plausible implementation of that intent but is an unstated behavior change to an existing feature, so no verification step covers it.
- **Fix**: Document it in the plan addendum alongside F2 (it is the mechanism that satisfies criterion 5.6).
- **Decision**: FIXED — recorded as Addendum A-2 in `plan.md`, including the scope note that the guard changes viewport behavior for every return to `MapPage`, not only the S-03 flow. Verified that `OnMapCenterRequested` (`MapPage.xaml.cs:113-119`) calls `ShowInitialRegion` unconditionally, so on-demand re-centering is unaffected.

## Explicitly not flagged

- **Idempotent replay does not compare the stored event against the incoming payload.** This looks like a gap but is a deliberate, documented plan decision ("A request id that already exists always returns the stored event; the payload is not re-compared... a same-key/different-payload request is unreachable from the app"). Implementation matches the decision; not a finding.
- **`NU1903` high-severity advisory on `Microsoft.OpenApi` 2.0.0** in `server/server.csproj` — pre-existing, unrelated to this change.
- **71 `MVVMTK0045` AOT warnings** — the same advisory fires on pre-existing `MapViewModel` fields; consistent with the established codebase pattern.

## What was verified clean

- **Security & privacy**: `POST /api/events` sits inside the `RequireAuthorization()` group (`Program.cs:124-127`); organizer identity comes only from `httpContext.GetUserId()` (`EventEndpoints.cs:37`) and `CreateEventRequest` carries no organizer field; `ToResponse` (:191-211) projects only event/venue/sport data. No contact, hash or token reaches any response body or XAML binding. No raw SQL, hardcoded secrets, or CORS change.
- **Idempotency sequencing**: `FindEventAsync` (:38-46) runs before validation (:48), so an exact retry cannot fail merely because time advanced. The unique-violation race is caught, detached, requeried and returned as 200 (:95-107).
- **Timestamp normalization**: one helper, `.UtcDateTime` (never `.DateTime`), microsecond truncation arithmetic correct.
- **Database constraints**: all five CHECKs plus the unique `(OrganizerUserId, ClientRequestId)` index present; both FKs `RESTRICT`; the forbidden `(VenueId, EstimatedEndsAtUtc)` index correctly absent; migration additive-only; no `Database.Migrate()` at startup.
- **Concurrency & resources**: `VenueCatalog._loadLock.Release()` in `finally` (:126-129); atomic versioned snapshot swap only after both fetches succeed, last good catalog retained on failure; `HttpResponseMessage` and the long-wait `CancellationTokenSource` are `using`-scoped; the long-wait task is awaited in `finally` (`CreateEventViewModel.cs:452-453`).
- **In-flight dismissal guard**: `OnBackButtonPressed` returns `true` while in flight (`CreateEventPage.xaml.cs:64-67`); `OnDisappearing` completes only when nothing is outstanding (:46-61); dead-completion-source results are ignored and `TrySetResult` is used throughout.
- **Conflict recovery**: all four server conflict codes terminate in one of the two defined client states, with venue-level and sport-level paths split as planned (`CreateEventViewModel.cs:465-540`).
- **Architecture**: shared projects stayed dependency-free; privileged logic stayed server-side; no new packages, no `.slnx`/`.csproj` changes, no iOS or CI changes.
- **Scope guardrails**: no event listing, join request, participant persistence, contact reveal, notifications, status enum, edit/cancel/auto-close, overlap logic, `GET /api/events/{id}`, or test/UI-automation project leaked in.
