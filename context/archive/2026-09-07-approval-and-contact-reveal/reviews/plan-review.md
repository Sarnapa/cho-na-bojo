<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Approval and Contact Reveal (S-05)

- **Plan**: `context/changes/approval-and-contact-reveal/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-07
- **Verdict**: SOUND (after triage; original verdict: REVISE)
- **Findings**: 1 critical, 6 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | PASS |
| Plan Completeness | PASS |

## Grounding

Grounding: 16/16 paths ✓, 10/10 symbols ✓, brief↔plan ✓

## Findings

### F1 — Reject bypasses the lock that serializes acceptance

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 — Accept and reject endpoints
- **Detail**: The plan serializes acceptance by locking `SportsEvents`, but explicitly says rejection writes `Status` directly and needs no lock. `EventJoinRequest` has no concurrency token, and its current constraints only validate stored values (`server/Data/ChoNaBojoContext.cs:296-331`). Concurrent accept and reject can both observe `Pending`, both return success, and leave whichever status commits last. That breaks final rejection and can reveal contacts after a successful rejection—or remove them after a successful acceptance.
- **Fix**: Serialize every resolution under the same event-row lock, then re-read the request and organizer relationship after acquiring the lock before applying idempotency/state checks and writing the transition.
  - Strength: Preserves one lock order for accept, reject, and auto-accept; no schema migration or second concurrency mechanism.
  - Tradeoff: Rejects serialize with accepts for the same event, which is negligible at the stated MVP scale.
  - Confidence: HIGH — without a concurrency token or conditional update, direct competing EF updates are last-writer-wins.
  - Blind spot: The plan's Supabase transaction-pooler assumption remains to be verified during implementation.
- **Decision**: FIXED — serialized reject through the event-row lock and required a locked re-read before transition checks

### F2 — Auto-accept exception recovery lacks a rollback contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 — Honor `AutoAccept` on request creation
- **Detail**: The current replay-first handler catches unique/FK `DbUpdateException` and queries again (`server/Events/EventEndpoints.cs:283-391`). Inside the new explicit PostgreSQL transaction, a failed `SaveChanges` leaves the transaction aborted; clearing tracking is not enough, and the replay query fails until rollback. The plan says to preserve the handlers but does not define helper transaction ownership, rollback, or detachment of the `Added` request.
- **Fix**: Specify that the auto-accept helper owns lock + insert + transition + commit; on `DbUpdateException` it rolls back first, clears/detaches the failed entity, then performs unique replay or FK mapping outside the failed transaction.
  - Strength: Preserves current idempotent replay while keeping insert and slot claim atomic.
  - Tradeoff: The helper owns more of request creation and exception mapping.
  - Confidence: HIGH — PostgreSQL rejects subsequent commands in an aborted transaction until rollback.
  - Blind spot: Exact Npgsql constraint metadata used by current handlers was not exercised against the local database.
- **Decision**: FIXED — assigned transaction ownership to the auto-accept helper and required rollback plus detachment before replay/error mapping

### F3 — Contact reveal response is not an exact API contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3 — Contact DTO / contact reveal endpoint
- **Detail**: The plan defines `ContactInfoResponse`, then only describes an unnamed "wrapper" with a person, relationship, and optional request id. It never fixes the response record names, list cardinality, relationship encoding, request-id nullability, or organizer-versus-participant payload shape. Phase 5 therefore has to invent the server contract it consumes.
- **Fix**: Add exact wrapper and row record signatures, including collection shape, relationship representation, `JoinRequestId` semantics, and one example payload for organizer and accepted-participant callers.
  - Strength: Makes Phases 3 and 5 independently implementable and prevents client/server DTO drift.
  - Tradeoff: Commits now to a wire shape that later roster work must evolve.
  - Confidence: HIGH — the shared Contracts project already treats DTO records as the authoritative wire contract.
  - Blind spot: No external consumers exist yet, so migration compatibility is not currently a concern.
- **Decision**: FIXED — defined exact contact response records, relationship and request-id semantics, cardinality, and caller-specific payload examples

### F4 — Request-detail navigation and contact cleanup are undecided

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phases 4–5 — My-events page and contact component
- **Detail**: Phase 4 leaves "either a second page or an in-page detail section" open, and Phase 5 leaves the reusable view unnamed. This choice determines Shell routing, DI registrations, back navigation, and where contact memory is cleared. Existing pages are transient and perform cleanup in `OnDisappearing` (`MauiProgram.cs:83-99`; `MapPage.xaml.cs:38-64`), but the plan does not assign equivalent lifecycle hooks to the new surface.
- **Fix A ⭐ Recommended**: Use an in-page request-detail state inside `MyEventsPage`, name the reusable component `ContactRevealView`, clear contacts in `MyEventsPage.OnDisappearing` and before changing the selected event, and refresh status in `OnAppearing`.
  - Strength: Honors the brief's "one new navigation concept" decision and avoids another route/back-stack lifecycle.
  - Tradeoff: `MyEventsViewModel` owns list and detail state.
  - Confidence: HIGH — it has the smallest navigation blast radius in the current one-root Shell.
  - Blind spot: Final phone layout density still needs manual validation.
- **Fix B**: Add a transient `MyEventRequestsPage` plus `ContactRevealView`, register an explicit Shell route, and clear contacts in that page's `OnDisappearing`.
  - Strength: Separates list and request-detail state into focused pages.
  - Tradeoff: Adds routing/back-stack behavior while the Shell is also being converted to tabs.
  - Confidence: MEDIUM — no existing Shell detail-route pattern is present.
  - Blind spot: Tab reselection and modal interaction require extra testing.
- **Decision**: FIXED via Fix A — selected an in-page detail state, named `ContactRevealView`, and assigned refresh/cleanup lifecycle hooks

### F5 — Contact launch and copy gestures are not implementable as written

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 5 — Contact view data and component
- **Detail**: A row is specified as both tappable to launch and "tap-to-copy," so one tap has two actions. The model stores a generic `CommunicatorHandle` for Messenger, Instagram, and WhatsApp, but no platform-specific validation or existing launcher helper exists. A WhatsApp handle is not necessarily a valid `wa.me` phone target, so a reliable URI cannot always be constructed.
- **Fix**: Define separate launch and copy controls, exact URI mappings, URI escaping/validation, and copy-only fallback whenever a communicator handle cannot produce a safe platform URL.
  - Strength: Every gesture has one outcome and malformed handles never launch misleading URIs.
  - Tradeoff: Some messenger rows may be copy-only rather than one-tap open.
  - Confidence: HIGH — the current enum has three platforms but the registration contract provides only an unconstrained handle.
  - Blind spot: Desired Android deep-link behavior for each installed app has not been product-tested.
- **Decision**: FIXED — separated open/copy controls and defined validated URI mappings with copy-only fallback

### F6 — The authorization-matrix prerequisites are undercounted

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Prerequisites / Phase 3 automated verification
- **Detail**: The brief requires at least three accounts, but the one-event matrix names six distinct roles/accounts (A–F), including two accepted participants plus pending, rejected, and uninvolved callers. The manual setup also uses a participant limit of 2, which cannot admit the second accepted participant.
- **Fix**: Require six seeded accounts and a limit of at least 3 for the automated matrix, or rewrite the matrix as an explicit four-account transition sequence; update both `plan.md` and `plan-brief.md` consistently.
- **Decision**: FIXED — required six seeded accounts and a dedicated limit-3-or-higher event consistently in the plan and brief

### F7 — The stale-contact criterion describes an impossible transition

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 5 manual verification / Progress 5.10
- **Detail**: "A stale contact for a request that was rejected in the meantime" cannot occur under this plan: pending/rejected requests never reveal contacts, and an accepted request cannot transition to `Rejected`. Remove-participant—the first real accepted-to-unentitled transition—is explicitly deferred to S-07.
- **Fix**: Replace both the criterion and Progress 5.10 with a tab-switch clear-and-refetch/no-value-flash check, and defer revocation-cache verification to S-07 when participant removal exists.
- **Decision**: FIXED — replaced the impossible transition with clear/refetch/no-value-flash verification and deferred revocation-cache testing to S-07
