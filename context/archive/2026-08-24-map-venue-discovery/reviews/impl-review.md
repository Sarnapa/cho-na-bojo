<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-02 Map Venue Discovery

- **Plan**: `context/changes/map-venue-discovery/plan.md`
- **Scope**: Phases 1-5 of 5
- **Date**: 2026-09-01
- **Triage completed**: 2026-09-02
- **Verdict**: APPROVED
- **Findings**: 0 critical, 5 warnings, 2 observations - all fixed

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Verification

| Criterion | Result | Evidence |
|-----------|--------|----------|
| Whole solution build | PASS | `dotnet build solutions\ChoNaBojo.slnx`: 0 errors; existing NU1903 warning for `Microsoft.OpenApi` 2.0.0 |
| Android build | PASS | `dotnet build app\ChoNaBojoApp -f net10.0-android`: 0 warnings, 0 errors |
| Windows build | PASS | `dotnet build app\ChoNaBojoApp -f net10.0-windows10.0.19041.0`: 0 warnings, 0 errors |
| API startup | PASS | Server listened on `http://localhost:5100` without startup errors |
| Unauthenticated venue/sport API | PASS | Both endpoints returned 401 |
| Authenticated venue/sport API | PASS | Venues returned 200/100 items; sports returned 200/10 items |
| Coordinate projection | PASS | Venue 1 returned latitude 52.2394, longitude 21.0458 |
| Venue payload size | PASS | 34,889 bytes, below the 100 KB expectation |
| Secret and manifest checks | PASS | `secrets/maps.props` ignored and untracked; no tracked API-key-shaped literal; built manifest contains a nonempty substituted key |
| Removed Home types | PASS | No `HomePage` or `HomeViewModel` references under `app` |

## Findings

### F1 - Current-location marker can remain stale or visible after fallback

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM - real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:192`; `app/ChoNaBojoApp/Views/MapPage.xaml.cs:162`
- **Detail**: Explicit recentering accepts Android's last-known location without checking its age, then prefers the view model's prior location before attempting a fresh fix. When permission or location becomes unavailable, `UseWarsawFallback()` sets `IsShowingUser` false, but `ConfigureUserLocationLayer()` returns without disabling an already-enabled native location layer. The map can therefore recenter to an old position or continue displaying an old "current" marker.
- **Fix**: Accept cached locations only within a defined freshness window, otherwise request a fresh medium-accuracy fix with the existing eight-second limit; centralize native location-layer state so fallback and page disappearance explicitly set `MyLocationEnabled` to false.
  - Strength: Keeps the custom marker truthful while retaining a fast path for genuinely recent fixes.
  - Tradeoff: A stale or unavailable fix may make the user wait up to eight seconds.
  - Confidence: HIGH - the current null-coalescing order and early return make both stale-location paths explicit.
  - Blind spot: The most appropriate freshness threshold still needs product/device testing.
- **Decision**: FIXED - Cached locations are limited to a two-minute freshness window, and the native location layer is disabled on fallback, permission loss, and page disappearance.

### F2 - Obsolete address searches can block the latest query

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH - architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/Services/Geocoding/AddressSearchService.cs:28`
- **Detail**: One forward geocode and up to five sequential reverse geocodes run while holding the singleton semaphore. The MAUI geocoding calls have no cancellation overload, so canceling a debounced search cannot interrupt native I/O; a slow obsolete search retains the gate and delays the user's newest query.
- **Fix**: Use an Android-specific geocoder operation that returns labeled address results from one bounded forward lookup, keeping cancellation/debounce at the view-model boundary and enforcing an operation timeout.
  - Strength: Removes the five reverse lookups and prevents stale work from serially delaying current input.
  - Tradeoff: Introduces an Android-specific provider adapter instead of relying only on the MAUI abstraction.
  - Confidence: MEDIUM - the defect is explicit, but the exact .NET 10 Android geocoder callback/binding behavior needs emulator validation.
  - Blind spot: Provider-specific result quality and timeout behavior vary across emulator images and devices.
