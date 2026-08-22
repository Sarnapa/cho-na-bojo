# S-01: Registration, Login, and Persistent In-App Session — Implementation Plan

## Overview

Deliver the first user-visible slice: a MAUI Android user can register an account (email + password + at least one shareable contact), log in, stay logged in across app restarts, and log out. All work is client-side — the F-02 auth API (`/auth/register|login|refresh|logout`, `/auth/me`) is already complete and unchanged. The slice also establishes the reusable design-system control styles, MVVM structure, session plumbing, and navigation gating that every later slice (S-02+) builds on.

## Current State Analysis

- **Server (F-02, done):** `server/Auth/AuthEndpoints.cs` exposes `POST /auth/register|login|refresh|logout` (all `AllowAnonymous`, rate-limited under the `"auth"` policy) and `GET /auth/me` (`RequireAuthorization`). Contracts live in `shared/ChoNaBojo.Contracts/DTOs/AuthDTOs.cs`: `RegisterRequest(LoginEmail, Password, ContactPhone?, ContactEmail?, CommunicatorPlatform?, CommunicatorHandle?)`, `LoginRequest(LoginEmail, Password)`, `RefreshRequest(RefreshToken)`, `AuthResponse(AccessToken, RefreshToken, AccessTokenExpiresUtc)`, `CurrentUserResponse(UserId)`. `CommunicatorPlatform` = `Messenger=1 | Instagram=2 | WhatsApp=3`. Access token TTL 15 min, refresh TTL 30 days (`server/Auth/JwtOptions.cs`).
- **Shared validation (reusable by client):** `shared/ChoNaBojo.Validation/AuthValidation.cs` returns a framework-neutral `ValidationResult` (field-keyed error dictionary). Error keys emitted: `loginEmail`, `password`, `contact`, `communicator`, `communicatorPlatform`. `shared/ChoNaBojo.Utils/Text/TextNormalization.cs` provides `IsBasicEmailShape`, `NormalizeOptionalText`. `shared/ChoNaBojo.Contracts/Consts/PasswordPolicy.cs` = MinLength 8 / MaxLength 128.
- **Server error shapes the client must handle:** `400` RFC-7807 `ValidationProblem` with an `errors` object; `401` on bad login credentials and on refresh failure; `409` Conflict on duplicate login email (`{ "message": ... }`) and on refresh `RetryInProgress`.
- **App (baseline / partial):** `app/ChoNaBojoApp/` is MAUI (`net10.0-android`, plus `net10.0-windows` for dev) using UraniumUI 3.0 + Material Symbols. `MauiProgram.cs` registers a named `HttpClient` `"ChoNaBojoApi"` (dev `http://10.0.2.2:5100`, prod Railway URL). `Services/IApiService.cs` + `ApiService.cs` expose **only** `CheckHealthAsync()`. `AppShell.xaml` has a single template `MainPage` (a counter demo). `App.xaml.cs` sets the window to `new AppShell()`. All three shared projects are already referenced by `ChoNaBojoApp.csproj`.
- **Design system:** `Resources/Styles/Colors.xaml` + `Styles.xaml` define the tokens/typography from `context/foundation/ui-guidelines.md` (`PrimaryColor`, `OnPrimaryColor`, `SurfaceColor`, `ErrorColor`, `HeadlineStyle`, `TitleStyle`, `BodyStyle`, `LabelStyle`, `CardShadow`, 8pt grid). **No control styles yet** for `material:TextField` / `material:MaterialButton`.
- **Gaps:** no MVVM toolkit, no ViewModels, no `SecureStorage` use, no auth screens, no token/refresh handling, no navigation gating, no test project.

## Desired End State

A logged-out user launches the app and sees a Login screen (never any app content — the PRD privacy boundary). They can register (providing ≥1 contact), which auto-logs them in and lands them on a minimal Home screen. Killing and relaunching the app keeps them logged in (tokens restored from `SecureStorage`, refreshed transparently). Logout returns them to Login and revokes the refresh-token family server-side. If the refresh token becomes invalid (revoked or > 30 days old), the next API call auto-signs them out to Login with an explaining snackbar. Every screen is built exclusively from UraniumUI Material controls against the design tokens — no hardcoded colors/spacing, spacing on the 8pt grid, buttons as MD3 pills, touch targets ≥ 48.

