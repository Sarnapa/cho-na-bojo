# Railway Integration & Full Deployment Plan

## Problem Statement

Deploy the ChoNaBojo ASP.NET Core Web API to Railway with auto-deploy on push to `master`, integrate the MAUI mobile app with the deployed API via HttpClient, and set up CI/CD pipelines to produce signed Android AAB (Google Play Internal Testing) and Windows WinUI3 MSIX artifacts on tagged releases.

## Approach

Six sequential phases: Account & secrets prerequisites → Railway backend deployment → mobile-to-API integration → Android CI/CD with Play Store → Windows MSIX CI/CD → edge-case support and fallbacks.

---

## Phase 0: Account Creation & Secrets Configuration (Prerequisites)

### 0.1 Create Railway account ✅
- ~~Sign up at `railway.app` using GitHub OAuth (links directly to your repos)~~
- ~~Select the **Hobby plan** ($5/month) — required for persistent services and custom domains~~
- ~~Verify email~~

### 0.2 Create Supabase account & project ✅
- ~~Sign up at `supabase.com` using GitHub OAuth~~
- ~~Create a new project (region: EU West or closest available)~~
- ~~Note down:~~
  - ~~**Project URL**: `https://<project-ref>.supabase.co`~~
  - ~~**Anon key** (public, safe for client): found in Settings → API~~
  - ~~**Service role key** (server-only, never expose to client): found in Settings → API~~
  - ~~**Connection string** (for direct Postgres access): Settings → Database → Connection string (URI)~~
- ~~Enable Row Level Security (RLS) on all future tables by default~~

### 0.3 Create Google Play Developer account (manual) ✅
- ~~Register at `play.google.com/apps/publish/signup` — **$25 one-time fee**~~
- ~~Complete identity verification (may take 24–48 hours for new personal accounts)~~
- ~~Create the app in Play Console → All apps → Create app~~
- ~~Complete all mandatory dashboard tasks (content rating, privacy policy, etc.)~~
- ~~Set up Internal Testing track: Testing → Internal testing → Create new release~~
- ~~Add tester email addresses~~

### 0.4 Configure GitHub repository secrets ⏳
Navigate to: GitHub repo → Settings → Secrets and variables → Actions → New repository secret

| Secret | Source | Description | Status |
|--------|--------|-------------|--------|
| `RAILWAY_TOKEN` | Railway dashboard → Account → Tokens | For optional CI access to Railway | ✅ |
| `SUPABASE_URL` | Supabase project settings → API | Project API URL | ✅ |
| `SUPABASE_ANON_KEY` | Supabase project settings → API | Public anon key | ✅ |
| `SUPABASE_SERVICE_ROLE_KEY` | Supabase project settings → API | Server-side key (never expose to client) | ✅ |
| `SUPABASE_CONNECTION_STRING` | Supabase project settings → Database | Direct Postgres URI | ✅ |
| `ANDROID_KEYSTORE_BASE64` | Generated in Phase 3.3 | Base64 of `.keystore` file | ⏳ Phase 3 |
| `ANDROID_KEYSTORE_PASSWORD` | Generated in Phase 3.3 | Keystore password | ⏳ Phase 3 |
| `ANDROID_KEY_ALIAS` | Generated in Phase 3.3 | `cho-na-bojo` | ⏳ Phase 3 |
| `ANDROID_KEY_PASSWORD` | Generated in Phase 3.3 | Key password | ⏳ Phase 3 |
| `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | GCP console (Phase 3.6) | Service account JSON for Play uploads | ⏳ Phase 3 |
| `PFX_BASE64` | Generated in Phase 4.2 | Base64 of Windows signing `.pfx` | ⏳ Phase 4 |
| `PFX_PASSWORD` | Generated in Phase 4.2 | PFX password | ⏳ Phase 4 |

> **Note**: Railway and Supabase secrets are configured ✅. Android and Windows signing secrets will be populated as they are generated in Phases 3 and 4.

### 0.5 Configure Railway service environment variables ✅
- ~~Set environment variables via Railway CLI/dashboard~~

---

## Phase 1: Railway Setup & API Deployment

### 1.1 Install Railway CLI (local) ✅
- ~~Install via Scoop: `scoop install railway`~~
- ~~Authenticate: `railway login`~~

### 1.2 Add health-check endpoint to API ✅
- ~~Add `GET /health` endpoint to `server/Program.cs` returning `200 OK` with `{ "status": "healthy" }`~~
- ~~This will be used by Railway's built-in healthcheck and by the mobile app for connectivity verification~~

### 1.3 Create `railway.toml` config ✅
- ~~Place in `server/` directory with build/deploy configuration~~

### 1.4 Connect GitHub repo to Railway ✅
- ~~Install Railway GitHub App~~
- ~~In Railway dashboard: create project → add service → connect `Sarnapa/cho-na-bojo` repo~~
- ~~Set root directory to `server/` in service settings~~
- ~~Set branch to `master` for auto-deploy on push~~
- ~~Configure start command~~

### 1.5 Set environment variables ✅
- ~~Configured in Phase 0.5 — verified via `railway variables list`~~

### 1.6 Generate public domain ✅
- ~~Generated `*.up.railway.app` URL for mobile app configuration~~

### 1.7 Verify deployment ✅
- ~~Confirmed build and health endpoint returning `200 OK`~~

---

## Phase 2: Mobile App ↔ API Integration

### 2.1 Configure HttpClient in MAUI app ✅
- ~~Register `HttpClient` in DI container in `MauiProgram.cs`~~
- ~~Use `IHttpClientFactory` pattern with named client `"ChoNaBojoApi"`~~

### 2.2 Create API service layer ✅
- ~~Create `Services/ApiService.cs` with typed methods calling API endpoints~~
- ~~Create `Services/IApiService.cs` interface for testability~~
- ~~Register in DI~~

### 2.3 Add connectivity check ✅
- ~~On app startup, ping `/health` endpoint~~
- ~~Show offline alert if API unreachable~~
- ~~Handle `HttpRequestException` gracefully~~

### 2.4 Platform-specific base URL configuration ✅
- ~~Use `#if DEBUG` / environment-based switching~~
- ~~Debug (Android emulator): `http://10.0.2.2:5100`~~
- ~~Debug (Windows): `http://localhost:5100`~~
- ~~Release: `https://cho-na-bojo-production.up.railway.app`~~

