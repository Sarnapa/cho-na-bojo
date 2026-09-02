<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Event Creation Implementation Plan (S-03)

- **Plan**: `context/changes/event-creation/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-02
- **Verdict**: REVISE
- **Findings**: 1 critical, 4 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | WARNING |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | WARNING |

## Grounding

16/16 paths ✓, 5/5 symbols ✓, brief↔plan ✓, Progress format ✓ (5/5 phases matched, 34/34 success criteria mapped, no stray checkboxes in phase bodies).

## Verified as accurate (no findings)

- All 16 claimed file paths and 5 claimed symbols exist.
- `AuthenticatingHttpMessageHandler` really does buffer POST bodies (`:51-58`, `:97-102`, `:193-224`), and event creation really is the first authenticated JSON POST to traverse it — every existing POST is either anonymous-excluded or on the separate `ChoNaBojoAuth` client.
- `VenueCatalog` already fetches venues and sports concurrently and preserves the last good catalog on failure, so the Phase 4 refresh contract is a small delta (`VenueCatalog.cs:43-77`).
- DST and 24-hour-duration reasoning is internally consistent between Phase 1 (UTC elapsed) and Phase 4 (local roll-over).
- Shared-project placement follows `context/foundation/lessons.md` correctly; no persisted enum is introduced, so the enum double-guard rule does not apply.
- Checked and **dismissed** a suspected issue: the 24-hour CHECK constraint would *not* be rejected for non-immutability — PostgreSQL does not enforce `IMMUTABLE` inside check constraints (unlike index expressions).

## Findings

### F1 — Modal pattern auto-resolves on dismiss, defeating "block navigation while outcome is unknown"

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 4 §6 (CreateEventPage), Phase 5 §2
- **Detail**: Phase 4 requires the form to "block Back/Cancel while a request outcome is unknown", and mandates following AddressSearchPage's `ShowAsync`/`TaskCompletionSource` handoff. But that pattern actively resolves the TCS on dismissal — `AddressSearchPage.xaml.cs:31-38` nulls `_completion` and completes it with `null` in `OnDisappearing()`, regardless of in-flight work. An implementer copying the pattern inherits this. Android hardware back / swipe-dismiss then tears down the modal while the POST is still running. The event still gets created — and S-03 ships no listing (S-04), no GET detail (explicitly out of scope), and no cancel (S-07), so that event is invisible and unmanageable from the app. Worse, the user re-creates it, and the second attempt carries a *new* `ClientRequestId`, so the idempotency machinery does not deduplicate it. This is the exact failure mode Phase 3's idempotency work exists to prevent, arriving through the UI door.
- **Fix A ⭐ Recommended**: Make in-flight state authoritative over dismissal — override `OnBackButtonPressed` to return `true` while a request is in flight, and have `OnDisappearing` complete the TCS with `null` *only* when no request is outstanding.
  - Strength: Keeps the one modal pattern the codebase already uses; the guard is local to `CreateEventPage`.
  - Tradeoff: `OnBackButtonPressed` does not cover every Android dismissal path (e.g. gesture nav in some shells), so the `OnDisappearing` guard is the real backstop and must define what happens if the page dies anyway.
  - Confidence: HIGH — the conflicting behaviour is verified at `AddressSearchPage.xaml.cs:31-38`, and the existing `_completion` guard is already instance-scoped.
  - Blind spot: Not verified which dismissal gestures the .NET 10 MAUI Android modal host actually routes through `OnBackButtonPressed`.
- **Fix B**: Persist the pending request id/snapshot outside the page so a later launch can replay the same `ClientRequestId` and reconcile via the 200 replay.
  - Strength: Survives page death entirely.
  - Tradeoff: Adds durable client state and a reconciliation path the plan explicitly scoped out ("no process-death restoration"); meaningfully widens Phase 4.
  - Confidence: MEDIUM — correct in principle, but no persisted-draft storage exists in the app today.
  - Blind spot: Where that state would live (Preferences vs SecureStorage) is unexamined.
- **Decision**: PENDING

### F2 — Restrict FK to VenueSport collides with cascading, HasData-seeded reference data

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §1/§3/§4 — composite venue/sport FK, restrictive deletion
- **Detail**: The plan adds `SportsEvent →(VenueId, SportId)→ VenueSport` with restrictive deletion. But the existing model cascades *into* `VenueSport` from both sides — `ChoNaBojoContext.cs:107-110` (`VenueSport → Venue`: Cascade), `ChoNaBojoContext.cs:112-115` (`VenueSport → Sport`: Cascade) — and `Sport` is seeded via `HasData` (`ChoNaBojoContext.cs:73`). Once a single event row exists, deleting a Venue or Sport cascades to `VenueSport` and is then blocked by the RESTRICT edge, failing the whole statement. Concretely: editing the `HasData` sport list makes EF emit `DeleteData` on Sports, and that migration will fail on any database with events. The same applies to the manual Warsaw venue uploads the PRD describes, and to any "venue no longer supports sport X" correction. The plan says "restrictive deletion" without noting that this permanently freezes the reference catalog.
- **Fix A ⭐ Recommended**: Keep RESTRICT, document the operational contract — reference data is append-only once events exist; corrections are UPDATEs, and sport/venue retirement needs its own slice.
  - Strength: RESTRICT is the right integrity choice — cascading a venue deletion into users' events would silently destroy data. Making the rule explicit turns a latent migration failure into a known constraint.
  - Tradeoff: Doesn't solve retirement; the first sport/venue removal still needs a follow-up design.
  - Confidence: HIGH — delete behaviours and `HasData` seeding verified directly in `ChoNaBojoContext.cs`.
  - Blind spot: Whether any planned S-04+ work assumes deletable `VenueSport` rows hasn't been checked.
- **Fix B**: Add an `IsActive` flag to `VenueSport` now instead of relying on deletes.
  - Strength: Makes "venue stopped offering this sport" expressible without touching events, and gives Phase 3's `sport_not_supported_at_venue` a real trigger.
  - Tradeoff: Expands Phase 2 beyond S-03 and adds a filter every read path must respect; no requirement currently asks for it.
  - Confidence: MEDIUM — plausible, but no roadmap slice asks for it.
  - Blind spot: Seed/upload tooling would need updating too.
- **Decision**: PENDING

### F3 — Replay comparison of UTC timestamps will round-trip lossily

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 §1 ("compare the canonical payload"); Phase 2 §1
- **Detail**: The wire contract carries zero-offset `DateTimeOffset`; the column is `timestamptz`. Two conversion hazards the plan doesn't name. (1) **Precision**: PostgreSQL `timestamptz` stores microseconds; .NET `DateTime` has 100ns ticks. A value with a non-zero 7th fractional digit is truncated on write. Comparing the incoming request against the stored row then reports "changed payload" for a byte-identical retry and returns 409 `idempotency_key_reused` — the precise outcome Phase 3 exists to avoid. The plan's own risk note ("exact replay comparison depends on one canonical normalization path") flags the symptom but not this cause. (2) **Kind**: `DateTimeOffset.DateTime` yields `Kind=Unspecified`, which Npgsql rejects for `timestamptz`; only `.UtcDateTime` is safe. There is no UTC value converter in `ChoNaBojoContext` to catch this — the codebase relies on `DateTime.UtcNow` at the call site (`AuthEndpoints.cs:70-81`, `RefreshTokenService.cs:67-80`).
- **Fix**: Normalize both sides through one conversion helper that uses `.UtcDateTime` and truncates to microseconds before persisting *and* before comparing; state that rule explicitly in the Phase 3 contract, and add a Phase 3 verification bullet replaying a request whose timestamps carry sub-microsecond precision.
  - Strength: Removes the whole class of false-conflict bug for the cost of one helper; matches the plan's existing "one canonical normalization path" intent.
  - Tradeoff: Slight extra ceremony on every timestamp write.
  - Confidence: HIGH — Npgsql 10.0.3 `timestamptz` semantics and the absence of any `HasConversion` in the context are both verified.
  - Blind spot: MAUI pickers are minute-granular, so this may never fire from the app itself — it bites API probes and any future client.
- **Decision**: PENDING

### F4 — venue_not_found conflict has no recovery path in the form

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 3 §2 (conflict codes) vs Phase 4 §5 (recovery contract)
- **Detail**: Phase 3 returns four conflict codes: `venue_not_found`, `sport_not_found`, `sport_not_supported_at_venue`, `reference_data_changed`. Phase 4's recovery contract handles exactly one shape of them — "refreshes the catalog, marks only the stale venue/sport selection invalid, and preserves every other field". That works for sport conflicts, because sport is a picker in the form. It does not work for `venue_not_found`: the venue is fixed by the map selection (`MapViewModel.cs:261-273`) and is not an editable field. The user lands in an unlockable form with an invalid, unchangeable venue and no way forward except Cancel. The plan never says the form should close and return the user to the map to pick a different venue, so the end state "recovers without losing the draft" is unreachable for this branch.
- **Fix**: Split the recovery contract by conflict class in Phase 4 §5 — sport-level conflicts refresh the catalog and re-open the sport picker in place; venue-level conflicts (`venue_not_found`, `reference_data_changed` on `venueId`) close the form with an explanatory message and return to the map with the venue sheet dismissed and the catalog refreshed.
  - Strength: Makes every code Phase 3 emits terminate in a defined UI state; no new machinery, just a branch the plan already half-describes.
  - Tradeoff: Venue-level conflicts lose the draft — acceptable, since the draft is anchored to a venue that no longer exists.
  - Confidence: HIGH — venue immutability in the form follows directly from the plan's own "Prepare from one selected `VenueResponse`" contract.
  - Blind spot: How likely venue deletion actually is during an MVP with manually curated Warsaw data — possibly rare enough to justify a simpler message-and-close for all four codes.
- **Decision**: PENDING

### F5 — Changed-payload 409 branch is the most fragile part of Phase 3 and isn't load-bearing

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Lean Execution
- **Location**: Phase 3 §1/§2; Phase 4 §1; Progress 3.5, 5.9
- **Detail**: Idempotency itself is well justified — with no cancel until S-07 and no listing until S-04, a duplicate is unrecoverable. But the plan goes further than that justification requires: it canonicalizes and compares the full payload, and adds a distinct `idempotency_key_reused` 409, a client result case, and UI copy for it. Removing that branch does not change the end state. The client mints one id per draft and only ever resends the exact snapshot (Phase 4 §5), so a same-key-different-payload request is not reachable from the app at all. Meanwhile the comparison is the single most failure-prone piece in the slice (see F3), and it is load-bearing for two Progress items.
- **Fix A ⭐ Recommended**: Same key → return the stored event; drop the payload comparison and the `idempotency_key_reused` code.
  - Strength: Standard idempotency-key semantics, removes the F3 failure mode entirely, and cuts a client result case, a conflict code, and UI copy from an MVP built after-hours on a 3-week budget.
  - Tradeoff: A future non-app client reusing a key with different data gets a silently "wrong" event instead of an error.
  - Confidence: HIGH — the unreachability follows from the plan's own Phase 4 snapshot-retry contract.
  - Blind spot: Whether a later slice wants strict key semantics for an admin or web client.
- **Fix B**: Keep the branch but compare only the id-defining fields (venue, sport, minute-truncated start).
  - Strength: Retains a guard against genuine key reuse while shrinking the surface F3's precision problem can corrupt.
  - Tradeoff: Still two code paths and still a partial comparison to get right; "which fields count" becomes a new judgement call.
  - Confidence: MEDIUM — reduces but does not remove the fragility.
  - Blind spot: Title/description edits would silently replay the old event.
- **Decision**: PENDING

### F6 — Second 409 body shape in the same API

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §2 (`EventConflictResponse`), Phase 4 §2
- **Detail**: Auth already returns conflicts as `Results.Conflict(new { message })` (`AuthEndpoints.cs:67`, `:93`) and `ApiService` parses that shape (`ApiService.cs:179-180`). `EventConflictResponse` introduces a second, structurally different conflict body. Not wrong — the new shape is better — but the plan should say so, or the codebase drifts into two conventions with no recorded decision.
- **Fix**: Note in Phase 1 §2 that `EventConflictResponse` is the new standard conflict shape and auth's anonymous `{ message }` is legacy to be aligned later; keep reusing the existing `ValidationProblemResponse` DTO for the 400 path rather than adding a parallel RFC-7807 parser.
- **Decision**: PENDING

### F7 — Verification step 3.2 runs a command that never exits

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 Automated Verification / Progress 3.2
- **Detail**: "API starts with the migrated development database: `dotnet run --project server`" is listed as automated verification. `dotnet run` blocks indefinitely, so an agent executing the Progress checklist literally will hang on 3.2, and steps 3.3-3.7 depend on that server already running.
- **Fix**: Restate 3.2 as "start the API in the background, confirm it is listening on `http://localhost:5100`, and keep it running for 3.3-3.7", and note that 3.3-3.7 need a JWT obtained via `/auth/login`.
- **Decision**: PENDING