**Verification:** manual emulator walkthrough (documented in Testing Strategy) covering register → auto-login → restart-persists → logout → login → session-expiry sign-out, plus inline validation and server-error surfacing; the full solution builds (`dotnet build solutions/ChoNaBojo.slnx`) and the Android app builds (`dotnet build app/ChoNaBojoApp -f net10.0-android`).

### Key Discoveries:

- Server issues a token pair on **register** as well as login (`AuthEndpoints.RegisterAsync` returns `AuthResponse`), so auto-login after registration is free — no second round-trip.
- Shared `AuthValidation` is already framework-neutral and reused verbatim by the client for instant inline errors; the server remains authoritative (`shared/ChoNaBojo.Validation/AuthValidation.cs`).
- `CommunicatorPlatform` + `CommunicatorHandle` must be provided **together or not at all** (`AuthValidation` error key `communicator`); at least one of phone / contact-email / (platform+handle) is required (key `contact`).
- The auth endpoints are `AllowAnonymous` and rate-limited; the client must **not** attach a bearer token to `register`/`login`/`refresh` and must avoid recursively refreshing on those calls.
- UraniumUI 3.0 ships **no** Snackbar; `context/foundation/ui-guidelines.md` §9 sanctions CommunityToolkit.Maui for the Snackbar and destructive-confirm dialog.
- The named `HttpClient` `"ChoNaBojoApi"` is the single integration seam — attaching a `DelegatingHandler` there makes bearer+refresh transparent to all current and future services.

## What We're NOT Doing

- **No server/API changes** — F-02 owns the auth endpoints; this slice only consumes them.
- **No map or venue UI** (S-02), **no event creation/listing** (S-03/S-04) — the post-login landing is a deliberate minimal placeholder.
- **No password reset, email verification, OAuth, or "remember me" toggle** — session persistence is always on (token in `SecureStorage`), matching the roadmap.
- **No automated test project** — verification is manual (documented), mirroring F-02. (No test infra exists; adding it is out of scope for this slice.)
- **No iOS code** — Android is the MVP target (Windows head remains dev-only, as today).
- **No Dark theme** — light-theme only per ui-guidelines §1.
- **No editing of the user's profile/contact after registration** — collection happens once at register.

## Implementation Approach

Build bottom-up, mirroring the F-01/F-02 layering:

1. **Foundation (Phase 1)** — dependency-free plumbing with no screens: token storage, session state, typed API client, the bearer/refresh `DelegatingHandler`, the root-swap navigation service, DI wiring, and the reusable design-system control styles.
2. **Sign-in path (Phase 2)** — Login screen + the unauthenticated Auth flow + a minimal Home + optimistic-restore startup routing. After this phase the gating and persistence work end-to-end against a manually created account.
3. **Sign-up path (Phase 3)** — Register screen with the contact-collection UX, reusing the shared validation and the same landing via auto-login.
4. **Lifecycle end (Phase 4)** — logout and session-expiry auto-sign-out.

MVVM via CommunityToolkit.Mvvm (`ObservableObject` / `[ObservableProperty]` / `[RelayCommand]`). Transient feedback + confirm dialog via CommunityToolkit.Maui. Screens consume the new control styles and design tokens exclusively.

## Critical Implementation Details

- **Refresh handler must not recurse or leak bearers.** The `DelegatingHandler` on `"ChoNaBojoApi"` attaches the bearer only when a session exists **and** the request is not an anonymous auth call (`/auth/register`, `/auth/login`, `/auth/refresh`, `/auth/logout`). The refresh and server-logout calls are issued through a **second, un-handled named client** (`"ChoNaBojoAuth"`, same `BaseAddress`, no `AddHttpMessageHandler`) exposed as `IAuthTokenClient`, so a 401 during refresh cannot trigger another refresh — and so the handler never depends on `IApiService` (which resolves the handled client and would otherwise recurse at construction).
- **Single-flight refresh + reuse-detection interaction.** Guard refresh with a `SemaphoreSlim(1,1)`. On a 401: acquire the lock, then re-check whether the stored access token already changed (another request refreshed while we waited) — if so, retry with the new token without calling `/auth/refresh`. Only the first waiter calls `/auth/refresh`. If refresh returns `409 RetryInProgress`, **do not re-call `/auth/refresh`** — the server has already consumed that token and never returns the replacement's raw value (`server/Auth/RefreshTokenService.cs:111-131`), so a retry returns 409 for the 20 s grace window and then trips `RevokeFamilyAsync` → `ReuseDetected`, signing the user out on every device. Instead, re-read the token store: if the stored pair changed (another process refreshed), retry the original request with it; if unchanged, treat the session as unrecoverable and sign out locally. If refresh returns `401`, treat as session-expired (do **not** loop). This prevents concurrent 401s from each rotating the token and tripping the server's family-reuse revocation.
- **Refresh-failure classification (never sign out on a transient failure).** The handler must bucket the refresh outcome explicitly: `401`/invalid-token → session expired, sign out; `409 RetryInProgress` → per the rule above; `429`, `5xx`, or transport/timeout → **keep the session**, propagate the original call's failure to the caller as a network error (retry snackbar), and do not navigate. The `/auth` group is rate limited to 10 req/min per IP with `QueueLimit = 0` (`server/Program.cs:83-91`) and the emulator shares one partition, so 429s are expected during manual testing — treating them as expiry would destroy valid sessions.

