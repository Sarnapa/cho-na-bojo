<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-01 Registration, Login, and Persistent In-App Session

- **Plan**: `context/changes/account-and-session/plan.md`
- **Scope**: Full plan — Phases 1–4 of 4
- **Date**: 2026-08-23
- **Git range**: `7969849..HEAD` (39 files under `app/`, `server/`, `shared/`)
- **Verdict**: REJECTED → **RESOLVED after triage (2026-08-24)** — 9 of 10 findings fixed, 1 skipped (cosmetic).
- **Findings**: 1 critical, 6 warnings, 3 observations

## Triage summary (2026-08-24)

| Outcome | Findings |
|---------|----------|
| Fixed in code | F1, F2 (Fix A), F3 (Fix A), F4, F6 (Fix A), F7, F8 |
| Fixed in plan | F5 (Fix A — Addendum A.1/A.2), F10 (Addendum A.4/A.5/A.6) |
| Skipped | F9 (indentation — cosmetic) |

Post-triage build: `dotnet build solutions/ChoNaBojo.slnx` → **PASS** (0 errors, 17 warnings — down from 23; all CS0168 cleared).

Dimension verdicts after triage: Plan Adherence PASS, Scope Discipline PASS, Safety & Quality PASS, Architecture PASS, Pattern Consistency WARNING (F9 skipped), Success Criteria PASS.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

**Architecture PASS notes**: layering matches the plan exactly — the handler depends only on `ISessionService`/`IAuthTokenClient` (never `IApiService`), the second un-handled `"ChoNaBojoAuth"` client exists with a shared base-URL constant, refresh cannot recurse through the handler by construction, root swaps replace `Application.Current.Windows[0].Page` outright, and no `shared/` project took an ASP.NET/EF/MAUI dependency (lessons.md rule upheld).

**Automated verification (re-run 2026-08-23)**:
- `dotnet build solutions/ChoNaBojo.slnx` → **PASS** (0 errors, 23 warnings)
- `dotnet build app/ChoNaBojoApp -f net10.0-android` → **PASS** (0 errors, 0 warnings)
- No hardcoded color/spacing literals in `Styles.xaml` → **PASS** (no hex literals; only 8/16/24/48 grid values)

## Findings

