# S-02: Map Venue Discovery — Plan Brief

> Full plan: `context/changes/map-venue-discovery/plan.md`

## What & Why

Give the app its core discovery surface. Today a logged-in user lands on a placeholder that literally says "the map and events are coming soon"; this slice replaces it with a Google Map of Warsaw's seeded sports venues, centered on the user, filterable by discipline, with a tappable venue sheet. Nothing downstream works without it — S-03 creates events *at a venue picked on this map*, and S-04/S-05 close the matchmaking loop from there.

## Starting Point

F-01 already shipped everything on the data side: `Venue.Location` is a PostGIS `geometry(Point,4326)`, 100 Warsaw venues and 10 sports are seeded, and there is even a GiST index commented *"powers the S-02 map-viewport bbox queries"*. `server/Program.cs:123` declares an authorized `/api` group that is **never used** — this slice is what consumes it. S-01 established MVVM, transparent JWT attachment, typed API results, and the design-token styles. The app has **no map dependency at all**, and its manifest declares only `INTERNET` and `ACCESS_NETWORK_STATE`.

## Desired End State

The user opens the app and lands on the map — centered on them if they allow location, on Warsaw with a centered warning and address-search launcher if they don't. The launcher opens a dedicated screen with debounced suggestions; selecting one or submitting free text recenters the map. All 100 venues appear as pins, colored by sport (41 venues are single-sport; the other 59 show a brand-green multi-sport pin). Tapping a pin opens a bottom sheet with the venue's name, address, description and disciplines, plus a disabled "Create event" button marking S-03's landing spot. A chip row filters to one sport at a time, instantly and without a network call.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Venue fetch strategy | Load all once per session, filter in memory | 100 venues is ~30 KB, so this meets the 2s NFR trivially and sidesteps the map control's broken Android viewport API. |
| Scale caveat | Documented in plan **and** in code | The load-all approach is only valid at one-city scale; the bbox evolution path must be discoverable by the next reader. |
| Map control | Official `Microsoft.Maui.Controls.Maps` 10.0.100 | Microsoft-backed with working `MoveToRegion`/`PinClicked`; its Android camera bug is irrelevant once we stop doing viewport queries. |
| Google account | GCP project with billing, key restricted to package + signing cert | 10k free map loads/month covers the MVP; a card on file is the unavoidable cost of Google tiles. |
| API key handling | `AndroidManifestPlaceholders` fed from gitignored `secrets/maps.props`, CI secret in prod | Key never enters git, and `secrets/` is already gitignored and already holds the keystore. |
| Pin appearance | Colored default markers, **not** sport glyphs | Glyph pins need bitmap composition the official control can't do until .NET 11's `Pin.ImageSource`; colors need only ~20 lines of Android code. |
| Sport filter | Single-select with an "All" default | Matches ui-guidelines §6E literally and keeps pin color unambiguous when a filter is active. |
| Filtered pins | Hide non-matching, recolor the rest to the selected sport | Makes the map answer "where can I play basketball?" directly. |
| API shape | Two endpoints: `GET /api/venues`, `GET /api/sports` | Resource-shaped rather than screen-shaped, so S-03 and S-04 reuse them unchanged. |
| Location denied | Center on Warsaw + centered warning + dedicated address-search screen | The map remains usable while search gets enough space for live suggestions and explicit submission. |
| Geocoding | Typed service over `Geocoding.Default` with debounce and graceful provider failure | Native geocoding remains dependency-free but is treated as fallible on stale or unsupported images. |
| Current location | App-owned lower-left Material icon with explicit camera requests | Avoids the native button colliding with filters and guarantees repeated taps work after panning. |
| Navigation | Map replaces `HomePage` as the Shell route | The map is the product's main screen; logout moves to a toolbar item. |
| Client caching | In-memory for the app session | No invalidation logic, and seed changes are picked up on the next cold start. |
| Verification | Manual only, with a documented emulator script | Consistent with F-01/F-02/S-01; the repo has no test projects. |

## Scope

**In scope:** `GET /api/venues` + `GET /api/sports` on the authorized `/api` group; shared venue/sport DTOs; Maps package with build-time key injection; location permissions and runtime flow; session-scoped catalog cache; map screen replacing Home; sport-colored pins via a custom Android pin handler; venue bottom sheet; single-select sport filter; batched marker refresh; dedicated address search with suggestions; centered warning/error overlays; custom current-location control; loading/empty/error states.

**Out of scope:** viewport-bounded queries (GiST index stays unused); sport-glyph pin icons (deferred to .NET 11); anything about events beyond a disabled button; venue search, clustering, disk cache, pull-to-refresh; automated tests; iOS; an Azure Maps token for the Windows head.

## Architecture / Approach

Bottom-up in five phases: server contract → app plumbing (no UI) → map screen → interaction → filtering. The server projects PostGIS `Point` into plain lat/lng doubles so `ChoNaBojo.Contracts` stays dependency-free per `lessons.md`. The client fetches both endpoints concurrently into a singleton catalog, and every later interaction — filtering, pin recoloring, sheet content — is pure in-memory work over that snapshot. The only platform-specific code is a ~20-line `MapPinHandler` subclass, guarded by `#if ANDROID`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Server read API | `/api/venues` + `/api/sports`, shared DTOs, `/api` group finally used | Transposing NetTopologySuite X/Y puts every venue in the wrong hemisphere |
| 2. App plumbing | Maps package, key injection, permissions, typed client + session cache | Key leaking into git; breaking the `net10.0-windows` head |
| 3. Map screen | Map replaces Home, location centering, sport-colored pins | Blank grey map from a misconfigured API key; permission flow must run on the main thread |
| 4. Venue sheet | Pin tap → §7 bottom sheet with venue details | MAUI has no native bottom sheet — it's a hand-built overlay |
| 5. Filter + fallback | Batched filtering; dedicated address search; centered overlays; custom current-location control | Native geocoding depends on the image's provider and can fail even when `Geocoder.IsPresent` is true |

**Prerequisites:** S-01 (done); a GCP project with billing and the Maps SDK for Android enabled; a `GOOGLE_MAPS_API_KEY` repo secret; an Android emulator running a **Google APIs** system image; a running API.
**Estimated effort:** ~4–5 sessions across 5 phases (solo, after-hours).

## Open Risks & Assumptions

- **The Google Cloud setup is a hard external dependency.** Phase 3 is unverifiable until a valid, correctly-restricted key is in place — a misrestricted key fails as a silent blank grey map, not an error.
- **Geocoding testing is environment-sensitive.** Native Android providers can return empty results or opaque transport failures such as `grpc failed`; fresh Google images usually work, but failure must remain non-fatal.
- **Warsaw-centering and load-all both hardcode a single-city assumption.** Both are documented in code, but both must be revisited before a second city is added.
- **Deleting `HomePage` touches S-01's proven logout and root-swap path**, so session persistence and back-stack clearing need re-verification in Phase 3.
- **Hue-only sport encoding is near its practical limit** at 11 categories; this is precisely why the .NET 11 glyph upgrade is recorded as deferred work.
- No automated tests means the filter predicate and hue mapping are unguarded against regressions in S-03/S-04.

## Success Criteria (Summary)

- A logged-in user lands on a map centered on their location showing all Warsaw venues, and can tap any venue to see its details.
- Denying location still yields a usable map, with a working address search to recenter it.
- Filtering to a sport instantly shows only venues offering it — and venue data is never visible to an unauthenticated caller.