- **`SecureStorage` at startup on Android 10+.** Read stored tokens on the main thread during bootstrap (`MainThread.IsMainThread` must be true / marshal via `MainThread.InvokeOnMainThreadAsync`); a background-thread first access can throw on some Android 10+ devices. Treat any `SecureStorage` read failure as "no session" (route to Login) rather than crashing.
- **ValidationProblem key → field mapping.** The client deserializes the server `400` body into a lightweight problem-details record (`{ errors: Dictionary<string,string[]> }`) and maps the shared keys (`loginEmail`→email field, `password`→password field, `contact`/`communicator`/`communicatorPlatform`→the contact group) onto the ViewModel's per-field error properties, so server and client errors render identically.
- **Root swap must clear the back-stack.** Swapping the window root between the Auth `NavigationPage` and the App Shell (login/register success, logout, expiry) must replace the root outright so a logged-out user can never navigate back into app content (privacy boundary).

## Phase 1: Session & API foundation + design-system control styles

### Overview

Establish all non-visual plumbing and the reusable Material control styles. No screens change behavior yet; the app still boots. This phase makes Phases 2–4 thin.

### Changes Required:

#### 1. Dependencies & MAUI wiring

**File**: `app/ChoNaBojoApp/ChoNaBojoApp.csproj`, `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Add CommunityToolkit.Mvvm (ViewModels) and CommunityToolkit.Maui (Snackbar + confirm dialog); initialize the toolkit.

**Contract**: Two `PackageReference`s at net10-compatible versions; `.UseMauiCommunityToolkit()` added to the `MauiProgram` builder chain alongside the existing `.UseUraniumUI*()` calls.

#### 2. Client session model + token store

**File**: `app/ChoNaBojoApp/Services/Auth/ITokenStore.cs`, `Services/Auth/TokenStore.cs`, `Services/Auth/AuthSession.cs`

**Intent**: Persist and retrieve the token pair from `SecureStorage`; model the in-memory session.

**Contract**: `AuthSession(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresUtc)` (a plain client record; may wrap/reuse the `AuthResponse` DTO fields). `ITokenStore` with `Task<AuthSession?> LoadAsync()`, `Task SaveAsync(AuthSession)`, `Task ClearAsync()`. `TokenStore` uses `SecureStorage.Default` keys (e.g. `auth_access`, `auth_refresh`, `auth_expires`); reads tolerate failure by returning `null`. No secrets logged.

#### 3. Session/auth-state service

**File**: `app/ChoNaBojoApp/Services/Auth/ISessionService.cs`, `Services/Auth/SessionService.cs`

**Intent**: Own the current session as the single source of truth for "am I signed in", expose the current access/refresh tokens to the handler, and raise an event when the session ends unexpectedly.

**Contract**: `ISessionService` with `AuthSession? Current { get; }`, `bool IsAuthenticated`, `Task InitializeAsync()` (loads from `ITokenStore`), `Task SetAsync(AuthSession)` (persist + set current), `Task SignOutAsync(bool revokeServer)` (optional `/auth/logout` via `IAuthTokenClient` + clear + null current), and an `event EventHandler SessionExpired`. Registered as a singleton. Depends on `ITokenStore` + `IAuthTokenClient` only — never on `IApiService`.

#### 4. Typed auth API client

**File**: `app/ChoNaBojoApp/Services/IApiService.cs`, `Services/ApiService.cs`, `Services/Auth/IAuthTokenClient.cs`, `Services/Auth/AuthTokenClient.cs`

**Intent**: Add register/login/logout/current-user calls returning typed results that carry either success or mapped field/message errors — no raw `HttpResponseMessage` leaks to ViewModels. Keep refresh/server-logout on a separate, handler-free client so the handler has no dependency on `IApiService`.

**Contract**: New methods `Task<AuthResult> RegisterAsync(RegisterRequest, CancellationToken)`, `Task<AuthResult> LoginAsync(LoginRequest, CancellationToken)`, `Task<CurrentUserResult> GetCurrentUserAsync(CancellationToken)` (calls the protected `GET /auth/me`; returns `Success(CurrentUserResponse)`, `Unauthorized`, or `Network/Unknown`). `IAuthTokenClient` (separate file, uses the un-handled `"ChoNaBojoAuth"` client) exposes `Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken)` and `Task LogoutAsync(string refreshToken, CancellationToken)`; `RefreshOutcome` distinguishes `Success(AuthResponse)`, `RetryInProgress` (409), `Invalid` (401), and `Transient` (429/5xx/transport). Introduce a client result type `AuthResult` distinguishing `Success(AuthResponse)`, `ValidationFailed(IReadOnlyDictionary<string,string[]>)`, `Unauthorized`, `Conflict(string message)`, and `Network/Unknown`. A small `ValidationProblemResponse(Dictionary<string,string[]> Errors)` record is deserialized from the server `400` body. Reuses the shared `AuthDTOs` for the wire payloads.

#### 5. Bearer + single-flight refresh DelegatingHandler

**File**: `app/ChoNaBojoApp/Services/Auth/AuthenticatingHttpMessageHandler.cs`, wired in `MauiProgram.cs`

**Intent**: Transparently attach the bearer to protected calls and refresh once on 401, retrying the original request; raise session-expired when refresh fails.

**Contract**: A `DelegatingHandler` registered on the `"ChoNaBojoApi"` named client via `AddHttpMessageHandler`, depending **only** on `ITokenStore`/`ISessionService` + `IAuthTokenClient` (never `IApiService` — that would recurse through `CreateClient("ChoNaBojoApi")`). Skips bearer attach + refresh for the anonymous auth endpoints. **Pre-flight expiry check**: before sending a protected request, if `AuthSession.AccessTokenExpiresUtc` is within 60 s (or already past), refresh first through the same single-flight path rather than waiting for a 401 — this is the single place the 15-min expiry is enforced and the reason the `auth_expires` key is persisted. Refresh guarded by `SemaphoreSlim(1,1)` with the change-detection + `409 RetryInProgress`/`401`/transient handling described in Critical Implementation Details. On unrecoverable refresh failure it calls `ISessionService.SignOutAsync(revokeServer:false)` semantics and triggers `SessionExpired`. The refresh HTTP call goes through the un-handled `"ChoNaBojoAuth"` client, so it bypasses this handler by construction.

#### 6. Root-swap navigation service

**File**: `app/ChoNaBojoApp/Services/Navigation/INavigationRootService.cs`, `Services/Navigation/NavigationRootService.cs`

**Intent**: Centralize swapping the window root between the Auth flow and the App Shell so login/register/logout/expiry share one mechanism and the back-stack is always replaced.

**Contract**: `INavigationRootService` with `void SetAuthRoot()` and `void SetAppRoot()`, each replacing `Application.Current.Windows[0].Page` (Auth `NavigationPage` hosting `LoginPage` vs the App `AppShell`) on the main thread. Registered singleton; consumed by `App`, the auth ViewModels, and the expiry handler.

#### 7. Reusable design-system control styles

**File**: `app/ChoNaBojoApp/Resources/Styles/Styles.xaml`

**Intent**: Add token-driven styles for the Material controls the auth screens use, so ui-guidelines rules (pill buttons `CornerRadius=24`, ≥48 touch targets, `PrimaryColor`/`OnPrimaryColor`, `SurfaceColor`, 8pt spacing, no hardcoded values) are enforced centrally and reused by later slices.

**Contract**: Keyed styles: a `material:TextField` style (min height 48, `PrimaryColor` accent, `ErrorColor` validation, `DividerColor` border); a `PrimaryButtonStyle` and `SecondaryButtonStyle` targeting the **MAUI `Button`** (`PrimaryColor` bg / `OnPrimaryColor` text and transparent bg / `PrimaryColor` text respectively, `CornerRadius=24`, `HeightRequest=48`). UraniumUI 3.0 ships **no** `MaterialButton` and **no** `TextButton` style class — verified against the restored `UraniumUI.Material.dll` — so the guideline's naming is wrong and is corrected as part of this step. All values reference existing `{StaticResource ...}` tokens — no literals. (Section E chips / cards are out of scope here.)

**Also update**: `context/foundation/ui-guidelines.md` §6.B (and the `MaterialButton` mentions in §6.C) to name MAUI `Button` + these keyed styles, so later slices don't inherit the same wrong control name.

#### 8. DI registration

**File**: `app/ChoNaBojoApp/MauiProgram.cs`

**Intent**: Register the new services, handler, ViewModels, and pages.

**Contract**: `ITokenStore`/`TokenStore`, `ISessionService`/`SessionService`, `INavigationRootService`/`NavigationRootService` as singletons; a second named client `"ChoNaBojoAuth"` (same `BaseAddress` as `"ChoNaBojoApi"`, sharing one base-URL constant, **no** message handler) backing `IAuthTokenClient`/`AuthTokenClient`; `AuthenticatingHttpMessageHandler` added to the `"ChoNaBojoApi"` client only; ViewModels + pages (added in later phases) registered transient. Existing `IApiService` singleton retained.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`
- No hardcoded color/spacing literals introduced in `Styles.xaml` (grep for `#`, non-8pt numeric spacing in the added styles)