### F1 — Logout can be undone by an in-flight refresh, re-persisting tokens after sign-out

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/Services/Auth/SessionService.cs:52-57`, `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:111`
- **Detail**: `SignOutAsync` is guarded by `_signOutLock` and sets `_signedOut = true`, but `SetAsync` takes no lock and never consults `_signedOut`:

  ```csharp
  public async Task SetAsync(AuthSession session)
  {
      await _tokenStore.SaveAsync(session);
      Current = session;
      _signedOut = false;
  }
  ```

  Reachable sequence: `HomePage` appears → `GET /auth/me` → 401 → `RefreshAsync` is awaiting the network → the user taps **Log out** → `SignOutAsync(revokeServer:true)` clears `SecureStorage` and swaps the root to Login → the refresh call returns `Success` → line 111 calls `SetAsync(refreshedSession)`, which **re-writes a token triple into `SecureStorage` and resurrects `Current`**. The user believes they are signed out, but on the next cold launch `LoadingPage` optimistically restores the session and renders `HomePage` — directly violating the plan's Desired End State ("A logged-out user launches the app and sees a Login screen — never any app content"). Server-side family revocation eventually forces a 401, but only *after* app content has already been shown. The same race exists between an expiry sign-out and a concurrent successful refresh.
- **Fix**: Make `SetAsync` sign-out-aware: acquire `_signOutLock`, and if `_signedOut` is `true`, discard the session instead of persisting it (a refresh that completes after sign-out is stale by definition).
  - Strength: One contained change in the class that already owns the invariant; reuses the existing lock and `_signedOut` flag, so the handler needs no changes.
  - Tradeoff: `SetAsync` becomes silently lossy in the race window — acceptable, since the discarded token belongs to a family the server is revoking anyway.
  - Confidence: HIGH — `_signOutLock` + `_signedOut` already exist for exactly this class of problem in `SignOutAsync` (`SessionService.cs:63-76`); this closes the other half.
  - Blind spot: `SetAsync` is also the login/register success path; the guard must not block the legitimate first sign-in (it won't — login calls `SetAsync` after a deliberate user action, but the flag starts `true` from `InitializeAsync`, so the guard must be reset on the explicit auth paths).
- **Decision**: FIXED — `SetAsync` now acquires `_signOutLock` and always clears `_signedOut` (explicit sign-in wins); new `ISessionService.TryRenewAsync` is used by the refresh path and discards the session when `_signedOut` is already `true`. Handler line 111 returns `null` when renewal is refused. Lock order `_refreshLock → _signOutLock` preserved.

### F2 — Transient refresh failure (429/5xx) surfaces as a silent `Unauthorized`, not a retry snackbar

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Success Criteria
- **Location**: `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:68-72,131-135`, `app/ChoNaBojoApp/Services/ApiService.cs:60-63`, `app/ChoNaBojoApp/ViewModels/HomeViewModel.cs:45-52`
- **Detail**: The plan's Critical Implementation Details require: "`429`, `5xx`, or transport/timeout → **keep the session**, propagate the original call's failure to the caller as a **network error (retry snackbar)**". The first half is correct — `RefreshOutcomeStatus.Transient` returns `null` without signing out. The second half is missing: `SendAsync` then returns the **original 401 response** (line 71), which `ApiService.GetCurrentUserAsync` maps to `CurrentUserResult.Unauthorized()` (line 62), and `HomeViewModel` deliberately ignores `Unauthorized` ("handled by AuthenticatingHttpMessageHandler's expiry path, not here"). Net effect: a rate-limited or 5xx refresh produces **no user feedback at all** — no snackbar, no sign-out, no retry affordance. Manual criterion **4.7** ("429 during a protected call → retry snackbar, session preserved") is marked `- [x]` in `## Progress`, but only the session-preservation half is observable in the diff. (Note: true airplane-mode *does* work, because the original request throws `HttpRequestException` before any 401 — which is likely what was tested.)
- **Fix A ⭐ Recommended**: Have the handler signal transient-refresh failure out-of-band — e.g. set a flag in `response.RequestMessage.Options` or throw `HttpRequestException` after a transient refresh — and map it to `CurrentUserResult.Network()` in `ApiService`.
  - Strength: Keeps the classification in the one place that knows *why* the 401 was not recoverable; the existing `Network` result and snackbar plumbing then work unchanged.
  - Tradeoff: Throwing changes the handler's contract from "always returns a response" to "may throw"; the Options-flag variant avoids that but needs a shared key constant.
  - Confidence: HIGH — `ApiService` already catches `HttpRequestException` → `Network()` (`ApiService.cs:67-70`), so the throw variant needs no ViewModel change at all.
  - Blind spot: Not verified whether any future caller relies on receiving the raw 401 response rather than an exception.
- **Fix B**: Add a `Transient`/`ServiceUnavailable` status to `CurrentUserResult`/`AuthResult` and have ViewModels show the retry snackbar for it.
  - Strength: Explicit, typed, and self-documenting at the ViewModel layer; no exceptions used for control flow.
  - Tradeoff: Touches the result unions, `ApiService`, and every consuming ViewModel — a wider edit, and `ApiService` still needs the handler to tell it the 401 was transient.
  - Confidence: MEDIUM — cleaner long-term, but does not by itself solve how the handler communicates the cause.
  - Blind spot: Haven't checked how many S-02+ call sites would need the new branch.
- **Decision**: FIXED via Fix A — `RefreshAsync` now returns a `RefreshResult` (`Session` + `IsTransient`); the reactive 401 path throws `HttpRequestException` on a transient refresh failure, which `ApiService`'s existing catch maps to `Network()` → retry snackbar. Pre-flight refresh still ignores transient failures so a token valid for <60s gets its chance.

