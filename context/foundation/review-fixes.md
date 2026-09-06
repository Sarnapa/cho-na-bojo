# Review Fixes Backlog

> Deferred fixes surfaced by implementation reviews. Each entry survives its originating change being archived, so it stays actionable once `context/changes/<change-id>/` moves to `context/archive/`.
>
> Append-only. Mark an entry `Status: done` (with the commit sha) rather than deleting it.

## RF-1 — Migrate the create-event date/time inputs to Uranium UI fields

- **Status**: open
- **Slice**: S-03 (Event creation)
- **Phase**: Phase 4 — MAUI Create Form and Recovery Flow
- **Source**: `context/changes/event-creation/reviews/impl-review.md` → finding F4 (Fix A accepted 2026-09-06 — accessibility half applied, guidelines half deferred)
- **Files**: `app/ChoNaBojoApp/Views/CreateEventPage.xaml:92-180`

**Problem**: Four `<DatePicker>`/`<TimePicker>` controls are hand-wrapped in `<Border>` with a separate caption `Label` standing in for a floating label. `context/foundation/ui-guidelines.md:11` forbids manually building custom input fields by wrapping native controls — Uranium UI components must be used. The rest of the page already complies via `material:TextField`/`material:SelectField`.

**Already done**: `HeightRequest="48"` was added to all four wrappers, satisfying the 48pt touch-target rule (`ui-guidelines.md:37, 56, 118`). Only the guidelines-consistency half remains.

**Work**: Replace the four wrapped controls with `material:DatePickerField` / `material:TimePickerField`, adding keyed styles alongside the existing `TextFieldStyle`/`SelectFieldStyle`.

**Check first**: `ui-guidelines.md:59` records that UraniumUI 3.0 dropped `MaterialButton`; confirm the pinned UraniumUI version actually exposes these field controls before starting.

**Verification**: re-run the Phase 4 manual time-entry matrix (same-day, overnight, exactly-24-hour, past-start, spring DST gap, autumn ambiguous) on an Android device.