- **Decision**: FIXED - Android now uses one timeout-bounded native forward-geocoder lookup without the shared semaphore or sequential reverse lookups; Windows retains the MAUI fallback.

### F3 - Release workflow can publish an AAB with an empty Maps key

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `.github/workflows/android-deploy.yml:80`
- **Detail**: A missing `GOOGLE_MAPS_API_KEY` secret expands to an empty MSBuild property. The project deliberately permits an empty key for fresh-clone builds, so CI can still publish and upload a release whose map is blank.
- **Fix**: Map the secret to an environment variable, fail the workflow when it is empty, and pass the quoted variable to `GoogleMapsApiKey`.
- **Decision**: FIXED - The release job now rejects an empty GOOGLE_MAPS_API_KEY before publishing and passes the quoted environment variable to MSBuild.

### F4 - Required native-geocoder caveat is absent from code

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `app/ChoNaBojoApp/Services/Geocoding/AddressSearchService.cs:21`
- **Detail**: The plan explicitly requires code to record that `Geocoder.IsPresent` does not guarantee an individual lookup succeeds and that stale emulator providers may return `grpc failed`. The implementation handles provider failures but omits the required rationale.
- **Fix**: Add the concise provider caveat beside the `Geocoder.IsPresent` check and transport-failure handling.
- **Decision**: FIXED - The active Android geocoder now documents that provider presence does not guarantee lookup success and that stale emulators can surface grpc transport failures.

### F5 - Repeated current-location behavior has no Progress criterion

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/map-venue-discovery/plan.md:538`
- **Detail**: Phase 5 requires manually confirming that the current-location button works repeatedly after panning, but the Progress mirror jumps from checking the button's visibility to warning/error layering. No checkbox or commit evidence records this required behavior.
- **Fix**: Add the missing Phase 5 Manual Progress item without renaming existing steps, run the emulator check, and record its result and commit SHA.
- **Decision**: FIXED - Added Progress item 5.10a and recorded the user's successful repeated-current-location emulator check against abdab86.

### F6 - Completed emulator checks have no retained review evidence

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/map-venue-discovery/plan.md:491-545`
- **Detail**: Twenty-three emulator/fresh-clone manual checks are marked complete with implementation commit SHAs, but the reviewed diff contains no verification log or other observable evidence. The API payload check was independently reproduced; the device-only claims cannot be independently distinguished from unchecked assertions during this review.
- **Fix**: Retain a concise manual verification log identifying the emulator image, permission scenarios, and pass/fail outcome for each manual script step.
- **Decision**: FIXED - Added reviews/manual-verification.md with the user-reported emulator image and pass results for every manual script step plus the fresh-clone secret check.

### F7 - Initial map loading is not canceled when the page disappears

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🔎 MEDIUM - real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs:309`; `app/ChoNaBojoApp/Views/MapPage.xaml.cs:60`
- **Detail**: `EnsureLoadedAsync` receives `CancellationToken.None`, while `OnDisappearing` only unsubscribes page handlers. A root replacement or navigation during startup leaves location and API work running and allows the disconnected view model to finish mutating state.
- **Fix**: Give the page appearance a lifetime cancellation token, cancel it on disappearance, and pass it through catalog loading and location resolution while preserving explicit cancellation propagation.
  - Strength: Stops obsolete startup work and gives the existing cancellation-aware service contracts a real caller token.
  - Tradeoff: Requires careful command-state cleanup so a later appearance can retry normally.
  - Confidence: HIGH - the current call explicitly uses `CancellationToken.None` and has no other cancellation path.
  - Blind spot: Root navigation during startup was not reproduced on an emulator in this review.
- **Decision**: FIXED - Appearance and retry commands now cancel on page disappearance and propagate their tokens through location and catalog loading; caller cancellation is preserved at the venue API boundary.
