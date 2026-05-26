---
bootstrapped_at: 2026-05-27T01:00:19+02:00
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: cho-na-bojo
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: cho-na-bojo
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: google-cloud-run
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: true
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: true
```

### Why this stack

Solo developer building a mobile sports-matchmaking app (Android-only MVP, 3-week after-hours timeline) with .NET MAUI for the mobile frontend and ASP.NET Core Web API for the backend. C# provides end-to-end type safety across both projects, and the .NET ecosystem's official templates (dotnet new webapi, dotnet new maui) give convention-based scaffolding that an AI agent can navigate predictably. The dotnet starter passes all four agent-friendly gates and has verified bootstrapper confidence for the API project; MAUI scaffolding is manual but follows Microsoft's well-documented official template. PostgreSQL backs the data layer; Firebase Cloud Messaging handles push notifications; background jobs manage event lifecycle (auto-closing past-end-time events). Deployment targets Google Cloud Run (containerized API) with GitHub Actions auto-deploying on merge.

## Pre-scaffold verification

| Signal | Value | Severity | Notes |
| --- | --- | --- | --- |
| npm package | not run | — | non-JS starter; npm recency check not applicable |
| GitHub repo | not run | — | docs_url (learn.microsoft.com/aspnet/core) is not a GitHub repo URL; no pushed_at signal available |

No recency signal available for this starter. The .NET SDK templates are bundled with the SDK itself and updated on SDK release cadence.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n server --no-restore`
**Strategy**: subdir-then-move (adapted — user requested project in `server/` subdirectory; no move-up needed)
**Exit code**: 0
**Files created**: 6 (Program.cs, server.csproj, server.http, appsettings.json, appsettings.Development.json, Properties/launchSettings.json)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (dotnet new webapi does not emit .gitignore)
**Cleanup**: not applicable (scaffolded directly into target `server/` subdirectory per user request)

User override: project scaffolded into `server/` subdirectory (instead of cwd root) to support a monorepo layout with `server/` (API) and `app/` (MAUI mobile) side by side.

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: no findings to distinguish

Clean dependency tree. No known vulnerabilities in the scaffolded project's packages.

## Hints recorded but not acted on

| Hint | Value |
| --- | --- |
| bootstrapper_confidence | verified |
| quality_override | false |
| path_taken | custom |
| self_check_answers | typed: true, from_official_starter: true, conventions: true, docs_current: true, can_judge_agent: true |
| team_size | solo |
| deployment_target | google-cloud-run |
| ci_provider | github-actions |
| ci_default_flow | auto-deploy-on-merge |
| has_auth | true |
| has_payments | false |
| has_realtime | false |
| has_ai | false |
| has_background_jobs | true |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep.
- Address audit findings per your project's risk tolerance — the full breakdown is in this log.