### F3 — Single-flight refresh lock is per-handler-instance; it holds only by accident

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality / Architecture
- **Location**: `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:28`, `app/ChoNaBojoApp/MauiProgram.cs:45,51`, `app/ChoNaBojoApp/Services/ApiService.cs:18-21`
- **Detail**: `private readonly SemaphoreSlim _refreshLock = new(1, 1);` is an **instance** field, and the handler is registered `AddTransient` (correct for `AddHttpMessageHandler`). `IHttpClientFactory` builds a fresh handler chain per `CreateClient` call once the 2-minute handler lifetime rotates. Today the single-flight guarantee survives only because `ApiService` is a singleton that caches one `HttpClient` in its constructor (`_httpClient = httpClientFactory.CreateClient("ChoNaBojoApi")`), pinning exactly one handler instance for the app's lifetime. The moment S-02 adds a second service that resolves `"ChoNaBojoApi"` — the plan explicitly frames this client as "the single integration seam ... for all current and future services" — two handler instances with two independent semaphores can call `/auth/refresh` concurrently. That is precisely the scenario the plan warns trips the server's family-reuse detection (`RevokeFamilyAsync` → `ReuseDetected`), signing the user out on every device.
- **Fix A ⭐ Recommended**: Change the field to `private static readonly SemaphoreSlim _refreshLock = new(1, 1);`.
  - Strength: One-word change that makes the guarantee hold by construction regardless of how many handler instances or consumers exist; matches the plan's stated intent.
  - Tradeoff: Static mutable state in a handler is slightly unidiomatic and shared process-wide (harmless here — there is exactly one API surface and one user session).
  - Confidence: HIGH — the semaphore protects a process-global resource (the one stored refresh token), so process-global scope is the correct scope.
  - Blind spot: Would need revisiting if the app ever supported multiple concurrent user sessions.
- **Fix B**: Move the single-flight refresh into `SessionService` (already a singleton) and have the handler call `ISessionService.RefreshAsync()`.
  - Strength: Puts the lock next to the state it protects; no static state; testable in isolation.
  - Tradeoff: Widens `ISessionService`, and moves refresh-outcome classification out of the handler that consumes it — a real refactor across two files.
  - Confidence: MEDIUM — architecturally cleaner, but `SessionService` currently has no knowledge of refresh classification and would grow responsibilities.
  - Blind spot: Haven't traced whether the pre-flight path's `Current` re-read still reads correctly after the move.
- **Decision**: FIXED via Fix A — `_refreshLock` is now `private static readonly`, with a comment explaining that the guarded resource (the single stored refresh token) is process-global.

