# S-02: Map Venue Discovery Implementation Plan

## Overview

Deliver the app's core discovery surface: a logged-in user lands on a Google Map centered on their current location (Warsaw fallback when location is denied), sees all seeded Warsaw sports venues as sport-colored pins, taps a pin to open a venue bottom sheet, and can narrow the map to a single discipline via a chip row. Two new authorized read endpoints back the screen.

This is the first slice to introduce a third-party map dependency, an external cloud account (Google Cloud + Maps API key), Android runtime permissions, and platform-specific handler code.

## Current State Analysis

**Server** — `server/Program.cs:123` already declares the protected seam and never uses it:

```csharp
var apiGroup = app.MapGroup("/api").RequireAuthorization();
```

This slice is what consumes it. Endpoints follow the `MapXEndpoints(this IEndpointRouteBuilder)` extension-method pattern established by `server/Auth/AuthEndpoints.cs:15`, with `Results.ValidationProblem` for validation failures (`AuthEndpoints.cs:187`).

**Data** — F-01 already shipped everything this slice reads:

- `Venue` (`server/Data/Entities/Venue.cs`) — `Id` (stable CSV identity, `ValueGeneratedNever`), `Name`, `Location` as PostGIS `geometry(Point,4326)` via NetTopologySuite, `Address`, `Description`.
- A **GiST index on `Venue.Location`** (`server/Data/ChoNaBojoContext.cs:100`) carrying the comment *"powers the S-02 map-viewport bbox queries"* — built in anticipation of viewport querying this plan deliberately defers (see "What We're NOT Doing").
- `Sport` seeded with 10 rows (`ChoNaBojoContext.cs:73-84`), id-keyed with stable `Code` + Polish `Name`.
- `VenueSport` join, composite PK `(VenueId, SportId)`.
- `data/warsaw-venues.csv` — **100 venues**, all Warsaw. 41 single-sport, 59 multi-sport (max 5).

**App** — S-01 established the conventions this slice extends:

- MVVM via `CommunityToolkit.Mvvm`; `ViewModels/ViewModelBase.cs` supplies `IsBusy`/`IsNotBusy`; commands via `[RelayCommand]`.
- `Services/ApiService.cs` returns **typed result objects**, never raw HTTP; JWT attached transparently by `Services/Auth/AuthenticatingHttpMessageHandler.cs` on the named `"ChoNaBojoApi"` client.
- `AppShell.xaml` has exactly one `ShellContent` → `HomePage`, a placeholder whose body reads *"The map and events are coming soon."* and which hosts the **Log out** button.
- `Resources/Styles/Styles.xaml` defines `HeadlineStyle`, `TitleStyle`, `BodyStyle`, `LabelStyle`, `CardShadow`, `PrimaryButtonStyle`, `SecondaryButtonStyle`, `DestructiveButtonStyle`, `TextFieldStyle`, `SelectFieldStyle`. `Colors.xaml` defines the §2 tokens plus matching `*Brush` keys.
- `Platforms/Android/AndroidManifest.xml` declares **only** `INTERNET` and `ACCESS_NETWORK_STATE` — no location permissions, no Maps metadata.
- `secrets/` is already gitignored (`.gitignore:369`) and already hosts the release keystore.
- The csproj also builds `net10.0-windows10.0.19041.0` on Windows, and `.github/workflows/windows-deploy.yml` publishes an MSIX on `v*.*.*` tags. **The Windows head must keep compiling.**

**Absent** — no map package, no geolocation use, no test projects anywhere in the repo (F-01/F-02/S-01 were all verified manually).

## Desired End State

A logged-in user opens the app and lands directly on the map. It centers on their location if permission is granted, otherwise on Warsaw with a dismissible banner and an address box that recenters the map on submit. All 100 venues render as pins — colored by sport for single-sport venues, red orange for multi-sport ones. Tapping a pin opens a bottom sheet with the venue's name, address, description and supported sports, plus a disabled "Create event" button marking where S-03 will land. A chip row pinned above the map filters to one discipline at a time, hiding non-matching venues and recoloring the rest; "All" is selected by default. Venue and sport data is fetched once per app session and every subsequent filter or pan is instant.

Verified by: launching the app on an Android emulator (Google APIs image) and walking the manual script in "Testing Strategy".

### Key Discoveries

