<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Event Listing and Join Request

- **Plan**: `context/changes/event-listing-and-join-request/plan.md`
- **Scope**: Phases 1–5 of 5 (full plan)
- **Date**: 2026-09-07
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 3 observations
- **Commit range**: `869b99e..HEAD` (`057eb95`, `62e196e`, `acaeb9f`, `62a937b`, `4f341af`)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Verification run during this review

Automated criteria re-run and passing:

- `dotnet build solutions\ChoNaBojo.slnx` — 0 errors (1.1, 1.2, 2.1, 3.1, 4.1, 5.1)
- `dotnet build app\ChoNaBojoApp -f net10.0-android` — 0 errors (4.2, 5.2)
- Privacy scans on `EventDTOs.cs`, `Services/Events`, `MapViewModel.cs`, `MapPage.xaml`, `VenueEventsPage.xaml` — no contact/credential matches (1.3, 4.4, 5.4)
- `dotnet ef migrations list --project server` includes `20260906143339_AddEventJoinRequests` (2.3)
- `dotnet ef migrations has-pending-model-changes --project server` — no model drift (2.4)
- `dotnet ef migrations script --project server --idempotent` contains `CK_EventJoinRequests_Status CHECK ("Status" IN (1, 2, 3))`, `FK_..._SportsEvents_SportsEventId ON DELETE CASCADE`, `FK_..._Users_RequesterUserId ON DELETE RESTRICT`, unique `IX_EventJoinRequests_SportsEventId_RequesterUserId`, `IX_EventJoinRequests_SportsEventId_Status`, `IX_SportsEvents_VenueId_EstimatedEndsAtUtc` (2.5)
- Route/auth scan: `GET /venues/{venueId:int}/events` and `POST /events/{eventId:guid}/join-requests` are mapped under `app.MapGroup("/api").RequireAuthorization()` (`server/Program.cs:124-127`); no `AllowAnonymous` (3.2)
- XAML bounding: `VenueEventsPage.xaml:37` uses `RowDefinitions="*,Auto"` with the `CollectionView` in row 0 and Create event in row 1; no `ScrollView` in the page (5.3)

Not re-runnable in this environment: probes P-01…P-07 (3.3–3.9) and all DB/device manual items — they need a live API, Supabase, and an Android device. They remain human-attested at their phase gates (see F5).

## Findings

### F1 — Event free text is broadcast to every authenticated user with no contact-pattern guard

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs:41-52`, `server/Events/EventEndpoints.cs:209-232`, `app/ChoNaBojoApp/Views/VenueEventsPage.xaml:312-334`
- **Detail**: The listing projects `Title` and `Description` and the card binds both, so any authenticated user sees organizer-authored free text before any approval. Existing `EventValidation` only checks blank/length. The product's hard privacy rule is that contact info reaches only approved participants; an organizer typing a phone number or messenger handle into the description bypasses the S-05 reveal gate entirely, and nothing stops a user pasting a *third party's* contact details there. S-03 created these fields but only the author could see them — S-04 is the change that makes them public, so the exposure is introduced here.
- **Fix A ⭐ Recommended**: Record the decision now and defer enforcement to S-05, where contact reveal is designed.
  - Strength: S-05 already owns the contact-reveal boundary end to end; adding pattern rejection there keeps one place responsible instead of splitting the rule across two slices. Cost here is a plan/lessons note, not code.
  - Tradeoff: The window stays open until S-05 ships; nothing blocks self-published contact details in the meantime.
  - Confidence: MEDIUM — depends on S-05 actually being scheduled before any real users exist. For a solo, pre-launch MVP the exposure is theoretical.
  - Blind spot: Not verified whether the MVP will be exposed to real users before S-05 lands.
- **Fix B**: Add server-side public-text validation in `shared/ChoNaBojo.Validation` rejecting phone/email/URL-like patterns in `Title`/`Description`, enforced on create.
  - Strength: Closes the bypass at the only write point, and validation lives in the dependency-free Validation project so the client can pre-check with the same rule.
  - Tradeoff: Pattern matching on free text is a false-positive magnet ("meet at hall 5, ext. 22"), and it edits the S-03 create path this plan explicitly kept out of scope.
  - Confidence: MEDIUM — the mechanism is easy; the heuristic is not.
  - Blind spot: No measurement of how often legitimate descriptions would trip the patterns.
- **Decision**: PENDING

### F2 — Deleted-event race on join insert surfaces as an untyped failure, not `event_not_found`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `server/Events/EventEndpoints.cs:355-377`
- **Detail**: `SaveChangesAsync` is guarded only for the unique-key constraint (`HasConstraint(exception, UniqueJoinRequestConstraint)`). If the event row disappears between the `SportsEvents` read at `:303-315` and the insert, `FK_EventJoinRequests_SportsEvents_SportsEventId` fails and the request escapes as a 500, which the client maps to the "unknown" state instead of the typed `event_ended`/`event_not_found` recovery path the plan specified. No delete path exists yet, so this is currently unreachable through the API — it becomes live as soon as S-07 adds event cancellation.
- **Fix**: Add a second `catch (DbUpdateException e) when (HasConstraint(e, "FK_EventJoinRequests_SportsEvents_SportsEventId"))` that detaches the entry and returns `EventConflictResponse(EventConflictCodes.EventNotFound, "eventId", …)`, mirroring the existing unique-key handler.
  - Strength: Reuses the `HasConstraint` + detach pattern already proven three lines above; the client's stale-conflict reload path then works unchanged.
  - Tradeoff: Adds an unreachable branch until S-07 — dead code for now.
  - Confidence: HIGH — the constraint name is fixed by the migration at `server/Migrations/20260906143339_AddEventJoinRequests.cs:29-34`.
  - Blind spot: None significant.
- **Decision**: PENDING

### F3 — Availability presets are recomputed on every reload, not frozen at selection time

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:452-466`, `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:802-825`
- **Detail**: The plan specified presets "defined from local calendar boundaries **at selection time**". `SelectAvailabilityFilterAsync` computes the window, then discards it (`_availabilityWindow = null` at `:462`), and `ReloadSelectedVenueEventsAsync` recalls `EventAvailabilityConversion.ForPreset(...)` on each reload at `:812-825`. So `Today` follows the wall clock across midnight instead of staying pinned. The implemented behavior is arguably better — the chip still says "Today" and now means today — but it differs from the written contract, and nothing in the code says the divergence is intentional.
- **Fix**: Record the intent — add a one-line comment at `ReloadSelectedVenueEventsAsync` stating presets are deliberately dynamic per reload while `Custom` stays pinned in `_availabilityWindow`, and align the plan wording.
- **Decision**: PENDING

