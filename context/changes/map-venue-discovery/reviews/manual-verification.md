# Manual Verification: S-02 Map Venue Discovery

- **Date**: 2026-09-02
- **Environment**: Pixel 9, Android 10.0 API 29, Google APIs
- **Evidence source**: Manual emulator run reported by the user during implementation review
- **Revisions**: `ccab409`, `c5c6f75`, `d8514fa`, `028491a`, `abdab86`

| Script step | Result | Covered behavior |
|-------------|--------|------------------|
| 1. First run, permission granted | PASS | Login flow remained unchanged; accepting location centered the map on the mock location and rendered venue pins. |
| 2. Pin colors | PASS | Single-sport venues used distinct sport colors and multi-sport venues used red orange. |
| 3. Venue sheet | PASS | Pin selection showed the expected details, switching pins replaced the content, dismissal restored the map, the disabled create-event control remained inert, and layout followed the design tokens. |
| 4. Filter | PASS | "All" was selected initially; sport selection filtered and recolored locally without a network request; selecting "All" restored all venues. |
| 5. Performance | PASS | Panning and zooming across Warsaw remained responsive within the two-second NFR. |
| 6. Session | PASS | Killing and relaunching the app returned to the map with the session preserved. |
| 7. Permission denied | PASS | Denial used Warsaw fallback, displayed and dismissed the warning correctly, address selection and free-text submission recentered, invalid/unavailable searches showed feedback, and blocking errors covered the warning. |
| 8. Current location | PASS | Repeated taps after panning returned to the current location and the native top-right location button stayed hidden. |
| 9. Offline | PASS | Cold-starting offline showed the blocking error state; restoring networking and retrying recovered. |
| 10. Logout | PASS | Toolbar logout returned to Login without map content in the back stack. |
| Fresh-clone secret check | PASS | The app built with `secrets/maps.props` absent. |

The API payload-size and endpoint checks are retained separately in `impl-review.md`, where they were reproduced during review.