- **`Pin.Type` is a no-op on Android.** `MapPinHandler.Mapper` (dotnet/maui `src/Core/maps/src/Handlers/MapPin/MapPinHandler.cs`) never maps `PinType`, and `MapPinHandler.Android.cs` returns a bare `MarkerOptions` with no `SetIcon`. There is **no cross-platform way to color a marker in .NET 10**; `Pin.ImageSource` arrives in .NET 11.
- **Colored markers need only ~20 lines of Android code.** Subclass `Pin` to carry a hue, then extend `MapPinHandler.Mapper` with one extra entry calling `BitmapDescriptorFactory.DefaultMarker(hue)`. This must be a *mapper entry*, not a `CreatePlatformElement` override — `CreatePlatformElement()` runs before `VirtualView` is populated, so the hue isn't readable there.
- **`PinClicked` survives the custom pin handler.** Click routing lives in `MapHandler.Android.cs` (`map.MarkerClick += OnMarkerClick`, matching on `pin.MarkerId` assigned after `Map.AddMarker`), not in `MapPinHandler`. The custom handler only alters `MarkerOptions` before `AddMarker`, so the chain stays intact.
- **The Android `VisibleRegion` bug (dotnet/maui#21094) does not affect us.** It only bites when a custom `MapHandler` hooks `CameraMove`. Because we load all venues once and never query by viewport, we never touch camera events.
- **Windows is supported in .NET 10 and does not break.** `Microsoft.Maui.Controls.Maps` 10.0.100 ships a real `MapHandler.Windows.cs` backed by Azure Maps (the NuGet README claiming otherwise is stale, and `aka.ms/maui-maps-no-windows` now redirects). Without an Azure Maps token the map renders blank — **no crash, no build failure**. So the `PackageReference` and `UseMauiMaps()` stay unconditional; only the pin handler needs `#if ANDROID`.
- **Conditioning the `PackageReference` on TFM would break the build.** The MAUI XAML compiler resolves types at build time, so `<maps:Map>` in XAML fails the Windows TFM compile with `MAUI003` if the assembly isn't referenced there. Keep it unconditional.
- **Built-in geocoding is silently empty on bare emulators.** `Geocoding.Default.GetLocationsAsync` delegates to `android.location.Geocoder`, which needs Google Play Services. On an AOSP emulator image it returns an empty sequence — indistinguishable from "address not found". Testing the fallback *requires* a **Google APIs** emulator image.
- **`Permissions.RequestAsync` must run on the main thread**; `GetLocationAsync` can hang indefinitely without an explicit timeout.

## What We're NOT Doing

- **No viewport-bounded venue queries.** The GiST index stays unused for now. Deferred deliberately — see "Scale caveat" below.
- **No sport-glyph pin icons.** ui-guidelines §7 asks for the sport icon on a `PrimaryColor` pin; we ship colored default markers instead and defer glyphs to .NET 11's `Pin.ImageSource`.
- **No events anywhere.** No event listing, creation, or join — the bottom sheet's "Create event" button is inert. That is S-03/S-04.
- **No venue search by name, no clustering, no offline/disk cache, no pull-to-refresh.**
- **No automated tests, no test project.** Consistent with every prior slice.
- **No iOS.** No Azure Maps token for the Windows head (blank map there is accepted).
- **No changes to auth, registration, or the refresh/session machinery** beyond relocating the Log out button.

### Scale caveat (must be recorded in code)

Loading all venues in one request is correct **only** while the dataset is one city / ~100 rows. Both the server endpoint and the client cache must carry an explicit comment stating that a viewport-bounded (bbox) query is the intended evolution, that the GiST index on `Venue.Location` already exists to serve it, and that switching to it will require a map control with a reliable camera-idle event (the official control's Android `VisibleRegion` behaviour is broken per dotnet/maui#21094).

## Implementation Approach

Bottom-up, mirroring F-01/F-02/S-01: server contract first, then app plumbing with no visible UI, then the screen, then interaction, then filtering. Each phase ends in a state that builds and runs.

The one ordering constraint that matters: **Phase 2 must land the Google Maps API key before Phase 3**, because without a valid key the map renders as a blank grey grid and every visual check in Phase 3 becomes meaningless.

## Critical Implementation Details

**Timing & lifecycle.** `Permissions.RequestAsync<Permissions.LocationWhenInUse>()` must be invoked on the main thread. Resolve location with `GetLastKnownLocationAsync()` first (instant, cached) and only fall back to `GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)))` — without an explicit timeout this call can hang indefinitely indoors, and `GeolocationAccuracy.High` can block 20–60s. Venue fetch and location resolution are independent and should run concurrently so the 2s NFR isn't the sum of both.

**Pin hue mapping must run after `VirtualView` is set.** Assign the hue through an added `PropertyMapper` entry on a `MapPinHandler` subclass, never by overriding `CreatePlatformElement()` — see Key Discoveries.

**Marker rebuild on filter change.** Bind `Map.ItemsSource` to a replacement `VisiblePins` list and render each item through an `ItemTemplate` that creates a `VenuePin`. Build the complete list first, then replace the property once per filter change so MAUI performs one native marker rebuild rather than rebuilding after every `Map.Pins.Add()`.

## Phase 1: Server — venue & sport read API

### Overview

Expose the seeded reference data over the existing authorized `/api` group as two resource endpoints, with DTOs in the shared Contracts project.

### Changes Required:

#### 1. Shared contracts

**File**: `shared/ChoNaBojo.Contracts/DTOs/VenueDTOs.cs` (new)

**Intent**: Define the wire shape for venues and sports so both server and app compile against one definition, per `lessons.md` ("Keep shared app/server code in three dependency-free projects").

**Contract**: Two sealed records mirroring the `AuthDTOs.cs` style. `VenueResponse(int Id, string Name, string Address, string Description, double Latitude, double Longitude, IReadOnlyList<int> SportIds)` and `SportResponse(int Id, string Code, string Name)`. `Latitude`/`Longitude` are plain doubles — **NetTopologySuite types must not leak into Contracts**, which may not reference EF Core or Npgsql.

#### 2. Venue endpoints

**File**: `server/Venues/VenueEndpoints.cs` (new)

**Intent**: Serve the full venue list and the sport lookup as read-only, authorized endpoints.

**Contract**: `public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder endpoints)` following `AuthEndpoints.cs:15`. Maps `GET /venues` (`WithName("VenuesList")`) and `GET /sports` (`WithName("SportsList")`) onto the passed builder — authorization is **inherited from the `/api` group**, so no per-endpoint `RequireAuthorization()` is needed. Both handlers take `ChoNaBojoContext` and `CancellationToken`, use `AsNoTracking()`, and project directly into the DTOs. Venue projection reads `v.Location.Y` as latitude and `v.Location.X` as longitude (NetTopologySuite `Point` is X=longitude, Y=latitude — getting this backwards puts every Warsaw venue in Somalia) and includes `v.VenueSports.Select(vs => vs.SportId).ToList()` so the value matches `VenueResponse`'s `IReadOnlyList<int>` contract. Sports are ordered by `Id`, venues by `Name`.

This file carries the scale caveat comment described in "What We're NOT Doing".

#### 3. Wire into the pipeline

**File**: `server/Program.cs`

**Intent**: Consume the currently-unused `apiGroup` variable.

**Contract**: Replace the dangling `var apiGroup = app.MapGroup("/api").RequireAuthorization();` at line 123 with the same group expression followed by `apiGroup.MapVenueEndpoints();`, and drop the now-stale "future feature endpoints (S-03+)" comment.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- API starts without error: `dotnet run --project server`
- `GET /api/venues` without a bearer token returns **401** (privacy guardrail)
- `GET /api/sports` without a bearer token returns **401**
- `GET /api/venues` with a valid token returns **200** and exactly **100** items
- `GET /api/sports` with a valid token returns **200** and exactly **10** items
- A known venue's coordinates land in Warsaw (id 1 ≈ lat 52.2394, lon 21.0458) — confirms the X/Y projection isn't transposed

#### Manual Verification:

- Venue payload is small enough for a single request (inspect `Content-Length`; expect well under 100 KB)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: App plumbing — Maps package, API key, permissions, typed client

### Overview

Add the map dependency, get a Google Maps API key into the manifest without committing it, declare location permissions, and extend the API client with a session-scoped venue/sport cache. No visible UI changes.

### Changes Required:

#### 1. Map package and handler registration

**File**: `app/ChoNaBojoApp/ChoNaBojoApp.csproj`

**Intent**: Reference the official map control for **all** target frameworks and import the untracked local key file.

**Contract**: Add `<PackageReference Include="Microsoft.Maui.Controls.Maps" Version="10.0.100" />` — **unconditional**; conditioning it on the Android TFM breaks the Windows XAML compile (`MAUI003`). Add an `AndroidManifestPlaceholders` property for the Android TFM feeding `GOOGLE_MAPS_KEY=$(GoogleMapsApiKey)`, and a conditional import of the gitignored key file:

```xml
<Import Project="$(MSBuildThisFileDirectory)..\..\secrets\maps.props"
        Condition="Exists('$(MSBuildThisFileDirectory)..\..\secrets\maps.props')" />
```

`secrets/` is already gitignored, so no `.gitignore` change is needed. CI overrides the value with `-p:GoogleMapsApiKey=...`.

**File**: `app/ChoNaBojoApp/MauiProgram.cs`

**Contract**: Chain `.UseMauiMaps()` onto the existing builder chain (unconditional — the Windows handler exists and no-ops safely), and register the Android pin handler inside `ConfigureMauiHandlers` guarded by `#if ANDROID`. Register `MapPage`/`MapViewModel` as transient alongside the existing page registrations, and the venue catalog service (below) as a singleton.

#### 2. Android manifest

**File**: `app/ChoNaBojoApp/Platforms/Android/AndroidManifest.xml`

**Intent**: Supply the Maps API key and request location access.

**Contract**: Inside `<application>`, add `<meta-data android:name="com.google.android.geo.API_KEY" android:value="${GOOGLE_MAPS_KEY}" />` and `<meta-data android:name="com.google.android.gms.version" android:value="@integer/google_play_services_version" />`. At manifest level add `ACCESS_COARSE_LOCATION` and `ACCESS_FINE_LOCATION` permissions, plus `<uses-feature android:name="android.hardware.location" android:required="false" />` so the app stays installable on devices without GPS.

#### 3. Local key file and documentation

**File**: `secrets/maps.props` (new, untracked)

**Contract**: A minimal MSBuild file setting `<GoogleMapsApiKey>` in a `PropertyGroup`. **Never committed.** The plan's implementer creates it locally with a development key restricted to package `com.cho_na_bojo` and the Android debug certificate SHA-1. `AGENTS.md` gains a short "Google Maps API key" note describing how to enable Maps SDK for Android, create the restricted development key, place it in `secrets/maps.props`, and obtain the debug SHA-1 with `keytool -list -v -alias androiddebugkey -keystore "$env:USERPROFILE\.android\debug.keystore" -storepass android -keypass android`. It separately documents that CI uses a production key restricted to the same package and the release signing certificate SHA-1.

#### 4. CI wiring

**File**: `.github/workflows/android-deploy.yml`

**Intent**: Supply the key in CI so release AABs get a working map.

**Contract**: Add `-p:GoogleMapsApiKey=${{ secrets.GOOGLE_MAPS_API_KEY }}` to the existing `dotnet publish` invocation in the "Build & sign AAB" step. A new `GOOGLE_MAPS_API_KEY` repository secret must exist.

#### 5. API client + session cache

**File**: `shared/ChoNaBojo.Contracts/DTOs/VenueDTOs.cs` — already added in Phase 1.

**File**: `app/ChoNaBojoApp/Services/IApiService.cs`, `app/ChoNaBojoApp/Services/ApiService.cs`

**Intent**: Fetch venues and sports through the authenticated client using the established typed-result convention.

**Contract**: Add `Task<VenueCatalogResult> GetVenuesAsync(CancellationToken)` and `Task<SportCatalogResult> GetSportsAsync(CancellationToken)`. Result types live in `app/ChoNaBojoApp/Services/Venues/VenueResults.cs` and mirror `CurrentUserResult`'s shape — a status enum covering `Success`, `Unauthorized`, `Network`, `Unknown`, with the same `HttpRequestException`/`TaskCanceledException`/`JsonException` handling as `ApiService.GetCurrentUserAsync`.

**File**: `app/ChoNaBojoApp/Services/Venues/IVenueCatalog.cs`, `VenueCatalog.cs` (new)

**Intent**: Hold venues and sports in memory for the app session so filtering and re-entry cost nothing.

**Contract**: Singleton exposing `Task<VenueCatalogLoadResult> EnsureLoadedAsync(CancellationToken)` plus `IReadOnlyList<VenueResponse> Venues` and `IReadOnlyList<SportResponse> Sports`. Fetches both endpoints **concurrently**, caches on success, and is idempotent — concurrent callers must not trigger duplicate fetches (guard with a `SemaphoreSlim`, mirroring the single-flight discipline already used in `AuthenticatingHttpMessageHandler`). A failed load leaves the cache empty so a retry re-fetches. Carries the scale caveat comment.

### Success Criteria:

#### Automated Verification:

- Android head builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Windows head still builds: `dotnet build app/ChoNaBojoApp -f net10.0-windows10.0.19041.0`
- `git status` shows **no** `secrets/maps.props` and no API key anywhere in tracked files
- The built manifest contains the substituted key, not the literal `${GOOGLE_MAPS_KEY}` (inspect `obj/Debug/net10.0-android/AndroidManifest.xml`)

#### Manual Verification:

- App still launches on the emulator and the existing login → Home flow is unchanged
- A build with `secrets/maps.props` absent still compiles (proves a fresh clone isn't blocked from building)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Map screen with venue pins

### Overview

Replace the Home placeholder with the map, center it on the user (Warsaw fallback), and render all venues as sport-colored pins.

### Changes Required:

#### 1. Sport→hue palette

**File**: `app/ChoNaBojoApp/Views/Maps/SportPinPalette.cs` (new)

**Intent**: Map a venue to a marker hue. Presentation-only, so it stays in the app — not in `Contracts` (per `lessons.md`, shared projects carry cross-boundary contracts, not client rendering choices).

**Contract**: `static float HueFor(IReadOnlyList<int> sportIds, int? selectedSportId, IReadOnlyDictionary<int, string> sportCodesById)`. Build `sportCodesById` once in `MapViewModel` from the cached `SportResponse` values. When a sport filter is active, resolve its ID through the lookup and return that code's hue. When a venue supports exactly one sport, resolve that ID and return its code's hue. When it supports several, return the multi-sport hue. A missing ID or unknown code also returns the multi-sport/red-orange hue rather than throwing. The palette is keyed on stable `Sport.Code` values, never Polish display names. Proposed values, tunable:

| Sport | Hue | Sport | Hue |
|---|---|---|---|
| `football` | 120 | `cycling` | 300 |
| `basketball` | 35 | `rollerblading` | 330 |
| `volleyball` | 60 | `gym` | 270 |
| `tennis` | 75 | `street_workout` | 240 |
| `running` | 150 | `swimming` | 210 |
| *multi-sport* | 15 | | |

This file carries the deferred-work note: from .NET 11, replace hue encoding with `Pin.ImageSource` and a sport-classification–driven glyph icon per ui-guidelines §7.

#### 2. Custom pin

**File**: `app/ChoNaBojoApp/Views/Maps/VenuePin.cs` (new)

**Contract**: `public sealed class VenuePin : Pin` adding bindable `float Hue` and `int VenueId` properties backed by `BindableProperty` definitions because the map `ItemTemplate` binds both values. `VenueId` lets the tap handler resolve the venue without matching on `Label`.

**File**: `app/ChoNaBojoApp/Platforms/Android/VenuePinHandler.cs` (new)

**Contract**: `public sealed class VenuePinHandler : MapPinHandler` declaring `public static new IPropertyMapper<IMapPin, IMapPinHandler> Mapper` built from `MapPinHandler.Mapper` plus one entry `[nameof(VenuePin.Hue)] = MapHue`, where `MapHue` calls `handler.PlatformView.SetIcon(BitmapDescriptorFactory.DefaultMarker(hue))`. Constructor passes the extended mapper to `base`. **Do not override `CreatePlatformElement()`** — `VirtualView` isn't populated at that point.

#### 3. Map page and view model

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml` + `.xaml.cs` (new)

**Intent**: Host the map, its chrome, and the three screen states from ui-guidelines §10.

**Contract**: `ContentPage` with `x:DataType="viewModels:MapViewModel"`, `xmlns:maps="http://schemas.microsoft.com/dotnet/2021/maui/maps"`. Root `Grid` layering, bottom to top: the `maps:Map` (named, `IsShowingUser` bound), the chip row placeholder (Phase 5), the address box and banner placeholders (Phase 5), the bottom sheet (Phase 4), and centered loading/error overlays bound to the view model. Bind `Map.ItemsSource` to `VisiblePins`; its typed `ItemTemplate` creates `VenuePin` instances and binds `VenueId`, `Hue`, `Location`, `Label`, and `Address`. A **Log out** `ToolbarItem` replaces `HomePage`'s button, invoking the same confirm-then-`SignOutAsync` flow and root swap that `HomePage.xaml.cs` performs today — including awaiting `LogoutCommand.ExecutionTask` before `SetAuthRoot()` to avoid the null `PlatformView` noted in that file.

The view model exposes `IReadOnlyList<VenuePinViewData> VisiblePins`, where the internal presentation record carries `VenueId`, `Hue`, `Location`, `Label`, and `Address`. Build a complete replacement list from cached venues and raise one property change whenever the visible set or coloring changes; do not mutate `Map.Pins` from code-behind. Every interactive element sets `SemanticProperties.Description`; all sizing uses the 8pt grid and `{StaticResource}` tokens only.

**File**: `app/ChoNaBojoApp/ViewModels/MapViewModel.cs` (new)

**Contract**: Extends `ViewModelBase`. `[RelayCommand] AppearingAsync` resolves location and loads the catalog **concurrently**, then builds one ID→code lookup from the cached sports and exposes `VisibleVenues`. Observable state: `IsLoading`, `HasError`, `ErrorMessage`, `InitialCenter`, `LocationDenied`, `SelectedSportId`. Location flow: check then request `Permissions.LocationWhenInUse` on the main thread; on `Granted`, `GetLastKnownLocationAsync()` and fall back to `GetLocationAsync(Medium, 8s)`; on denial set `LocationDenied` and center on Warsaw (52.2297, 21.0122) at roughly a 5 km radius. Catalog failures surface the §10 error state with a `Retry` command; they never crash and never leave a blank map with no explanation.

#### 4. Navigation swap

**File**: `app/ChoNaBojoApp/AppShell.xaml`, `app/ChoNaBojoApp/MauiProgram.cs`

**Contract**: Point the single `ShellContent` at `MapPage` with `Route="MapPage"`. `HomePage`, `HomeViewModel` and their DI registrations are deleted — `MapViewModel` absorbs the logout behavior. The root-swap contract in `NavigationRootService` is unchanged.

### Success Criteria:

#### Automated Verification:

- Android head builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Windows head builds: `dotnet build app/ChoNaBojoApp -f net10.0-windows10.0.19041.0`
- No references to `HomePage`/`HomeViewModel` remain: `rg "HomePage|HomeViewModel" app/` returns nothing

#### Manual Verification:

- Granting the location prompt centers the map on the emulator's mock location
- Denying the prompt centers the map on Warsaw without crashing
- All 100 venues render as pins; single-sport venues show distinct colors and multi-sport venues show red orange
- Panning and zooming stay responsive (NFR: under 2 seconds)
- Log out from the map toolbar returns to Login and clears the back stack
- Killing and relaunching the app returns to the map still logged in (S-01 session persistence intact)
- With the API stopped, the error state and Retry appear instead of a blank map

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 4: Venue bottom sheet

### Overview

Tapping a pin opens the ui-guidelines §7 bottom sheet with venue details and the inert S-03 affordance.

### Changes Required:

#### 1. Sheet UI

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml`

**Intent**: Render the venue sheet as an overlay — MAUI has no native bottom sheet, and §7 specifies the visual contract precisely.

**Contract**: A `Border` pinned to the bottom of the root `Grid`, `SurfaceColor` background, top corners `16`, `Shadow="{StaticResource CardShadow}"`, padding `16`, visibility bound to `IsVenueSheetVisible`. Contents: venue name in `TitleStyle`, address in `LabelStyle`, description in `BodyStyle`, a read-only horizontal row of supported-sport chips reusing the §6E chip visual, then the §10 empty state ("No events here yet") and a **disabled** "Create event" `Button` (`PrimaryButtonStyle`) with `32` top spacing and a "coming soon" hint. Dismissible by tapping outside or a close affordance.

#### 2. Tap wiring

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml.cs`, `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`

**Contract**: Subscribe to `Map.PinClicked`; resolve `VenuePin.VenueId` to the cached `VenueResponse` and pass it to the view model, which exposes `SelectedVenue` plus its resolved sport names (joining `SportIds` against the cached sports). Set `e.HideInfoWindow = true` so Google's default info window doesn't compete with the sheet.

### Success Criteria:

#### Automated Verification:

- Android head builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Windows head builds: `dotnet build app/ChoNaBojoApp -f net10.0-windows10.0.19041.0`

#### Manual Verification:

- Tapping a pin opens the sheet with the correct name, address, description and supported sports
- Tapping a different pin swaps the sheet content without closing and reopening
- The sheet dismisses cleanly and the map stays interactive underneath
- The "Create event" button is visibly disabled and does nothing
- A multi-sport venue lists all its disciplines; a single-sport venue lists exactly one
- The sheet honors the 8pt grid and uses only design tokens (no hardcoded colors or spacing)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 5: Sport filter and manual-address fallback

### Overview

Add the single-select chip row and the location-denied address fallback — the two remaining FR-003 / FR-004 obligations.

### Changes Required:

#### 1. Sport filter chip row

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml`, `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`

**Intent**: Let the user narrow the map to one discipline, per FR-003 and ui-guidelines §6E.

**Contract**: A horizontally scrolling `CollectionView` pinned above the map, bound to a chip collection built from the cached sports with a leading **"All"** chip selected by default. Chip visuals per §6E: height `48`, `CornerRadius="24"`, `8` spacing, sport Material Symbol icon left of the name; unselected = `SurfaceColor` background + `DividerColor` border + `OnSurfaceColor` text, selected = `PrimaryColor` background + `OnPrimaryColor` text. Selection is **single-select** — choosing a sport clears any other, and "All" clears the filter.

`SelectedSportId` drives `VisibleVenues`, which filters to venues whose `SportIds` contain it; non-matching venues are removed from the map entirely. Build a complete replacement `VisiblePins` list and recolor each item via `SportPinPalette.HueFor(sportIds, selectedSportId, sportCodesById)` so every visible pin takes the selected sport's color; assigning the list once lets `ItemsSource` trigger one marker rebuild. Filtering is pure in-memory over the cached list — **no network call on filter change**. An active filter that matches nothing shows the §10 empty state.

#### 2. Manual-address fallback

**File**: `app/ChoNaBojoApp/Views/MapPage.xaml`, `app/ChoNaBojoApp/ViewModels/MapViewModel.cs`

**Intent**: Satisfy FR-004's fallback for users who deny location permission.

**Contract**: A `material:TextField` (`TextFieldStyle`) at the top of the screen plus a dismissible info banner, both visible only when `LocationDenied` is true. Banner styling follows ui-guidelines §9's in-app banner: `SurfaceColor` with `CardShadow`. A `SearchAddressCommand` — triggered by an explicit submit, never per keystroke — calls `Geocoding.Default.GetLocationsAsync(address)` and recenters the map on the first result via `MoveToRegion`. Empty results and documented geocoding failures (`IOException`, `FeatureNotSupportedException`, and `PermissionException`) surface a "Couldn't find that address" snackbar through the existing `IFeedbackService`. Do not catch cancellation or unexpected exceptions here; let them propagate through the established command/error path rather than presenting a misleading address-not-found message.

Record in code that empty results are **expected on emulators without Google Play Services**, so this path must be tested on a Google APIs image.

### Success Criteria:

#### Automated Verification:

- Android head builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- Windows head builds: `dotnet build app/ChoNaBojoApp -f net10.0-windows10.0.19041.0`
- Whole solution builds: `dotnet build solutions/ChoNaBojo.slnx`

#### Manual Verification:

- "All" is selected on first open and every venue is visible
- Selecting a sport hides non-matching venues and recolors the remainder to that sport's color
- Re-selecting "All" restores every venue and its base coloring
- Filter changes are instant and issue no network request
- With location denied, the address field and banner appear; submitting a Warsaw address recenters the map
- An unresolvable address shows the "couldn't find" snackbar rather than failing silently
- With location granted, the address field and banner are absent
- The banner can be dismissed and does not obstruct the chip row or map

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation. This is the final phase — confirm the full Testing Strategy script passes before closing the change.

---

## Testing Strategy

No test projects exist in this repo and none are added here (consistent with F-01/F-02/S-01). Verification is manual, with the API-level checks scripted via `curl`.

### API checks (Phase 1)

1. Start the API: `dotnet run --project server`
2. `curl -i http://localhost:5100/api/venues` → expect **401**
3. `curl -i http://localhost:5100/api/sports` → expect **401**
4. Register/login to obtain a bearer token, then re-issue both with `Authorization: Bearer <token>` → expect **200**, 100 venues and 10 sports
5. Confirm venue id 1 reports lat ≈ 52.2394 / lon ≈ 21.0458 (guards against transposed X/Y)

### Manual emulator script

**Emulator requirement: use a system image with Google APIs.** A bare AOSP image has no Google Play Services, so Google Maps tiles won't render and `Geocoding` returns empty — both would look like implementation bugs.

1. **First run, permission granted** — launch, log in, accept the location prompt. Map centers on the emulator's mock location; pins render.
2. **Pin colors** — confirm single-sport venues show distinct colors and multi-sport venues show red orange.
3. **Venue sheet** — tap a pin; verify name, address, description, sports. Tap another pin; verify the sheet swaps. Dismiss it.
4. **Filter** — select a sport; confirm non-matching venues vanish and the rest recolor. Confirm no network call. Return to "All".
5. **Performance** — pan and zoom across Warsaw; confirm responsiveness within the 2s NFR.
6. **Session** — kill and relaunch; confirm you land back on the map, still logged in.
7. **Permission denied** — revoke location in emulator settings, clear app data, relaunch and deny. Confirm Warsaw centering, banner, and address field. Submit "Pole Mokotowskie, Warszawa" and confirm recentering. Submit gibberish and confirm the snackbar.
8. **Offline** — disable emulator networking and cold-start; confirm the error state with Retry, then re-enable and confirm Retry recovers.
9. **Logout** — log out from the toolbar; confirm return to Login with no map content in the back stack.

## Performance Considerations

The 2-second map NFR is met structurally rather than through optimization: all venue data is fetched once per session, so pan, zoom, and filter operations are pure local work. Location resolution runs concurrently with the venue fetch so startup latency is the max of the two, not the sum, and `GeolocationAccuracy.Medium` with an 8-second cap prevents a GPS fix from dominating. Each filter change builds a complete `VisiblePins` replacement list before assigning it to `Map.ItemsSource`, avoiding the O(n²) native churn caused by repeated `Map.Pins.Add()` calls. At 100 pins no clustering is warranted, but clustering becomes the first thing to add if the venue set grows substantially.

## Migration Notes

No database migration — this slice only reads F-01's seeded tables. Two operational prerequisites are **not** code and must be done by hand:

1. A Google Cloud project with billing enabled and the **Maps SDK for Android** enabled.
2. A development API key restricted to package `com.cho_na_bojo` plus the Android debug certificate SHA-1, stored only in local `secrets/maps.props`.
3. A production API key restricted to package `com.cho_na_bojo` plus the release signing certificate SHA-1, stored as the `GOOGLE_MAPS_API_KEY` GitHub repository secret for the Android release workflow.

`HomePage`/`HomeViewModel` are deleted rather than deprecated; nothing outside `AppShell.xaml` and `MauiProgram.cs` references them.

## References

- Roadmap slice S-02: `context/foundation/roadmap.md` (lines 107–118)
- PRD FR-003, FR-004, Business Logic, Access Control: `context/foundation/prd.md`
- UI contract for map, sheet, chips, states: `context/foundation/ui-guidelines.md` §6E, §7, §9, §10, §11
- Shared-project placement rule: `context/foundation/lessons.md`
- Endpoint pattern to mirror: `server/Auth/AuthEndpoints.cs:15`
- Protected group seam: `server/Program.cs:123`
- Spatial column + GiST index: `server/Data/ChoNaBojoContext.cs:87-101`
- Typed-result client convention: `app/ChoNaBojoApp/Services/ApiService.cs`
- Prior slice for MVVM/session conventions: `context/archive/2026-07-18-account-and-session/plan.md`
- MAUI Map control docs: https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/map?view=net-maui-10.0
- Android `VisibleRegion`/camera bug: https://github.com/dotnet/maui/issues/21094

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Server — venue & sport read API

#### Automated

- [x] 1.1 Solution builds — ccab409
- [x] 1.2 API starts without error — ccab409
- [x] 1.3 GET /api/venues unauthenticated returns 401 — ccab409
- [x] 1.4 GET /api/sports unauthenticated returns 401 — ccab409
- [x] 1.5 GET /api/venues authenticated returns 200 with 100 items — ccab409
- [x] 1.6 GET /api/sports authenticated returns 200 with 10 items — ccab409
- [x] 1.7 Venue id 1 coordinates land in Warsaw (X/Y not transposed) — ccab409

#### Manual

- [x] 1.8 Venue payload small enough for a single request — ccab409

### Phase 2: App plumbing — Maps package, API key, permissions, typed client

#### Automated

- [x] 2.1 Android head builds — c5c6f75
- [x] 2.2 Windows head builds — c5c6f75
- [x] 2.3 No API key or secrets/maps.props in tracked files — c5c6f75
- [x] 2.4 Built manifest contains the substituted key — c5c6f75

#### Manual

- [x] 2.5 App launches and existing login flow is unchanged — c5c6f75
- [x] 2.6 Build succeeds with secrets/maps.props absent — c5c6f75

### Phase 3: Map screen with venue pins

#### Automated

- [x] 3.1 Android head builds
- [x] 3.2 Windows head builds
- [x] 3.3 No HomePage/HomeViewModel references remain

#### Manual

- [x] 3.4 Granting location centers the map on the user
- [x] 3.5 Denying location centers on Warsaw without crashing
- [x] 3.6 All 100 venues render with correct sport colors
- [x] 3.7 Pan and zoom respond within the 2s NFR
- [x] 3.8 Log out from the map toolbar returns to Login
- [x] 3.9 Relaunch returns to the map still logged in
- [x] 3.10 API down shows the error state with Retry

### Phase 4: Venue bottom sheet

#### Automated

- [ ] 4.1 Android head builds
- [ ] 4.2 Windows head builds

#### Manual

- [ ] 4.3 Pin tap opens the sheet with correct venue details
- [ ] 4.4 Tapping another pin swaps sheet content
- [ ] 4.5 Sheet dismisses and the map stays interactive
- [ ] 4.6 Create event button is disabled and inert
- [ ] 4.7 Supported sports list correctly for multi- and single-sport venues
- [ ] 4.8 Sheet honors the 8pt grid and design tokens

### Phase 5: Sport filter and manual-address fallback

#### Automated

- [ ] 5.1 Android head builds
- [ ] 5.2 Windows head builds
- [ ] 5.3 Whole solution builds

#### Manual

- [ ] 5.4 All chip selected by default with every venue visible
- [ ] 5.5 Selecting a sport hides non-matching venues and recolors the rest
- [ ] 5.6 Re-selecting All restores every venue and base coloring
- [ ] 5.7 Filter changes are instant with no network request
- [ ] 5.8 Location denied shows address field and banner; valid address recenters
- [ ] 5.9 Unresolvable address shows the snackbar
- [ ] 5.10 Location granted hides the address field and banner
- [ ] 5.11 Banner dismisses without obstructing the chip row or map