#### Manual Verification:

- App still launches on the Android emulator without regression (existing behavior intact)
- `SecureStorage` round-trip works: a temporary debug write/read returns the same value (or verified via the Phase 2 flow)

**Implementation Note**: After this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Login + Auth flow + Home + startup gating

### Overview

Deliver the sign-in path end-to-end: the unauthenticated Auth flow with a Login screen, a minimal authenticated Home, and optimistic-restore startup routing. After this phase, gating and persistence are demonstrable against a manually created account.

### Changes Required:

#### 1. Login ViewModel

**File**: `app/ChoNaBojoApp/ViewModels/LoginViewModel.cs` (+ `ViewModels/ViewModelBase.cs`)

**Intent**: Drive the login form: email/password inputs, inline validation via shared `AuthValidation`, a login command that calls the API and maps results, and navigation to Register.

**Contract**: `ObservableObject` with `[ObservableProperty]` for `LoginEmail`, `Password`, per-field error strings, `IsBusy`, and a form-level error. `[RelayCommand] LoginAsync` runs `AuthValidation.ValidateLoginRequest` first, then `IApiService.LoginAsync`; on `Success` → `ISessionService.SetAsync` + `INavigationRootService.SetAppRoot()`; on `Unauthorized` → snackbar "Invalid email or password"; on `ValidationFailed` → map keys to fields; on network error → snackbar. `[RelayCommand] GoToRegister`.