### 2.5 Remove iOS/macOS targets from `.csproj` ✅
- ~~Updated `TargetFrameworks` to only include `net10.0-android` and `net10.0-windows10.0.19041.0`~~
- ~~Removed iOS/macOS `SupportedOSPlatformVersion` conditions~~
- ~~Removed iOS and MacCatalyst platform folders~~

---

## Phase 3: Android Build & Google Play Internal Testing

### 3.1 Create Google Play Developer account (manual)
- Register at `play.google.com/apps/publish/signup` ($25 one-time fee)
- Complete identity verification

### 3.2 Create app in Google Play Console (manual)
- All apps → Create app → fill required dashboard tasks
- Set up Internal Testing track → add tester email list

### 3.3 Generate Android signing keystore (manual, one-time)
```powershell
keytool -genkeypair -v -keystore cho-na-bojo.keystore -alias cho-na-bojo -keyalg RSA -keysize 2048 -validity 10000
```
- **CRITICAL**: Back up keystore + password securely — cannot be regenerated

### 3.4 Configure `.csproj` for Android release signing
```xml
<PropertyGroup Condition="$(TargetFramework.Contains('-android')) and '$(Configuration)' == 'Release'">
    <AndroidKeyStore>true</AndroidKeyStore>
    <AndroidSigningKeyStore>cho-na-bojo.keystore</AndroidSigningKeyStore>
    <AndroidSigningKeyAlias>cho-na-bojo</AndroidSigningKeyAlias>
    <AndroidPackageFormats>aab</AndroidPackageFormats>
</PropertyGroup>
```

### 3.5 First manual upload to Play Console (mandatory)
- Build locally: `dotnet publish app/ChoNaBojoApp/ChoNaBojoApp.csproj -f net10.0-android -c Release -p:AndroidSigningStorePass=... -p:AndroidSigningKeyPass=...`
- Upload the `*-Signed.aab` to Internal Testing track manually
- Google locks the signing key to this first upload

### 3.6 Set up Google Play Service Account for automation
- Enable Android Publisher API in GCP console
- Create service account → download JSON key
- Invite service account email in Play Console → grant "Release to Internal Testing" permission

### 3.7 Configure GitHub Secrets
| Secret | Description |
|--------|-------------|
| `ANDROID_KEYSTORE_BASE64` | Base64-encoded `.keystore` file |
| `ANDROID_KEYSTORE_PASSWORD` | Keystore password |
| `ANDROID_KEY_ALIAS` | `cho-na-bojo` |
| `ANDROID_KEY_PASSWORD` | Key password |
| `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | GCP service account JSON |

### 3.8 Create GitHub Actions workflow: Android
- File: `.github/workflows/android-deploy.yml`
- Trigger: push tag `v*.*.*` + manual `workflow_dispatch`
- Steps: checkout → setup .NET 10 → install `maui-android` workload → setup Java 17 → decode keystore → build signed AAB → upload to Play Console internal track via `r0adkll/upload-google-play@v1`
- Version code: `10000 + github.run_number` (strictly incrementing)
- Upload AAB as artifact for backup

---

## Phase 4: Windows WinUI3 MSIX Build

### 4.1 Fix `.csproj` for .NET 10 Windows known issues
Add WindowsAppSDK #3337 workaround:
```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows' and '$(RuntimeIdentifierOverride)' != ''">
    <RuntimeIdentifier>$(RuntimeIdentifierOverride)</RuntimeIdentifier>
