---
change_id: approval-and-contact-reveal
title: Approval and contact reveal
status: impl_reviewed
created: 2026-09-07
updated: 2026-09-10
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### 2026-09-08 — VenueEventsPage modal-close crash (async void guards)

Closing the venue events modal crashed the Android app with
`Android.Runtime.JavaProxyThrowable` wrapping
`InvalidOperationException: "PlatformView cannot be null here"`.

Root cause: `OnCloseClicked` → `CompleteAndCloseAsync` → `Navigation.PopModalAsync`
tears down the page; UraniumUI's `StatefulButtonHandler.DisconnectHandler`
(`UraniumUI/Handlers/StatefulButtonHandler.cs:59`, registered by `.UseUraniumUIMaterial()`
in `MauiProgram.cs`) calls `VisualStateManager.GoToState`, which clears bindable
properties and re-enters the button's property mapper after MAUI has already nulled
`ViewHandler<IButton, MaterialButton>.PlatformView`. Because the event handler is
`async void`, the exception was posted to the Android sync context and became fatal.

Decision: guard the `async void` handlers in `VenueEventsPage` with try/catch and log
(`ReportHandlerFailure`) instead of letting teardown faults kill the process. The
underlying UraniumUI handler bug is not fixed here — revisit if the same crash shows up
on other pages or after a UraniumUI upgrade.

Follow-up: the catch stopped the crash but not the damage — the exception is raised inside
`Page.SendNavigatedFrom` during `PopModalAsync`, so Shell's navigation pipeline aborted
mid-pop and left the modal stack inconsistent (no further venue could be opened).
Fixed at the source with `Platforms/Android/SafeStatefulButtonHandler.cs`, a
`StatefulButtonHandler` subclass that swallows the `InvalidOperationException` from
`DisconnectHandler`, registered for `Button` in `MauiProgram.ConfigureMauiHandlers`
(after `UseUraniumUI()` so it wins the mapping). The page-level guards stay as defense in
depth.

### 2026-09-10 — `mailto:` built unescaped behind a round-trip guard (deliberate plan deviation)

Plan Phase 5 §2 specified `mailto:{Uri.EscapeDataString(address)}`. `TryBuildEmailUri`
(`app/ChoNaBojoApp/ViewModels/MyEventsViewModel.cs:1411-1421`) instead emits
`mailto:{address.Address}` unescaped, gated by a stricter guard: `MailAddress.TryCreate`
must succeed **and** the parsed `address.Address` must equal the raw input
case-insensitively, so only clean round-tripping addresses ever reach the URI.

Rationale: `Uri.EscapeDataString` percent-encodes the `@`, producing a non-standard
`mailto:` that some Android mail clients reject. The round-trip equality check closes the
same injection surface the escape was there to close, without breaking the scheme. This is
a decision, not drift — recorded via `/10x-impl-review` finding F2.

### 2026-09-10 — `MapViewModel` reflects the auto-accept outcome (unplanned but required)

`MapViewModel.cs` is in no phase's "Changes Required" list, but Phase 2 made `AutoAccept`
real: `POST /api/events/{id}/join-requests` can now return `Accepted` immediately instead
of always `Pending`. The map's venue-events card is a pre-existing surface that consumes
that response, so it had to render the new outcome or it would show "Request pending" for
a request the server had already accepted.

Changes (`MapViewModel.cs:517-539`): on an `Accepted` response the card's
`ParticipantCount` is incremented once — guarded by `card.CurrentUserRequestStatus !=
Accepted` so a replay doesn't double-count, and clamped with `Math.Min(..., ParticipantLimit)`
— and the snackbar says "You've joined this event." (or "You're already part of this
event." on replay). Status labels for `Accepted`/`Rejected` were added at `:105-125`.

Recorded via `/10x-impl-review` finding F3 so a future reader doesn't read it as untracked
scope creep.