#### 2. Login page (design-system)

**File**: `app/ChoNaBojoApp/Views/LoginPage.xaml` (+ `.xaml.cs`)

**Intent**: Render the login form using UraniumUI Material controls and the new styles.

**Contract**: `material:TextField` for email (Email keyboard) and password (`IsPassword` + `TextFieldPasswordShowHideAttachment` show/hide toggle), a `PrimaryButtonStyle` `Button` (Login), a `SecondaryButtonStyle` `Button` link to Register. Root `VerticalStackLayout`/`Grid` with `Margin=16`, 8pt spacing, `HeadlineStyle` title. Inline field errors bound to the VM; loading state via `ActivityIndicator` tinted `PrimaryColor` (ui-guidelines §10). `SemanticProperties.Description` on interactive controls.

#### 3. Minimal Home placeholder

**File**: `app/ChoNaBojoApp/Views/HomePage.xaml` (+ `.xaml.cs`), `app/ChoNaBojoApp/ViewModels/HomeViewModel.cs`, `AppShell.xaml`, `MauiProgram.cs`

**Intent**: Replace the counter `MainPage` as the authenticated landing with a small, on-brand placeholder that S-02 will later replace with the map, and host the (Phase 4) logout affordance.

**Contract**: `HomePage` shows a `HeadlineStyle` welcome and a spot for logout, built from tokens only. `AppShell.xaml`'s single `ShellContent` points to `HomePage` (counter `MainPage` no longer referenced). If `MainPage.xaml`/`.cs` is deleted, its `AddTransient<MainPage>()` registration in `MauiProgram.cs:36` must be removed in the same step or the build breaks. `HomeViewModel` registered transient. On appearing, `HomeViewModel` calls `IApiService.GetCurrentUserAsync` — the slice's one protected call, which exercises bearer attach, transparent 401 refresh, and expiry sign-out. It does **not** block the UI: `Success` is silent (Home renders regardless), `Unauthorized` is handled by the handler's expiry path, and a network failure surfaces a non-blocking snackbar without navigating.