</PropertyGroup>
```
**CRITICAL**: Use `win-x64` (NOT `win10-x64`) — .NET 10 removed version-specific RIDs (NETSDK1083).

### 4.2 Create self-signed certificate for MSIX (manual, one-time)
```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=ChoNaBojo Dev" `
  -KeyUsage DigitalSignature -FriendlyName "ChoNaBojo Dev Signing" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" `
  -FilePath "cho-na-bojo-dev.pfx" -Password (ConvertTo-SecureString "YourPassword" -AsPlainText -Force)
```

### 4.3 Configure GitHub Secrets for Windows
| Secret | Description |
|--------|-------------|
| `PFX_BASE64` | Base64-encoded `.pfx` certificate |
| `PFX_PASSWORD` | PFX password |

### 4.4 Create GitHub Actions workflow: Windows MSIX
- File: `.github/workflows/windows-deploy.yml`
- Trigger: push tag `v*.*.*` + manual `workflow_dispatch`
- Runner: `windows-latest` (mandatory for MSIX)
- Steps: checkout → setup .NET 10 → install `maui` workload → restore → import PFX cert → build with `RuntimeIdentifierOverride=win-x64` → collect MSIX + public cert → upload as artifact
- Include `INSTALL.txt` with sideloading instructions for testers

---

## Phase 5: Edge Cases, Fallbacks & Support

### 5.1 Railpack .NET 10 fallback — Dockerfile
If Railpack fails to resolve .NET 10, create `server/Dockerfile`:
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
CMD ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet server.dll
```
Set `RAILWAY_DOCKERFILE_PATH=Dockerfile` in Railway service variables.

### 5.2 Railway edge proxy gotchas
- **HTTP→HTTPS redirect converts POST to GET**: Ensure all external webhook senders (Firebase, Supabase) use `https://` URLs only
- **WebSocket 60s idle timeout**: SignalR's 15s keep-alive handles this; if using raw WebSockets, add heartbeat
- **15-minute max request duration**: Long-polling must heartbeat within this window

### 5.3 Android build troubleshooting
- **`gradlew` permission issue on Linux runners**: Add `find . -name "gradlew" -exec chmod +x {} \;` step
- **`env:` prefix not supported for AAB signing**: Pass passwords directly via `"${{ secrets.X }}"` syntax
- **Solution-level publish fails**: Always target `.csproj` directly, never `.sln`/.slnx`
- **Version code collision**: If a re-run collides, manually bump in Play Console or use `run_id` instead of `run_number`

### 5.4 Windows MSIX troubleshooting
- **NETSDK1083 error**: You used `win10-x64` instead of `win-x64` — fix the RID
- **APPX1101 duplicate file**: You used `-r win-x64` directly — must use `-p:RuntimeIdentifierOverride=win-x64` instead
- **MSIX won't install on tester machine**: Tester must import the public `.cer` into `LocalMachine\TrustedPeople` certificate store first
- **App crashes on launch from publish folder**: By-design — MSIX apps must be installed and launched from Start Menu

### 5.5 Railway monitoring & rollback
- Stream logs: `railway logs --filter "@level:error"`
- Rollback: Dashboard → select prior deployment (no CLI command for rollback)
- If build cache corrupts NuGet restore: clear via Railway dashboard

### 5.6 Update `ApplicationId` for production
- Change from `com.companyname.chonabojoapp` to a proper reverse-domain ID (e.g., `com.sarnapa.chonabojo`)
- This must be set **before** the first Play Console upload — it cannot be changed after

---

## Implementation Order & Dependencies

```
Phase 1 (Railway) ──► Phase 2 (API Integration) ──► Phase 3 (Android) ──► Phase 4 (Windows)
                                                          │
Phase 5 (Support) applies across all phases               │
                                                          ▼
                                              Phase 3.5 first requires manual upload
```

## Notes

- Railway auto-deploys on push to `master` — no GitHub Actions needed for the API
- Mobile app CI/CD triggers on version tags (`v*.*.*`) — decoupled from API deploys
- All secrets go in GitHub repository secrets, never in committed files
- The `.gitignore` should exclude `*.keystore`, `*.pfx`, and any signing material
