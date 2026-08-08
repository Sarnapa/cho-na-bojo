<!-- PLAN-REVIEW-REPORT -->
# Plan Review: S-01 Registration, Login, and Persistent In-App Session

- **Plan**: `context/changes/account-and-session/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-08
- **Verdict**: REVISE -> SOUND after triage (all 9 findings fixed 2026-08-08)
- **Findings**: 4 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | FAIL |
| Lean Execution | PASS |
| Architectural Fitness | FAIL |
| Blind Spots | FAIL |
| Plan Completeness | FAIL |

Overall is REVISE rather than RETHINK: four dimensions fail on specifics, but the architecture
(bottom-up layering, root-swap gating, single-flight refresh, shared-validation reuse) is sound
and every fix is local — no re-architecture is required.

## Grounding

15/15 claimed paths exist; 6/7 symbols verified (`material:MaterialButton` refuted — see F3);
Progress↔Phase mapping complete (26 steps, 1.1–4.6, all four phases matched);
brief↔plan consistent apart from one drift (see F8).
`docs/reference/contract-surfaces.md` does not exist — contract-surface check skipped.

**Positive grounding note:** CommunityToolkit.Maui 15.0.0 ships `net10.0-android36.0` and requires
`Microsoft.Maui.Controls >= 10.0.60`, compatible with the pinned `MauiVersion` 10.0.71. The brief's
"CommunityToolkit.Maui must resolve against MauiVersion 10.0.71" risk is closed — pin 15.0.0 in Phase 1 §1.

## Findings

### F1 — The slice never makes a single authenticated call

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: End-State Alignment
- **Location**: Phase 1 §5, Phase 2 §3/§4, Phase 4 §2, Success Criteria 2.6 / 4.5
- **Detail**: All four auth endpoints are `AllowAnonymous` (`server/Auth/AuthEndpoints.cs:21,25,29,33`);
  only `GET /auth/me` requires authorization (`:38`). The planned `IApiService` surface (plan.md:98) is
  register/login/logout/refresh — no `/auth/me` — and `HomePage` is a static placeholder (plan.md:179).
  Nothing in the slice ever sends a bearer token, so `AuthenticatingHttpMessageHandler` (the plan's own
  "riskiest code") is dead code and these criteria cannot be exercised: 2.6 "token refreshed transparently
  on first protected call", 4.5 "invalid refresh → auto-sign-out on the next protected call", and the edge
  cases "concurrent protected calls after 15-min expiry" and "offline at launch". Worse for the end state:
  with optimistic restore and zero protected calls, a user whose refresh family was revoked stays parked in
  Home indefinitely and is never signed out.
- **Fix A ⭐ Recommended**: Add `Task<CurrentUserResult> GetCurrentUserAsync(...)` hitting `GET /auth/me` and
  call it from `HomeViewModel` on appearing (Phase 2 §3); add matching Progress steps (2.8, 4.7).
  - Strength: One small addition makes the whole handler path live — bearer attach, 401 refresh, and expiry
    sign-out all become reachable and verifiable; the endpoint already exists and returns `CurrentUserResponse(UserId)`.
  - Tradeoff: One extra request per Home entry; needs new Progress steps.
  - Confidence: HIGH — `/auth/me` is `RequireAuthorization` and already implemented.
  - Blind spot: Whether Home should block on the call or fail silently isn't decided yet.
- **Fix B**: Defer the handler to S-02; do a blocking `/auth/refresh` at startup instead.
  - Strength: Removes the riskiest code from this slice entirely.
  - Tradeoff: Contradicts the "optimistic restore" decision and the no-blocking-network-at-launch performance
    goal; pushes the risk downstream.
  - Confidence: MEDIUM — simpler, but reopens a settled decision.
  - Blind spot: Offline-at-launch behavior would need redefining.
- **Decision**: FIXED (Fix A) — /auth/me added to IApiService + HomeViewModel on-appearing call; Progress steps 2.8 / 4.8 added

### F2 — Handler↔ApiService DI cycle will recurse on the first HTTP call

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §3, §4, §5, §8
- **Detail**: `ApiService` resolves the named client in its constructor
  (`app/ChoNaBojoApp/Services/ApiService.cs:7-10` — `httpClientFactory.CreateClient("ChoNaBojoApi")`).
  The plan registers `AuthenticatingHttpMessageHandler` on that same client (plan.md:106) and puts refresh on
  `IApiService` (plan.md:98) and server logout on `ISessionService` (plan.md:90) — both handler dependencies.
  Resolving the handler → resolves `IApiService` → `CreateClient("ChoNaBojoApi")` → rebuilds the handler chain
  → resolves the handler again: unbounded recursion (StackOverflow) at the first request. The plan also asserts
  "the refresh HTTP call bypasses this handler" (plan.md:54,106) without specifying a mechanism, contradicting §4's placement.
- **Fix A ⭐ Recommended**: Add a second, un-handled named client (e.g. `"ChoNaBojoAuth"`) behind an
  `IAuthTokenClient` used for refresh and logout; the handler depends only on `ITokenStore` + `IAuthTokenClient`.
  - Strength: Breaks the cycle structurally and satisfies the "refresh bypasses the handler" rule by construction.
  - Tradeoff: One more named client and interface to register.
  - Confidence: HIGH — standard HttpClientFactory + auth-handler pattern.
  - Blind spot: `BaseAddress` configuration is now duplicated across two client registrations.
- **Fix B**: Handler ctor-injects `IServiceProvider` and resolves `IApiService` lazily.
  - Strength: No new client; smallest edit.
  - Tradeoff: Service locator hides the dependency, and refresh would still travel through the handled client
    unless separately suppressed.
  - Confidence: MEDIUM — fragile; the bypass requirement stays unenforced.
  - Blind spot: Re-entrancy under concurrent 401s not verified.
- **Decision**: FIXED (Fix A) — separate un-handled "ChoNaBojoAuth" client behind IAuthTokenClient; handler no longer depends on IApiService

### F3 — `material:MaterialButton` does not exist in UraniumUI 3.0

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §7, Phase 2 §2, Phase 3 §2
- **Detail**: Scanned the restored packages: the string `MaterialButton` appears 0 times in
  `UraniumUI.Material.dll` across every TFM, and its only occurrence anywhere is the
  `Google.Android.Material.Button` interop inside `UraniumUI.dll` (android). `TextButton` appears 0 times,
  so `StyleClass="TextButton"` has no backing style either. `TextField`, `PickerField`, and
  `TextFieldPasswordShowHideAttachment` (password show/hide toggle) DO exist — those parts of the plan are sound.
  Every screen and the Phase 1 style contract name `material:MaterialButton`, so Phase 1 fails to compile as written.
  Root cause is upstream: `context/foundation/ui-guidelines.md:59-60` names the control and the plan inherits it.
- **Fix**: Verify the real button API in Phase 1 before writing styles; target MAUI `Button`
  (pill via `CornerRadius=24`, `HeightRequest=48`, `PrimaryColor`/`OnPrimaryColor` tokens) with keyed
  primary/secondary styles, and correct `ui-guidelines.md` §B to match.
- **Decision**: FIXED — plan targets MAUI Button with PrimaryButtonStyle/SecondaryButtonStyle; ui-guidelines.md corrected (sec 6.B rewritten; remaining MaterialButton references in sec 6.C and the bottom-sheet/error-state sections replaced)

### F4 — 409 `RetryInProgress` handling can revoke the user's refresh family

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Critical Implementation Details (plan.md:55), Phase 1 §5
- **Detail**: The plan says "If refresh returns `409 RetryInProgress`, retry the refresh once with the current pair."
  The server's semantics (`server/Auth/RefreshTokenService.cs:111-131`) are that the presented token was already
  consumed and has a live child inside a 20-second grace window — and the replacement token's raw value is never
  returned (only its hash is stored). Retrying `/auth/refresh` with the same token therefore returns 409 again for
  20 s, and once the grace lapses it falls into `RevokeFamilyAsync` → `ReuseDetected` → 401, signing the user out on
  every device. Under single-flight there is no sibling refresh in-process to supply the new pair, so an in-app 409
  (typically a lost/timed-out refresh response) is unrecoverable — exactly the family-reuse revocation the plan
  claims to prevent.
- **Fix A ⭐ Recommended**: On 409, re-read the token store and retry the original request only if the stored pair
  changed. If unchanged, do NOT re-call `/auth/refresh` — treat the session as unrecoverable and sign out. Never
  retry after the 20 s grace.
  - Strength: Matches the server's actual grace-window/reuse-detection logic and makes family-wide revocation
    impossible from the client side.
  - Tradeoff: A lost refresh response costs the user one re-login.
  - Confidence: HIGH — read directly from `RotateAsync`'s branch structure.
  - Blind spot: How often the response-loss case occurs on flaky mobile networks.
- **Fix B**: Retry once inside the grace window after a short delay, then sign out.
  - Strength: Single deploy; covers a genuinely concurrent multi-process refresh.
  - Tradeoff: Cannot succeed in-process (the client can never learn the replacement token value) and burns one of
    the 10/min rate-limit permits.
  - Confidence: LOW — the retry has no path to success under single-flight.
  - Blind spot: Timing against the 20 s window is unverified on a real device.
- **Decision**: FIXED (Fix A) — 409 never re-calls /auth/refresh; re-read token store, retry only if the pair changed, else sign out locally

### F5 — Transient refresh failures (429/5xx/offline) will look like session expiry

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 §5, Phase 4 §2, Testing Strategy
- **Detail**: The handler contract enumerates only success / 409 / 401. The `/auth` group is rate limited to
  10 requests per minute per IP across register+login+refresh+logout (`server/Program.cs:83-91`, `QueueLimit = 0`),
  and on the emulator (and behind Railway's proxy) all traffic shares one partition — the manual test script
  (plan.md:308-315) issues well over 10 auth calls in a burst. A 429, 5xx, or transport failure that falls into the
  "refresh failed" bucket silently logs the user out and destroys a valid session.
- **Fix**: Specify the classification explicitly — 401/invalid → sign out; 409 → per F4; 429/5xx/transport → keep the
  session, surface a retry snackbar, no navigation. Add a manual verification step for "airplane mode during a
  protected call does not log the user out".
- **Decision**: FIXED — explicit refresh-failure classification (401 sign out / 409 per F4 / 429-5xx-transport keep session + retry snackbar); Progress step 4.7 added

### F6 — Startup ordering (sync `CreateWindow` vs async session init vs root swap) is undefined

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §4, Phase 1 §6
- **Detail**: `App.CreateWindow` is synchronous and today returns `new Window(new AppShell())`
  (`app/ChoNaBojoApp/App.xaml.cs:10-13`), but the plan requires awaiting `SessionService.InitializeAsync()` — a
  main-thread `SecureStorage` read — before picking a root (plan.md:187), while `NavigationRootService` mutates
  `Application.Current.Windows[0].Page` (plan.md:114), which does not exist until `CreateWindow` returns.
  "A brief loading page is acceptable" leaves the ordering, the async kick-off point, and exception handling
  unspecified; an unobserved async-void bootstrap crashes the app on the very `SecureStorage` failure the plan
  calls out as a gotcha.
- **Fix**: Specify it concretely — `CreateWindow` returns `new Window(new LoadingPage())`; the loading page's
  `Loaded` handler awaits `InitializeAsync()` in try/catch on the main thread and then calls `SetAppRoot()` or
  `SetAuthRoot()`; any failure routes to Login.
- **Decision**: FIXED — CreateWindow returns Window(LoadingPage); LoadingPage.Loaded awaits InitializeAsync in try/catch then swaps root; any failure routes to Login

### F7 — Phase blocks use checkboxes, violating the Progress parsing contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Success Criteria in all four phases (plan.md:136-143, 193-202, 244-254, 288-296)
- **Detail**: `/10x-implement` requires that "checkmarks live ONLY [in Progress]. Phase blocks contain plain `- `
  bullets (no checkboxes)" and that phase blocks are read-only
  (`.github/skills/10x-implement/SKILL.md:35,125`); it locates the next step as "the first `- [ ]` line in document
  order" (`:46`). This plan's Success Criteria are all `- [ ]`, so a document-order scan lands on Phase 1's criteria
  instead of Progress, and the duplicate checkboxes invite the implementer to flip the wrong copy. The `## Progress`
  section itself is correct (26 steps, 1.1–4.6, all phases matched).
