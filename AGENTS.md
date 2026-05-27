# Command Execution and Permissions Policy (Security Policy)

As an AI assistant operating in the CLI, you must strictly adhere to the following Access Control Lists (Allow / Ask / Deny) regarding file operations and proposed shell (Bash) commands.

## ALLOW
You have full permission to Read, Edit, and Write files. You may also freely propose the following commands without any additional warnings:
- **Node/NPM:** `npm *`, `npx *`, `node *`
- **Git (Local):** `git add *`, `git commit *`, `git diff *`, `git log *`, `git status *`, `git branch *`, `git checkout *`, `git stash *`

## ASK (Requires Warning)
If your solution requires the use of the following commands, you **MUST** include a clear, explicit text warning in your response before generating the command, notifying me about the network risk or remote modification:
- **Network:** `curl *`, `wget *`
- **Git (Remote):** `git push`, `git push *`

## DENY (Strictly Prohibited)
**NEVER, under any circumstances**, propose or generate the following commands in your responses. If my query necessitates their use, firmly refuse and state that the repository's security policy explicitly forbids this action.
- **Deletion:** `rm -rf *`

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
- PascalCase for public members, types, and file names; camelCase for locals and parameters.
- One type per file; file name matches type name (e.g. `WeatherForecast` lives in `WeatherForecast.cs`).
- XAML pages follow the pattern: `PageName.xaml` paired with `PageName.xaml.cs` code-behind.

## Commit & PR Guidelines

- Short imperative or descriptive commit messages; no enforced prefix convention yet.
- Target branch: `master`.

## Architecture Notes

- The API uses minimal APIs (top-level `Program.cs` with `MapGet`/`MapPost` route handlers), not MVC controllers.
- Planned data layer: PostgreSQL. Push notifications: Firebase Cloud Messaging.
- Deployment target: Google Cloud Run (containerized), auto-deploy via GitHub Actions (not yet configured).
- Background jobs will handle event lifecycle such as auto-closing past-end-time events.