### F4 — Retry clone reads request content *after* the request was already sent

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs:75,157-165`
- **Detail**: On a successful 401-refresh the handler calls `CloneRequestAsync(request)` *after* `base.SendAsync(request, ...)` has completed, and the clone does `byte[] buffer = await request.Content.ReadAsByteArrayAsync();`. `HttpClient` disposes the request content once the response is received, and non-buffered/one-shot content streams cannot be re-read — so this can throw `ObjectDisposedException` or silently produce an empty body. The bug is **latent today**: the only protected call in this slice is `GET /auth/me`, which has no content. It will fire the first time S-02+ sends a protected `POST`/`PUT` (create event, join request) whose access token has just expired — i.e. it will surface as an intermittent, hard-to-reproduce failure in a later slice.
- **Fix**: Buffer the content before the first send — call `await request.Content.LoadIntoBufferAsync()` at the top of `SendAsync` when `request.Content is not null`, or build the clone before `base.SendAsync` rather than after.
- **Decision**: FIXED — `SendAsync` now snapshots the body bytes and content headers before the first send; `CloneRequestAsync` became the synchronous `CloneRequest(request, bufferedContent, bufferedContentHeaders)` and rebuilds the body from that snapshot.

### F5 — Unplanned `server/` and `shared/` changes despite "No server/API changes"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: `server/Program.cs:103-113`, `shared/ChoNaBojo.Validation/AuthValidation.cs:46,65`
- **Detail**: The plan's "What We're NOT Doing" opens with "**No server/API changes** — F-02 owns the auth endpoints; this slice only consumes them." Two server-side changes landed anyway:
  1. `app.UseHttpsRedirection()` was moved inside an `else` branch so it runs only outside Development, with the justification that the dev certificate's SAN does not cover `10.0.2.2` and would break the emulator's TLS handshake. **This is behaviourally correct** — production still redirects — and was genuinely required to make the client work. It is a legitimate discovery, not sloppiness, but it was never written back into the plan.
  2. `AuthValidation` error copy changed "handle" → "login" in two messages (keys and rules unchanged), aligning the server text with the Register screen's "Communicator login" label.
  Both are low-risk. The issue is that the plan — the ground truth for this and future reviews — still asserts the server was untouched. `server/Program.cs` also picked up unrelated indentation churn (see F9).
- **Fix A ⭐ Recommended**: Add an addendum to `plan.md` recording both changes and the emulator-TLS rationale, and narrow the "No server/API changes" guardrail to "no changes to auth endpoint contracts or business rules".
  - Strength: Preserves correct, necessary work and repairs the source of truth before `/10x-archive` freezes it; the TLS rationale is exactly the kind of discovery a future slice needs.
  - Tradeoff: The plan becomes a slightly moving target after the fact.
  - Confidence: HIGH — the code comment at `server/Program.cs:107-110` already documents the reasoning; this just promotes it to the plan.
  - Blind spot: None significant.
- **Fix B**: Revert the copy change in `AuthValidation` and keep only the HTTPS-redirection fix.
  - Strength: Restores strict scope discipline on the shared project, which two projects depend on.
  - Tradeoff: Re-introduces a mismatch between the server error text ("handle") and the Register screen label ("login") — a worse user-facing outcome.
  - Confidence: MEDIUM — the copy change is arguably an improvement, so reverting trades correctness for process purity.
  - Blind spot: Haven't checked whether any other UI copy references "handle".
- **Decision**: FIXED via Fix A — `plan.md` gained "Addendum A — Post-implementation deviations" (A.1 HTTPS-redirection/emulator-TLS, A.2 validation copy), and the "No server/API changes" bullet is now narrowed to "no changes to auth endpoint contracts or business rules".

### F6 — `SecureStorage` token triple is written non-atomically with no error handling

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/Services/Auth/TokenStore.cs:48-53`
- **Detail**: `SaveAsync` performs three independent `SecureStorage.Default.SetAsync` calls (`auth_access`, `auth_refresh`, `auth_expires`) with no try/catch and no atomicity. If the second or third write fails or the process dies mid-save, the store holds a **mixed triple** — e.g. a new access token beside the *old* refresh token and *old* expiry. `LoadAsync` only checks that all three keys are non-empty and parseable (`TokenStore.cs:28-36`), so it will happily hand that inconsistent session back at next launch. The plan's Critical Implementation Details explicitly hardened the *read* path against `SecureStorage` failure ("Treat any `SecureStorage` read failure as 'no session'") but said nothing about the write path, so this is a gap in the plan as much as in the code.
- **Fix A ⭐ Recommended**: Serialize `AuthSession` to a single JSON string and store it under one key, so the write is atomic by construction.
  - Strength: Eliminates the mixed-triple state entirely rather than trying to detect it; simplifies `LoadAsync` to one read plus one deserialize.
  - Tradeoff: Changes the on-device key layout — existing dev installs will fall back to "no session" and re-login once (harmless; the plan's Migration Notes already accept this).
  - Confidence: HIGH — the plan already states "a returning user with no stored keys is simply routed to Login".
  - Blind spot: None significant.
- **Fix B**: Keep three keys but wrap `SaveAsync` in try/catch and call `ClearAsync()` on any failure.
  - Strength: Minimal diff; preserves the current key layout and the plan's stated key names.
  - Tradeoff: Only covers thrown exceptions — a process kill between writes still leaves a mixed triple.
  - Confidence: MEDIUM — narrows the window, does not close it.
  - Blind spot: Haven't measured how likely a mid-save process kill is on Android in practice.
- **Decision**: FIXED via Fix A — `TokenStore` now stores the whole `AuthSession` as JSON under a single `auth_session` key (atomic write), `SaveAsync` clears the store on write failure. Recorded in `plan.md` Addendum A.3.

### F7 — JSON deserialization failures are unguarded on every API boundary

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `app/ChoNaBojoApp/Services/ApiService.cs:87,94,103`, `app/ChoNaBojoApp/Services/Auth/AuthTokenClient.cs:35`
- **Detail**: Every `ReadFromJsonAsync` call sits inside a try block that catches only `HttpRequestException` and `TaskCanceledException`. A malformed body, an HTML error page from a proxy, or a contract drift throws `JsonException`/`NotSupportedException`, which escapes `ApiService` and faults the `[RelayCommand]` task — an unobserved exception with no user feedback. The plan's stated contract is that "no raw `HttpResponseMessage` leaks to ViewModels" and every outcome maps to a typed result; an escaping `JsonException` breaks that guarantee.
- **Fix**: Add `catch (JsonException)` (and `NotSupportedException`) to the existing catch blocks in `PostAuthAsync`, `GetCurrentUserAsync`, and `AuthTokenClient.RefreshAsync`, mapping to `Unknown()` / `Transient` respectively.
- **Decision**: FIXED — added `catch (Exception ex) when (ex is JsonException or NotSupportedException)` to `GetCurrentUserAsync` → `Unknown()`, `PostAuthAsync` → `Unknown()`, and `AuthTokenClient.RefreshAsync` → `Transient()` (a body we cannot parse must never sign the user out).

### F8 — New code introduces CS0168 build warnings

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `app/ChoNaBojoApp/Services/ApiService.cs:113,117`
- **Detail**: `catch (HttpRequestException ex)` and `catch (TaskCanceledException ex)` declare `ex` but never use it, producing four `warning CS0168` entries in the solution build (once per target framework). The rest of the file's catch blocks correctly omit the identifier (`ApiService.cs:32,67,71`).
- **Fix**: Drop the unused `ex` identifiers, or log them — the surrounding code already uses the identifier-less form.
- **Decision**: FIXED — removed the unused `ex` identifiers from both catch blocks in `PostAuthAsync`.

### F9 — Mixed tab/space indentation introduced in touched files

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `server/Program.cs:17-48,102-118`, `app/ChoNaBojoApp/Services/ApiService.cs:18-36`
- **Detail**: `server/Program.cs` was previously 4-space indented throughout; this change rewrote blocks to a mix of 2-space and tab indentation, producing diff noise in a file whose only intended change was the HTTPS-redirection guard. `ApiService.cs` similarly mixes the pre-existing 2-space style with tab-indented new members. This is cosmetic, but it inflates the diff of an "out of scope" file (F5) and makes future blame/review harder.
- **Fix**: Normalize indentation in the touched blocks to each file's original convention (or add an `.editorconfig` to settle it project-wide).
- **Decision**: SKIPPED — cosmetic only; not worth churning the diff now.

### F10 — Documented deviations from planned contracts were never written back to the plan

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `app/ChoNaBojoApp/ViewModels/LoginViewModel.cs:137-144`, `app/ChoNaBojoApp/Views/LoginPage.xaml.cs:43-57`, `app/ChoNaBojoApp/ViewModels/RegisterViewModel.cs:43-44,184-190`, `app/ChoNaBojoApp/ViewModels/HomeViewModel.cs:32-38`
- **Detail**: Three deliberate, well-commented deviations from the plan's contracts: (1) the plan says the ViewModel calls `INavigationRootService.SetAppRoot()` on success, but the implementation raises a `LoginSucceeded`/`LoggedOut` event and the **page** performs the root swap — the code comment explains this avoids tearing down the page while the command is still flushing `CanExecuteChanged`, which is a sound reason; (2) `CommunicatorHandle` was renamed `CommunicatorLogin` in the VM and UI copy (the wire DTO field is unchanged); (3) the plan's `LoginViewModel` contract lists a "form-level error" property that does not exist — form-level failures go to the snackbar instead. All three are reasonable; none are recorded in the plan, so a future reader will see contradictions between plan and code.
- **Fix**: Add a short "Deviations" addendum to `plan.md` capturing all three with their rationale (fold into the same addendum as F5).
- **Decision**: FIXED — recorded in `plan.md` Addendum A as A.4 (page performs the root swap), A.5 (`CommunicatorHandle` → `CommunicatorLogin`, wire DTO unchanged), and A.6 (no form-level error property; snackbar instead).
