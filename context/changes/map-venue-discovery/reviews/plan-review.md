<!-- PLAN-REVIEW-REPORT -->
# Plan Review: S-02: Map Venue Discovery

- **Plan**: `context/changes/map-venue-discovery/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-25
- **Verdict**: REVISE
- **Findings**: 4 critical, 1 warning, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | FAIL |

## Grounding

Grounding: 8/8 paths ✓, 5/5 symbols ✓, brief↔plan ✓

Additional verification: Maps 10.0.100 restores beside Controls 10.0.71; the Windows handler exists and tolerates a missing Azure token; the custom Android mapper and `PinClicked` chain are viable. One rationale should be corrected: `VirtualView` is set before `CreatePlatformElement()`, not after it.

## Findings

### F1 — Pin collection cannot be rebuilt as specified

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Critical Implementation Details; Phase 3 §3
- **Detail**: The plan says to build pins and assign them to `Map.Pins` as one batch, and says .NET 10 has no bindable-items API. Actual MAUI 10 exposes get-only `IList<Pin> Pins`, so assignment cannot compile, and it also exposes `ItemsSource`/`ItemTemplate`. `Clear()` plus `Add()` is possible, but every collection mutation triggers a complete native marker rebuild, yielding O(n²) churn for 100 pins.
- **Fix A ⭐ Recommended**: Use `Map.ItemsSource` with an `ItemTemplate` producing `VenuePin` instances; make bound `VenuePin` fields bindable properties.
  - Strength: Uses the supported bindable API and avoids the impossible assignment contract.
  - Tradeoff: Adds `BindableProperty` definitions and needs a focused handler/template spike.
  - Confidence: MED — APIs are verified; their combination is not yet exercised in this repository.
  - Blind spot: Custom `VenuePin` handler behavior through `ItemTemplate`.
- **Fix B**: Use `Clear()` plus `Add()`, explicitly accept the repeated native rebuilds, and add a measured filter-performance gate.
  - Strength: Simplest verified public API and minimal design change.
  - Tradeoff: O(n²) native churn may violate the instant-filter/2s goal.
  - Confidence: HIGH — collection and handler behavior are verified.
  - Blind spot: Performance on target Android hardware is unmeasured.
- **Decision**: PENDING

### F2 — Venue DTO projection does not compile

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2 — Venue endpoints
- **Detail**: `VenueResponse` requires `IReadOnlyList<int>`, but the specified `v.VenueSports.Select(vs => vs.SportId)` is `IEnumerable<int>`. A matching EF Core 10 probe fails with CS1503; adding `ToList()` compiles and translates.
- **Fix**: Specify `v.VenueSports.Select(vs => vs.SportId).ToList()`.
- **Decision**: PENDING

### F3 — Release-only API-key restriction breaks debug maps

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §3; Migration Notes
- **Detail**: The setup instructions restrict the key to package `com.cho_na_bojo` plus the release signing certificate, but Phase 3 verification uses a Debug emulator build signed by the Android debug certificate. Google rejects that combination, producing the exact silent grey map the plan warns about.
- **Fix**: Require both debug and release package/SHA-1 restrictions (or separate dev/prod keys), and document how to obtain the debug SHA-1.
- **Decision**: PENDING

### F4 — Sport palette contract cannot use Sport.Code

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Phase 3 §1 — Sport→hue palette
- **Detail**: `HueFor` receives only sport IDs and `selectedSportId`, while the plan requires palette lookup by `Sport.Code`. The method has no way to obtain a code, so the implementer must invent an unstated mapping or violate the contract.
- **Fix**: Add an ID→code lookup parameter built once from cached `SportResponse` values, and specify unknown-code fallback behavior.
- **Decision**: PENDING

### F5 — Broad geocoding catch hides cancellation and defects

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 5 §2 — Manual-address fallback
- **Detail**: “Catch broadly” conflicts with the repository’s filtered exception pattern and would turn cancellation or programming defects into a misleading “Couldn’t find that address” snackbar.
- **Fix**: Catch documented geocoding failures explicitly, including `IOException` and unsupported/permission cases; let cancellation and unexpected defects propagate through the established command/error path.
- **Decision**: PENDING
