<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Event Listing and Join Request (S-04)

- **Plan**: `context/changes/event-listing-and-join-request/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-06
- **Verdict**: REVISE → **SOUND** after triage (all 6 findings fixed on 2026-09-06)
- **Findings**: 0 critical, 4 warnings, 2 observations — 6 fixed, 0 skipped, 0 accepted, 0 dismissed

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING → PASS (F5 fixed) |
| Blind Spots | WARNING → PASS (F3 fixed) |
| Plan Completeness | WARNING → PASS (F1, F2, F4, F6 fixed) |

## Grounding

21/21 paths ✓, 14/15 code claims ✓ (1 nuanced → F5), brief↔plan ✓, Progress↔Phase contract ✓ (5 phases, 41 steps, format conformant).

Verified and clean (no findings needed): idempotency via unique `(SportsEventId, RequesterUserId)`; the `/api` group + `HttpContext.GetUserId()` auth path (`server/Program.cs:123-127`, `server/Auth/CurrentUser.cs:30-33`); `venueId:int` / `eventId:guid` route constraints match actual key types (`Venue.Id` int, `SportsEvent.Id` Guid); safe token-refresh replay — `CloneRequest` re-creates the request with a buffered body (`AuthenticatingHttpMessageHandler.cs:54-60,82-99,186-215`); `EventConflictCodes.cs` is genuinely new and centralizes literals currently inline at `server/Events/EventEndpoints.cs:184,193,201`; `DatePickerField`/`TimePickerField` do exist in UraniumUI.Material 3.0.0; no `Database.Migrate()` call anywhere in `server/`.

## Findings

### F1 — "Bounded grid layout" is unspecified; the sheet row is Auto-sized

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 5 §2 — Event cards and venue-sheet layout
- **Detail**: Plan says "Replace the outer sheet ScrollView with a bounded grid layout whose vertical CollectionView owns scrolling" but never says what establishes the bound. `app/ChoNaBojoApp/Views/MapPage.xaml:225-226` is `<Grid IsVisible="{Binding IsVenueSheetVisible}" RowDefinitions="*,Auto">` with the sheet `<Border Grid.Row="1">` at :237 — an Auto row, i.e. content-driven and unconstrained. Removing the inner `ScrollView` (:248) without changing the outer row hands the CollectionView infinite available height: on Android it typically measures to zero or grows the sheet past the screen. Criterion 5.3 only asserts the CollectionView isn't nested in a ScrollView — it does not assert a height bound, so the automated gate passes while the screen is broken. The brief already names this the phase's key risk ("Scroll/layout pressure in the existing bottom sheet") without resolving it.
- **Fix A ⭐ Recommended**: Specify the bound explicitly in the Contract — outer sheet row becomes `*` with a `MaximumHeightRequest` (or proportional row) on the sheet Border, and the CollectionView is the only star-sized row inside. Pair criterion 5.3 with a height-bound assertion.
  - Strength: Keeps CollectionView virtualization; makes 5.3 meaningful. Concrete enough that the implementer can't guess wrong.
  - Tradeoff: Requires touching the outer sheet Grid, slightly widening the Phase 5 diff.
  - Confidence: HIGH — the Auto row is verified at `MapPage.xaml:225-237`.
  - Blind spot: Interaction with the dimming BoxView tap-to-dismiss row isn't verified under a star-sized sheet row.
- **Fix B**: Keep the ScrollView and render events with `BindableLayout`.
  - Strength: Zero nested-scroll risk; matches the pattern already used for the sport chips inside this same sheet.
  - Tradeoff: Loses virtualization; the plan's own 5.3 criterion and the "CollectionView owns scrolling" decision must be rewritten.
  - Confidence: MEDIUM — fine at MVP volume, but the plan already rejected pagination on the same "small dataset" reasoning, so this compounds an unmeasured assumption.
  - Blind spot: No measured event-per-venue ceiling anywhere in the plan.
- **Decision**: FIXED via Fix A — Phase 5 §2 Contract now specifies a star-sized sheet row, a `MaximumHeightRequest` on the sheet Border, and the `CollectionView` as the only `*` row inside; criterion 5.3 (and Progress row 5.3) now assert the height bound.

### F2 — Phase 3's "Automated Verification" items are not automatable

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3 — Success Criteria; Testing Strategy
- **Detail**: Phases 1, 2, 4 and 5 give exact runnable commands (`dotnet build`, `dotnet ef ...`, `rg ...`). Phase 3 items 3.2–3.8 are behavioural assertions — "Unauthenticated list and join requests return 401", "Sport and availability probes prove strict interval-overlap behavior, including boundary-touching non-overlap" — with no command, no tool, and no harness. The plan explicitly excludes a test project, and `server/server.http` (the repo's only probe artifact) contains just a health GET and is never referenced by the plan. Testing Strategy lists ~14 probe scenarios requiring two authenticated users with no stated mechanism. This is the largest verification surface in the change and the least specified. It also matters mechanically: these seven rows sit under `#### Automated` in Progress, and `/10x-goal-implement` confines itself to Automated rows.
- **Fix**: Extend `server/server.http` with named requests covering the Phase 3 probe matrix (two user tokens as variables, one request per scenario), reference that file in criteria 3.2–3.8, and move any probe needing human judgement into `#### Manual`.
  - Strength: Uses the artifact already in the repo; no new tooling or packages, consistent with the "no new test infrastructure" scope decision.
  - Tradeoff: `.http` files are still human-run — this buys repeatability and reviewability, not true automation.
  - Confidence: HIGH — `server/server.http` exists and is the established (if minimal) location.
  - Blind spot: Whether the token variables can be captured without a request-chaining feature isn't verified.