### F4 — The create path's conflict codes still bypass the new `EventConflictCodes` vocabulary

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `server/Events/EventEndpoints.cs:428-459`
- **Detail**: Phase 1 added `EventConflictCodes` specifically to "prevent string drift between endpoint classification and client recovery", and the new listing helper `ClassifyVenueSportReferenceAsync` (`:245-278`) uses the constants correctly. The sibling `ClassifyMissingReferenceAsync`, used by the S-03 create path in the same file, still emits `"venue_not_found"`, `"sport_not_found"`, and `"sport_not_supported_at_venue"` as literals — and `sport_not_found` has no constant at all, so a code the client must handle is absent from the shared vocabulary. The client already compares against `EventConflictCodes.VenueNotFound` (`app/ChoNaBojoApp/ViewModels/MapViewModel.cs:1201-1204`), so the two sides agree only by coincidence of matching string values. This is pre-existing code, but the new constants file makes it a live divergence inside the file this change edited.
- **Fix**: Replace the three literals in `ClassifyMissingReferenceAsync` with `EventConflictCodes` constants and add a `SportNotFound = "sport_not_found"` constant.
- **Decision**: PENDING

### F5 — Human-gated verification left no recorded evidence, including the only exercise of Accepted/Rejected

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/event-listing-and-join-request/plan.md:465-549` (Progress)
- **Detail**: Every manual and probe checkbox is `[x]` with a commit SHA, but the plan deliberately adds no test project, so probes P-01…P-07, the `EXPLAIN` plans (3.11), the Supabase constraint checks (2.7), and manual step 9 leave no artifact in the repo. Step 9 matters most: the plan states S-04 has **no code path** that produces a non-`Pending` row, so seeded Supabase rows are the only exercise of the `Joined` and `Request rejected` card states — and S-05 will build directly on that untested surface. Nothing here contradicts the diff; the point is that a future reviewer (or S-05's plan) cannot tell which checks were actually observed.
- **Fix**: Append a short verification log (probe → observed status/body, `EXPLAIN` plan summary, step-9 screenshots or notes) under `context/changes/event-listing-and-join-request/reviews/` so S-05 can rely on it.
- **Decision**: PENDING

## Notes

- Plan drift sweep found **MATCH** on every planned change across all five phases, with no MISSING items and no unplanned product-code changes. The only diff entries outside the plan's file contracts are the change's own metadata files (`change.md`, `plan.md`, `plan-brief.md`).
- The three critical implementation details were verified explicitly in code: existing-request lookup precedes all dynamic checks (`server/Events/EventEndpoints.cs:289-299`); auto-accept never activates, joins always persist `Pending` (`:348-353`); and the `AddressSearchViewModel` supersession pattern is genuinely reproduced via `_venueEventsCancellation` / `_joinRequestCancellation` with `CreateLinkedTokenSource` and `ReferenceEquals` staleness guards (`app/ChoNaBojoApp/ViewModels/MapViewModel.cs:208-210, 827-830, 875-878, 931-937`).
- Recorded lessons were checked as priors: the persisted enum is guarded at both layers (`Enum` values plus `CK_EventJoinRequests_Status`), shared projects stayed dependency-free with no EF/ASP.NET/MAUI leakage, and the new endpoints use the single recorded 409/400 body shapes. F4 is the one place that convention is not yet applied uniformly.