#### 4. Auth flow container + startup routing

**File**: `app/ChoNaBojoApp/App.xaml.cs`, `app/ChoNaBojoApp/Views/LoadingPage.xaml` (+ `.xaml.cs`), `MauiProgram.cs`

**Intent**: On launch, initialize the session and route to the correct root (optimistic restore); define the Auth flow root as a `NavigationPage` hosting `LoginPage`.

**Contract**: `CreateWindow` stays synchronous and returns `new Window(new LoadingPage())` — a token-styled page with a `PrimaryColor` `ActivityIndicator`. `LoadingPage`'s `Loaded` handler (main thread, `async`, wrapped in try/catch so no unobserved async-void exception can crash the app) awaits `ISessionService.InitializeAsync()` (main-thread `SecureStorage` read), then calls `INavigationRootService.SetAppRoot()` if a refresh token exists, else `SetAuthRoot()`; **any** exception or `SecureStorage` failure routes to `SetAuthRoot()`. Because the swap happens after `CreateWindow` has returned, `Application.Current.Windows[0]` is guaranteed to exist. Register `LoadingPage`, `LoginPage`/`LoginViewModel`, and `HomePage`/`HomeViewModel` in DI.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`

#### Manual Verification:

- Cold launch with no stored session shows the Login screen (no app content visible)
- Logging in with a valid manually-created account lands on Home
- Invalid credentials show an "Invalid email or password" snackbar; empty/invalid fields show inline errors
- Kill and relaunch the app after login → user stays on Home (optimistic restore; token refreshed transparently on first protected call)
- Home's `GET /auth/me` call succeeds with an attached bearer (verified via server log / no error snackbar)
- Screens visually conform to ui-guidelines (pill buttons, 48 touch targets, tokens, 8pt spacing) — spot-checked

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: Register + contact collection + auto-login

### Overview

Add the sign-up path: a Register screen collecting email, password, and ≥1 contact, reusing the shared validation and landing the new user on Home via auto-login.

### Changes Required:

#### 1. Register ViewModel

**File**: `app/ChoNaBojoApp/ViewModels/RegisterViewModel.cs`

**Intent**: Drive the register form, including the "at least one contact" rule and the paired messenger platform+handle, with inline + server-mapped errors and auto-login on success.

**Contract**: `ObservableObject` with `[ObservableProperty]` for `LoginEmail`, `Password`, `ContactPhone`, `ContactEmail`, `CommunicatorPlatform?` (selected enum), `CommunicatorHandle`, per-field/group error strings, `IsBusy`. `[RelayCommand] RegisterAsync` runs `AuthValidation.ValidateRegisterRequest` (surfacing `contact`/`communicator`/`communicatorPlatform` group errors), then `IApiService.RegisterAsync`; on `Success` → `ISessionService.SetAsync` + `INavigationRootService.SetAppRoot()` (auto-login); on `Conflict` → email field error "An account with this email already exists"; on `ValidationFailed` → map keys to fields/group. `[RelayCommand] GoToLogin`.

#### 2. Register page (design-system, contact collection)

**File**: `app/ChoNaBojoApp/Views/RegisterPage.xaml` (+ `.xaml.cs`)

**Intent**: Render the register form with all three contact methods optional and a clear "at least one required" affordance.

**Contract**: `material:TextField` for email + password (show/hide); a contact section with `material:TextField` for phone (Telephone keyboard) and contact-email (Email keyboard), plus a messenger row = a Uranium picker for `CommunicatorPlatform` (Messenger/Instagram/WhatsApp) + a handle `TextField`. Helper text "Provide at least one contact method"; group error bound to the VM. `PrimaryButtonStyle` `Button` (Create account), `SecondaryButtonStyle` `Button` link to Login. Tokens/8pt spacing/48 targets throughout; `SemanticProperties` set.