- **Decision**: FIXED (fixed differently) — `server/server.http` is left untouched to avoid pile-up. Instead, Testing Strategy → Integration Tests now carries an explicit 7-probe matrix (P-01…P-07) with setup steps, per-probe request and expectation, runnable verbatim from PowerShell or importable into Postman. Phase 3 criteria now reference P-01…P-07 by name, gained a route/auth static scan, and renumbered to 3.1–3.11 with a stated fallback: if the API can't be started locally, the same matrix is run in Postman and confirmed at the human gate.

### F3 — Accepted/Rejected card states are built but unreachable in this slice

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 4 §4, Phase 5 §2, Manual Testing Steps
- **Detail**: Desired End State and Phases 4–5 require `Joined` and `Request rejected` card states. But "What We're NOT Doing" gives all accept/reject to S-05, and Phase 3 §2 inserts `Pending` even when `AutoAccept` is true — so no S-04 code path can ever produce a non-Pending row. Manual criterion 5.7 asks to verify "pending, accepted, and rejected cards", yet Manual Testing Steps 1–10 contain no step that creates such a row. Two of the six card states ship unexercised.
- **Fix A ⭐ Recommended**: Add a manual verification step that sets `EventJoinRequests.Status` directly in Supabase to the Accepted and Rejected values and verifies card rendering.
  - Strength: Keeps the DTO/template forward-compatible so S-05 adds only the transition endpoint, not UI work. Cheap — the list DTO already carries the nullable status.
  - Tradeoff: Verification depends on hand-editing rows, which is easy to skip under time pressure.
  - Confidence: HIGH — status is a plain column on the new table.
  - Blind spot: None significant.
- **Fix B**: Render only Pending/none in S-04; defer Accepted/Rejected rendering to S-05.
  - Strength: Nothing ships unverified; strictly leaner slice.
  - Tradeoff: S-05 must reopen the card template and the client result mapping — work the plan deliberately tried to front-load.
  - Confidence: MEDIUM — depends on how much S-05 restructures the sheet.
  - Blind spot: S-05's UI shape isn't planned yet.
