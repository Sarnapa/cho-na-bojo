---
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
---

## Why this stack

Solo developer building a mobile sports-matchmaking app (Android-only MVP, 3-week after-hours timeline) with .NET MAUI for the mobile frontend and ASP.NET Core Web API for the backend. C# provides end-to-end type safety across both projects, and the .NET ecosystem's official templates (dotnet new webapi, dotnet new maui) give convention-based scaffolding that an AI agent can navigate predictably. The dotnet starter passes all four agent-friendly gates and has verified bootstrapper confidence for the API project; MAUI scaffolding is manual but follows Microsoft's well-documented official template. PostgreSQL backs the data layer; Firebase Cloud Messaging handles push notifications; background jobs manage event lifecycle (auto-closing past-end-time events). Deployment targets Google Cloud Run (containerized API) with GitHub Actions auto-deploying on merge.