#### 3. Register routing wiring

**File**: `app/ChoNaBojoApp/MauiProgram.cs`, `App.xaml.cs`/auth flow

**Intent**: Register the page/VM and enable Login↔Register navigation within the Auth `NavigationPage`.

**Contract**: `RegisterPage`/`RegisterViewModel` registered transient; `GoToRegister`/`GoToLogin` push/pop within the Auth `NavigationPage`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`

#### Manual Verification:

- Registering with email + password + exactly one contact succeeds and auto-lands on Home
- Submitting with no contact method shows the "at least one contact" group error (matches server)
- Selecting a messenger platform without a handle (or vice-versa) shows the paired `communicator` error
- Registering an already-used email shows the duplicate-email error on the email field (server 409)
- After register+auto-login, relaunching the app keeps the user signed in
- Register screen conforms to ui-guidelines — spot-checked

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: Logout + session-expiry lifecycle

### Overview

Complete the session lifecycle: an explicit logout from Home and an automatic sign-out when the refresh token is no longer valid.

### Changes Required:

#### 1. Logout action

**File**: `app/ChoNaBojoApp/ViewModels/HomeViewModel.cs`, `app/ChoNaBojoApp/Views/HomePage.xaml`

**Intent**: Let the user log out with an MD3 confirm, revoking the refresh family server-side and returning to Login.

**Contract**: A logout control on `HomePage` (design-system). `[RelayCommand] LogoutAsync` shows a CommunityToolkit.Maui destructive confirm (SurfaceColor dialog, ErrorColor confirm per ui-guidelines §9); on confirm → `ISessionService.SignOutAsync(revokeServer:true)` (calls `POST /auth/logout` with the current refresh token, then clears `SecureStorage`) → `INavigationRootService.SetAuthRoot()`.

#### 2. Session-expiry auto-sign-out

**File**: `app/ChoNaBojoApp/App.xaml.cs` (or a dedicated coordinator), consuming `ISessionService.SessionExpired`

**Intent**: When the DelegatingHandler's refresh fails, sign the user out to Login with an explanation instead of leaving them on a broken screen.

**Contract**: Subscribe to `ISessionService.SessionExpired`; on the event (marshalled to the main thread) clear the session, call `SetAuthRoot()`, and show a snackbar "Your session expired — please sign in." Ensure exactly one sign-out occurs even if multiple calls fail concurrently (idempotent via the session state).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`

#### Manual Verification:

- Logout shows the MD3 confirm dialog; confirming returns to Login and the back button cannot re-enter Home
- After logout, relaunching the app shows Login (session cleared; server refresh family revoked)
- Forcing an invalid refresh (e.g., logout on another session / wait out the refresh, or a debug-invalidated refresh token) triggers auto-sign-out to Login with the "session expired" snackbar on the next protected call
- Concurrent failing calls result in a single sign-out (no duplicate navigations)
- Airplane mode (or a 429 from the auth rate limit) during a protected call shows a retry snackbar and does **not** log the user out
- With the access token expired but the refresh valid, entering Home refreshes transparently and `GET /auth/me` succeeds without user-visible interruption

**Implementation Note**: Pause for manual confirmation; this is the final phase.

---

## Testing Strategy

Manual-only, mirroring F-02 — no automated test project is added in this slice.

### Manual Testing Steps (Android emulator):

1. Fresh install / cleared storage → app opens on **Login** (no app content visible).
2. Tap **Register** → submit with no contact → group error appears. Add one contact (phone, email, or messenger platform+handle) → submit → auto-login → **Home**.
3. Re-register the same email → duplicate-email error on the email field.
4. Kill the app, relaunch → still on **Home** (session restored, token refreshed on first call).
5. **Logout** → confirm dialog → back to **Login**; back button cannot return to Home; relaunch → Login.
6. **Login** with the account → **Home**. Bad password → snackbar.
7. Invalidate the refresh token server-side (or wait out TTL / logout elsewhere) → next protected call → auto-sign-out to Login + "session expired" snackbar.
8. Throughout: inline validation matches server rules; every screen conforms to ui-guidelines (tokens, 8pt spacing, pill buttons, ≥48 targets, no hardcoded values).

