---
project: cho-na-bojo
researched_at: 2026-05-28
recommended_platform: Railway
runner_up: Google Cloud Run
context_type: mvp
tech_stack:
  language: C#
  framework: ASP.NET Core Web API
  runtime: .NET 10
---

## Recommendation

**Deploy on Railway.**

Railway offers the simplest deployment path for a solo developer building an ASP.NET Core Web API on a 3-week after-hours timeline. Its Railpack build system auto-detects `.csproj` files and resolves .NET 10 from `TargetFramework` — a single `railway up` deploys the API with zero configuration. Native persistent processes mean `IHostedService` background workers (event auto-close lifecycle) run without architectural workarounds. The full MCP server (local + remote, GA) and `llms.txt` documentation make it the most agent-friendly option for AI-assisted development. The $5/month Hobby plan covers all MVP resource needs with usage-based billing absorbing the compute costs entirely.

## Platform Comparison

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP/Integration | Total |
|---|---|---|---|---|---|---|
| **Railway** | Pass | Pass | Pass | Pass | Pass | 5/5 |
| **Google Cloud Run** | Pass | Pass | Partial | Pass | Pass | 4.5/5 |
| **Fly.io** | Pass | Pass | Partial | Pass | Partial | 4/5 |
| **Render** | Pass | Pass | Pass | Partial | Pass | 4.5/5 |
| Vercel | — | — | — | — | — | Eliminated (.NET not supported) |
| Cloudflare Workers | — | — | — | — | — | Eliminated (.NET not supported) |
| Netlify | — | — | — | — | — | Eliminated (.NET not supported) |

### Shortlisted Platforms

#### 1. Railway (Recommended)

Railway scored highest across all five agent-friendly criteria. Key strengths:

- **Zero-config .NET detection**: Railpack's `dotnet` provider reads `TargetFramework` from `.csproj`, installs the correct SDK, sets `ASPNETCORE_URLS`, and publishes — no Dockerfile required (though Dockerfile path is available as fallback).
- **Persistent processes**: The API runs as a long-lived container, not a serverless function. Background workers (`IHostedService`) execute normally without external schedulers.
- **Best-in-class MCP**: Both local (`railway mcp install`) and remote (`mcp.railway.com`) MCP servers with tools for deploy, logs, env vars, and debugging — GA and compatible with GitHub Copilot, Claude Code, and Cursor.
- **Agent-readable docs**: `llms.txt`, `llms-full.txt`, and per-page `.md` endpoints — the most comprehensive AI-friendly documentation of all evaluated platforms.
- **Simple CLI**: `railway up` deploys, `railway logs` streams, `railway redeploy` resets — minimal cognitive overhead for a solo developer.