### F8 — Privacy verification is weaker than the guardrail it protects

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 / Phase 5 Automated Verification (`rg` privacy scans)
- **Detail**: Contact-info leakage is the PRD's hardest guardrail, but the checks are substring greps over one or two files. They cannot see a nested type declared elsewhere (the plan's "safe venue/sport summaries" come from other files), nor an organizer summary added later. The genuine check is the response-body inspection already listed under Manual Verification — the greps just shouldn't be mistaken for it.
- **Fix**: Scope the scan to the whole shared Contracts DTO folder and the serialized response body captured in Phase 3, rather than to a single source file.
- **Decision**: PENDING

### F9 — Speculative index shaped for a slice that isn't designed yet

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Lean Execution
- **Location**: Phase 2 §3/§4, Performance Considerations
- **Detail**: The `(VenueId, EstimatedEndsAtUtc)` index is added purely for S-04, which the plan lists under "What We're NOT Doing". Roadmap S-04 also filters by sport and availability, so the final index may well want `SportId` in it — meaning this one gets replaced rather than used. Cheap either way, but it's scope the end state doesn't need.
- **Fix**: Drop the index from Phase 2 and let S-04 add the index its actual query shape requires; keep only the unique `(OrganizerUserId, ClientRequestId)` index, which S-03 does use.
- **Decision**: PENDING