- **Decision**: FIXED via Fix A — Manual Testing Step 9 now seeds `EventJoinRequests.Status` to Accepted/Rejected in Supabase and verifies card rendering; criterion 5.7 (and Progress row 5.7) reference the seeded check explicitly.

### F4 — Delete semantics left as "restrictive or cascading" inside a migration

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §2 — EF Core configuration
- **Detail**: The Contract reads "restrictive or cascading relationships consistent with event/account deletion semantics" — an unresolved either/or the implementer must guess, baked into `AddEventJoinRequests`, the single most expensive artifact in this plan to change later. It also points at semantics that don't exist: there are no delete flows in the MVP, S-07 introduces cancel (not delete), and this plan excludes event status entirely. Every other Contract in the plan is decided; this one isn't.
- **Fix**: Decide it in the Contract — Cascade from `SportsEvent` (requests are owned children with no standalone meaning) and Restrict from `User` (protects the accepted-membership history S-05 depends on).
- **Decision**: FIXED — Phase 2 §2 Contract now states `DeleteBehavior.Cascade` on the `SportsEvent` relationship and `DeleteBehavior.Restrict` on the `User` relationship, each with its rationale inline.

### F5 — "(VenueId, SportId) foreign-key index" is FK-derived, not declared

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §2; Performance Considerations
- **Detail**: Plan says "retain the existing (VenueId, SportId) foreign-key index so PostgreSQL can combine it for sport-filtered queries". `server/Data/ChoNaBojoContext.cs` declares exactly one explicit index on `SportsEvents` — unique `(OrganizerUserId, ClientRequestId)` at :263-269. `(VenueId, SportId)` exists only as the auto-generated index behind the composite FK to `VenueSport` (:271-275). "Retain" implies an explicit object to preserve, and Postgres will normally choose one index rather than bitmap-combine two sharing the same leading column. Criterion 3.10 (`EXPLAIN`) already contains this risk.
- **Fix**: Reword to note the index is FK-derived, and state the fallback: if `EXPLAIN` shows a poor plan for the sport-filtered listing, add `(VenueId, SportId, EstimatedEndsAtUtc)` rather than relying on index combination.
- **Decision**: FIXED — both Phase 2 §2 and Performance Considerations now describe the index as auto-generated behind the `VenueSport` composite FK, drop the "combine" claim, and name the explicit `(VenueId, SportId, EstimatedEndsAtUtc)` fallback gated on criterion 3.11's `EXPLAIN`.

### F6 — The supersession pattern Phase 4 depends on exists, but not in MapViewModel

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Current State Analysis; Phase 4 §4
- **Detail**: Current State Analysis says "Existing client patterns provide cancellation-aware commands", and Phase 4 §4 requires generation-scoped cancellation with criterion 4.5 as a manual race gate. `app/ChoNaBojoApp/ViewModels/MapViewModel.cs` has no `CancellationTokenSource` at all — its cancellation is only the `[RelayCommand]`-supplied token (:173-181, :368). The real precedent is `app/ChoNaBojoApp/ViewModels/AddressSearchViewModel.cs:21-22` and `:115-165` (`_sessionCancellation` / `_suggestionSearchCancellation` with `CreateLinkedTokenSource`). Since a hand-verified race is the weakest kind of gate, pointing at the exact precedent materially lowers the risk.
- **Fix**: Cite `AddressSearchViewModel.cs:115-165` in Phase 4 §4 as the pattern to copy, and name the `MapViewModel` fields it introduces.
- **Decision**: FIXED — Current State Analysis now states that generation-scoped supersession lives in `AddressSearchViewModel`, not `MapViewModel`; Phase 4 §4 cites `AddressSearchViewModel.cs:21-22,115-165` as the pattern to copy and names the new `_venueEventsCancellation` and `_joinRequestCancellation` fields plus their cancel/dispose points.