Trade-offs accepted: $5/mo minimum cost (vs. Cloud Run's free tier), EU West region only (not Warsaw — ~20-40ms added latency for Polish users), self-managed database containers (mitigated by using external Supabase).

#### 2. Google Cloud Run

Cloud Run scored second overall, with its strongest advantages being the generous free tier and Warsaw region proximity:

- **$0/month at MVP scale**: 2M free requests/month, 180k vCPU-seconds — the entire MVP fits within free tier.
- **Warsaw region (`europe-central2`)**: Lowest possible latency for Polish users.
- **Fully managed with auto-scaling**: Zero operational overhead once deployed; scales from 0 to thousands of instances.
- **Cloud Run MCP server (GA)**: Structured tool access for AI assistants.

Gap vs. Railway: Higher setup complexity (IAM, Artifact Registry, billing, multiple GCP services), CPU throttling breaks background workers (requires Cloud Scheduler/Tasks refactoring), no `llms.txt`, steeper learning curve for solo after-hours development.

#### 3. Fly.io

Fly.io scored third, offering ultra-low cost and Warsaw proximity but weaker agent tooling:

- **Scale-to-zero at ~$0.10-$2/month**: Cheapest always-available option with autostop/autostart.
- **Warsaw region (`waw`)**: Good latency for Polish users.
- **Full persistent process support**: Background workers run natively.
- **.NET auto-detection**: `fly launch` detects .NET projects and generates Dockerfiles.

Gap vs. Railway: MCP tooling is experimental (not GA), no `llms.txt`, no dedicated rollback command, Postgres is explicitly "unmanaged", WireGuard requirements on Windows add friction.

## Anti-Bias Cross-Check: Railway

### Devil's Advocate — Weaknesses

1. **$5/month minimum is real, not free**: Unlike Cloud Run's genuinely-free tier, Railway charges $5/mo from day one on Hobby. For an MVP that might sit idle for weeks, you pay regardless.
2. **Self-managed databases**: Railway's PostgreSQL is a container — not a managed database service. No automated point-in-time recovery or failover unless manually configured. (Mitigated: project uses external Supabase.)
3. **No Warsaw region**: Railway offers EU West regions but not Poland specifically. Users in Warsaw see ~20-40ms added latency vs. a Warsaw-located server.
4. **15-minute max request duration**: Railway's edge proxy terminates requests after 15 minutes. Long-polling connections must heartbeat within this window.
5. **Railpack .NET 10 is code-verified but practice-unverified**: The regex handles `net10.0` correctly in source, but Railway's test fixtures only cover .NET 7. Dockerfile fallback is available if Railpack fails.

### Pre-Mortem — How This Could Fail

The developer chose Railway for its delightful DX. The first deploy worked perfectly via Railpack auto-detection. PostgreSQL was skipped in favor of external Supabase — good call. Life was good for two months. Then a local sports tournament drove 500 concurrent users to the map endpoint. Railway's single-replica Hobby plan couldn't handle the connection pool exhaustion, and upgrading to Pro ($20/mo) mid-crisis while manually configuring replicas took precious hours. The $5/month that seemed trivial during development now felt like a commitment when the developer took a 3-week break. Meanwhile, the 20-40ms latency to EU West was barely noticeable for API calls but added up for the map tile loading pattern where users make dozens of rapid viewport-change requests.

### Unknown Unknowns

- **HTTP POST on port 80 becomes GET**: Railway's edge redirects HTTP→HTTPS but converts POST to GET. Webhook senders hitting HTTP (not HTTPS) will silently lose request bodies.
- **60-second WebSocket idle timeout**: Railway's proxy drops idle WebSocket connections after 60 seconds. SignalR's default 15-second keep-alive handles this, but raw WebSockets without heartbeats fail silently.
- **No dedicated outbound IP**: External services with IP allowlisting (Supabase, Firebase) cannot restrict to Railway's shared egress IPs.
- **Build cache corruption**: NuGet restore is cached between builds but the cache key strategy isn't documented. Corrupted caches require manual clearing via dashboard.
- **10-minute build timeout on Free plan**: The $1/month post-trial Free plan has a strict build timeout that large .NET solutions can exceed on first build.

## Operational Story

- **Preview deploys**: Railway supports PR-based preview environments on Pro plan ($20/mo). On Hobby, deploy via `railway up` targeting a separate environment (`railway environment create staging`). No automatic PR previews on Hobby.
- **Secrets**: Environment variables stored in Railway's project settings, scoped per environment (production/staging). Set via `railway variables set KEY=VALUE` or MCP tool `set-variables`. Only team members with project access can read them. No rotation automation — manual update required.
- **Rollback**: One-click rollback via Railway dashboard (select any prior deployment). No dedicated CLI command — use `railway deployment list` to identify deployment IDs, then dashboard to promote. Rollback is code-only; database state is not reverted.
- **Approval**: Human-only actions: delete project, remove team members, upgrade plan tier. Agent-safe actions: deploy, set env vars, restart service, read logs (all available via MCP tools).
- **Logs**: `railway logs` streams live logs via WebSocket. Filter with `--filter "@level:error"`, scope with `--build` or `--deployment`, look back with `--since 1h`. Also accessible via MCP tool `get-logs`.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Railpack fails to resolve .NET 10 SDK | Unknown unknowns | Low | Medium | Fallback to custom Dockerfile with `mcr.microsoft.com/dotnet/sdk:10.0` base image |
| Database loss (if using Railway Postgres) | Pre-mortem | N/A | N/A | Using external Supabase — risk does not apply |
| 20-40ms added latency (EU West vs Warsaw) | Devil's advocate | High | Low | API response times still well under 200ms target; map tiles served from CDN not Railway |
| $5/mo cost during idle periods | Devil's advocate | High | Low | Accept as cost of simplicity; $60/year is trivial vs. dev-time saved |
| Single replica overwhelmed at traffic spike | Pre-mortem | Low | Medium | Monitor via MCP; scale replicas manually or upgrade to Pro with autoscaling if growth warrants |
| WebSocket timeout on idle connections | Unknown unknowns | Medium | Low | SignalR default keep-alive (15s) handles this automatically; no action needed |
| HTTP POST→GET on port 80 redirect | Unknown unknowns | Low | Medium | Ensure all webhook senders (Firebase, Supabase) use HTTPS endpoints exclusively |
| No static outbound IP for allowlisting | Unknown unknowns | Low | Low | Supabase and Firebase don't require IP allowlisting by default; no immediate action |

## Getting Started

1. **Install Railway CLI**:
   ```powershell
   scoop install railway
   ```
   Or via npm: `npm install -g @railway/cli`

2. **Login and link project**:
   ```powershell
   railway login
   railway init
   ```

3. **Ensure `.csproj` has correct TargetFramework** (for Railpack auto-detection):
   ```xml
   <TargetFramework>net10.0</TargetFramework>
   ```

4. **Deploy the API**:
   ```powershell
   railway up
   ```
   Verify build logs show `Installing dotnet@10.x` and correct project publish.

5. **Set environment variables** (Supabase connection, Firebase keys):
   ```powershell
   railway variables set SUPABASE_URL=https://your-project.supabase.co
   railway variables set SUPABASE_KEY=your-anon-key
   railway variables set FIREBASE_PROJECT_ID=your-project-id
   ```

6. **Generate a public domain**:
   ```powershell
   railway domain
   ```

## Out of Scope

The following were not evaluated in this research:
- Docker image configuration
- CI/CD pipeline setup (GitHub Actions auto-deploy-on-merge from tech-stack.md will be configured separately)
- Production-scale architecture (multi-region, HA, DR)
