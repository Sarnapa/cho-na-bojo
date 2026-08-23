---
change_id: account-and-session
title: Account and session
status: impl_reviewed
created: 2026-07-18
updated: 2026-08-24
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

- 2026-08-08 — Plan review triaged: all 9 findings fixed in `plan.md` (verdict REVISE → SOUND). Side effect: `context/foundation/ui-guidelines.md` corrected — UraniumUI 3.0 has no `MaterialButton`/`TextButton`; use MAUI `Button` with `PrimaryButtonStyle`/`SecondaryButtonStyle`.
- 2026-08-24 — Impl review triaged: 9/10 findings fixed (F9 indentation skipped). Code: sign-out-aware `TryRenewAsync`, transient-refresh → retry snackbar, static refresh lock, pre-send body buffering, atomic single-key `TokenStore`, JSON-failure guards, CS0168 cleanup. Plan: Addendum A (A.1–A.6) records the server/shared and contract deviations.