### Edge cases to exercise manually:

- Offline at launch with a stored session (optimistic restore shows app; first call surfaces a network snackbar, not a crash).
- Auth rate limit (10 req/min per IP) tripped by a burst of manual login/register attempts → 429 surfaces as a retry snackbar, never as a sign-out.
- Concurrent protected calls right after the 15-min access token expires (single refresh, both succeed).
- `SecureStorage` read failure at startup → routed to Login, no crash.

## Performance Considerations

- Startup uses **optimistic restore** — no blocking network call on cold start; the first protected call performs the (cached-cheap) refresh if needed.
- Single-flight refresh avoids redundant `/auth/refresh` calls (and avoids tripping server reuse-detection) under concurrent 401s.

## Migration Notes

- No data migration. Client-side only. `SecureStorage` keys are new; a returning user with no stored keys is simply routed to Login.
- The counter `MainPage` is superseded by `HomePage`; removing it is optional cleanup — if removed, drop its `AddTransient<MainPage>()` line in `MauiProgram.cs` too.

## References

- Roadmap slice: `context/foundation/roadmap.md` → S-01 (account-and-session)
- Design system (authoritative for all XAML): `context/foundation/ui-guidelines.md`
- Server auth contract: `server/Auth/AuthEndpoints.cs`, `shared/ChoNaBojo.Contracts/DTOs/AuthDTOs.cs`, `shared/ChoNaBojo.Validation/AuthValidation.cs`, `server/Auth/JwtOptions.cs`
- Prior slice brief: `context/archive/2026-07-15-auth-scaffold/plan-brief.md`
- Lessons (shared-code placement, enum guarding): `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Session & API foundation + design-system control styles

#### Automated

- [x] 1.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — 8b1569f
- [x] 1.2 Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android` — 8b1569f
- [x] 1.3 No hardcoded color/spacing literals introduced in `Styles.xaml` — 8b1569f

#### Manual

- [x] 1.4 App still launches on the Android emulator without regression — 8b1569f
- [x] 1.5 `SecureStorage` round-trip works — d7aeba5

### Phase 2: Login + Auth flow + Home + startup gating

#### Automated

- [x] 2.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx` — d7aeba5
- [x] 2.2 Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android` — d7aeba5

#### Manual

- [x] 2.3 Cold launch with no stored session shows Login (no app content visible) — d7aeba5
- [x] 2.4 Valid login lands on Home — d7aeba5
- [x] 2.5 Invalid credentials → snackbar; invalid fields → inline errors — d7aeba5
- [x] 2.6 Relaunch after login keeps the user on Home (optimistic restore) — d7aeba5
- [x] 2.7 Screens conform to ui-guidelines (spot-checked) — d7aeba5
- [x] 2.8 Home's `GET /auth/me` call succeeds with an attached bearer — d7aeba5

### Phase 3: Register + contact collection + auto-login

#### Automated

- [x] 3.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [x] 3.2 Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`

#### Manual

- [x] 3.3 Register with ≥1 contact succeeds and auto-lands on Home
- [x] 3.4 No contact method → group error (matches server)
- [x] 3.5 Messenger platform/handle mismatch → paired `communicator` error
- [x] 3.6 Duplicate email → email-field error (server 409)
- [x] 3.7 Relaunch after register+auto-login keeps the user signed in
- [x] 3.8 Register screen conforms to ui-guidelines (spot-checked)

### Phase 4: Logout + session-expiry lifecycle

#### Automated

- [ ] 4.1 Solution builds: `dotnet build solutions/ChoNaBojo.slnx`
- [ ] 4.2 Android app builds: `dotnet build app/ChoNaBojoApp -f net10.0-android`

#### Manual

- [ ] 4.3 Logout shows MD3 confirm; confirming returns to Login and blocks back-nav
- [ ] 4.4 After logout, relaunch shows Login (session cleared + family revoked)
- [ ] 4.5 Invalid refresh → auto-sign-out to Login + "session expired" snackbar
- [ ] 4.6 Concurrent failing calls result in a single sign-out
- [ ] 4.7 Airplane mode / 429 during a protected call → retry snackbar, session preserved
- [ ] 4.8 Expired access token + valid refresh → transparent refresh, `GET /auth/me` succeeds uninterrupted
