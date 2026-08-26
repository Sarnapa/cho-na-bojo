# Repository Guidelines

ChoNaBojo is a sports-matchmaking mobile app connecting recreational athletes with nearby players at local venues. The stack is C# end-to-end: .NET MAUI (Android-only MVP) for the mobile client and ASP.NET Core Web API (.NET 10) for the backend, with PostgreSQL planned for persistence.

## Hard Rules

- Never expose user contact info (phone, email, messenger) to unauthenticated users or unapproved event participants — this is a core privacy boundary.
- Do not add iOS targets or platform-specific iOS code — the MVP is Android-only.
- Do not create or modify files under `context/archive/` — archived content is immutable.
- Secrets and connection strings belong in user-secrets or environment variables, never committed to `appsettings.json`.

## Project Structure

- `solutions/ChoNaBojo.slnx` — solution file (open in Visual Studio or Rider)
- `app/ChoNaBojoApp/` — .NET MAUI mobile client (Android target)
- `server/` — ASP.NET Core Web API (net10.0)
- `context/foundation/` — PRD, tech-stack decisions, shape notes

Feature requirements and architectural decisions: @context/foundation/prd.md and @context/foundation/tech-stack.md.

## Build & Development Commands

- `dotnet build solutions/ChoNaBojo.slnx` — build entire solution
- `dotnet run --project server` — start API at http://localhost:5100
- `dotnet build app/ChoNaBojoApp -f net10.0-android` — build MAUI Android app
- `dotnet test` — run tests (when test projects are added)

## Coding Style & Naming

- C# with nullable reference types enabled and implicit usings — do not disable either setting.

## Commit & PR Guidelines

- Short imperative or descriptive commit messages; no enforced prefix convention yet.
- Target branch: `master`.

## Architecture Notes

- The API uses minimal APIs (top-level `Program.cs` with `MapGet`/`MapPost` route handlers).
- Background jobs will handle event lifecycle such as auto-closing past-end-time events.
- Full stack decisions (database, push notifications, deployment target): @context/foundation/tech-stack.md.

## Google Maps API key

The Android app needs a Google Maps API key to render the map (S-02+):

1. In Google Cloud Console, enable **Maps SDK for Android** on a project with billing enabled.
2. Create a development API key restricted to package `com.cho_na_bojo` and your Android debug certificate SHA-1, obtained with:
   `keytool -list -v -alias androiddebugkey -keystore "$env:LOCALAPPDATA\Xamarin\Mono for Android\debug.keystore" -storepass android -keypass android`
   (.NET for Android keeps its debug keystore under `%LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore` on Windows — **not** the plain-Android-tooling path `~/.android/debug.keystore`, which .NET MAUI never creates. If that file doesn't exist yet, build/deploy the Android app once first to generate it.)
3. Place the key in `secrets/maps.props` (gitignored, never committed):
   ```xml
   <Project>
     <PropertyGroup>
       <GoogleMapsApiKey>YOUR_DEV_KEY</GoogleMapsApiKey>
     </PropertyGroup>
   </Project>
   ```
4. CI uses a separate production key restricted to the same package plus the release signing certificate SHA-1, stored as the `GOOGLE_MAPS_API_KEY` GitHub repository secret and passed via `-p:GoogleMapsApiKey=...` in `.github/workflows/android-deploy.yml`.

A fresh clone without `secrets/maps.props` still builds — the key placeholder resolves to empty and the map renders blank instead of failing.