- **Fix**: Convert the Success Criteria bullets in all four phase blocks from `- [ ]` to plain `- `.
- **Decision**: FIXED — all phase-block Success Criteria converted to plain bullets; checkboxes live only in ## Progress

### F8 — `AccessTokenExpiresUtc` is stored but never read

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2 (plan.md:82) vs `plan-brief.md:23`
- **Detail**: The brief claims the handler is where "one place enforces the 15-min expiry", but the Phase 1 §5
  contract only refreshes reactively on a 401 — the persisted expiry timestamp is never consulted, making the
  `auth_expires` key dead weight.
- **Fix**: Either add a proactive pre-flight check (refresh when expiry is within ~60 s) or drop the
  expiry-enforcement claim from the brief and stop persisting the field.
- **Decision**: FIXED — pre-flight expiry check (refresh when within 60s) added to the handler contract; brief decision row updated

### F9 — `MainPage` cleanup misses its DI registration

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §3, Migration Notes (plan.md:179, 331)
- **Detail**: The plan says `AppShell.xaml` stops referencing the counter `MainPage` and that removing it is optional
  cleanup, but `app/ChoNaBojoApp/MauiProgram.cs:36` also has `AddTransient<MainPage>()`. Deleting the file without
  touching `MauiProgram.cs` breaks the build.
- **Fix**: Name both `AppShell.xaml` and `MauiProgram.cs` in the Phase 2 §3 contract.
- **Decision**: FIXED — Phase 2 sec 3 now names MauiProgram.cs; Migration Notes call out the AddTransient<MainPage>() removal


