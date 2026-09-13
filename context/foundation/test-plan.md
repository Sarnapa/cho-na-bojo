# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1-§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-09-13

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team
   is worried about X, and the failure would surface somewhere in an area"
   carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* - drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `server`,
`app\ChoNaBojoApp`, and `shared`. Generated migrations, build output,
fixtures, documentation, and archived plans were excluded.

## 2. Risk Map

Risks are ordered by impact × likelihood. The Source column records the
evidence that surfaced each scenario, never a presumed code anchor.

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence - not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | Concurrent accepts or auto-accepts overfill an event's final slot | High | High | interview Q1; roadmap S-05; hot-spot dir `server\Events` (12 commits/30d) |
| 2 | An authenticated but unauthorized user obtains contact or event data by probing another event or request | High | High | PRD Guardrails and Access Control; roadmap S-05 privacy risk; hot-spot dir `server\Events` (12 commits/30d) |
| 3 | A committed transition produces a missing, late, duplicate, or wrong-recipient notification | Medium | High | interview Q2 and Q3; PRD push NFR; roadmap S-06; hot-spot dirs `server\Events` and `app\ChoNaBojoApp\Services` |
| 4 | Racing cancel, remove, leave, and accept actions leave contradictory state or preserve obsolete contact access | High | Medium | interview Q1; roadmap S-07; hot-spot dirs `server\Events` and `app\ChoNaBojoApp\ViewModels` |
| 5 | Session or authorization races expose protected data or sign out a valid user during token refresh | High | Medium | PRD Access Control; archived auth/session slices; hot-spot dir `app\ChoNaBojoApp\Services` (17 commits/30d) |
| 6 | Client validation or stale UI is trusted, allowing invalid, expired, full-event, or replayed operations | Medium | Medium | PRD FR-005 and FR-007; roadmap S-03 and S-04; hot-spot dirs `server\Events` and `app\ChoNaBojoApp\ViewModels` |

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | Coordinated concurrent attempts never exceed capacity, and losing attempts receive the specified outcome | Sequential happy paths imply concurrency safety | Transaction and lock boundary, persisted capacity state, and auto-accept convergence | Real-database integration | Mocked persistence or requests that never overlap |
| #2 | Organizer, accepted participant, other request states, strangers, and unauthenticated callers each receive only permitted fields | Authentication implies resource authorization | Identity and relationship checks, not-found policy, and contact projection boundary | Multi-identity API integration | Testing only 401 responses or copying production predicates into assertions |
| #3 | Every committed transition creates one durable intent for the correct recipient, and retries remain idempotent and classified | A final 200 or emulator display proves delivery correctness | Transaction/outbox boundary, idempotency key, retry and terminal states, and gateway seam | Integration plus gateway contract | Waiting on Firebase, sleeps, or asserting SDK internals |
| #4 | Racing lifecycle actions end in one valid state and immediately revoke obsolete contact access | Actions proven separately are safe when interleaved | State machine, shared lock ordering, contact gating, and notification side effects | Real-database integration | Independent happy-path tests with no interleaving |
| #5 | Protected data stays gated, refresh is single-flight, and transient failures do not become logout | Successful login proves session safety | Token store, refresh coordination, navigation gate, and error classification | Focused unit plus API auth integration | Real clock/network dependence or over-mocking state transitions |
| #6 | Malformed, stale, expired, full, and replayed operations are rejected or replayed according to the product contract | Client validation is authoritative | Server validation, time source, idempotency rule, and authoritative-state refresh | Shared-validator unit plus API integration | Expected values copied from production calculations |

## 3. Phased Rollout

Each row opens its own change folder. Status uses the orchestrator's fixed
vocabulary and advances only as downstream artifacts land.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|----------------|------------|--------|---------------|
| 1 | Transactional critical-path foundation | Bootstrap the runner and prove capacity and privacy invariants at the API/database boundary | #1, #2 | unit + real-database integration | not started | - |
| 2 | Lifecycle, session, and validation boundaries | Prove state revocation, refresh behavior, and server-authoritative rejection or replay | #4, #5, #6 | unit + integration | not started | - |
| 3 | Deterministic notification pipeline | Prove outbox, recipient, idempotency, retry, and terminal-failure behavior without waiting for Firebase | #3 | integration + gateway contract | not started | - |
| 4 | Android matchmaking smoke and quality floor | Prove one full matchmaking flow and enforce the shipped test floor without duplicating cheaper checks | #1-#6 cross-cutting | minimal Appium e2e + selective AI-native review + gates | not started | - |

## 4. Stack

The repository currently has no test project, test-runner configuration,
or test files. Versions and exact package choices remain hypotheses until
Phase 1 research verifies compatibility with the repository.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| unit + integration | xUnit + Microsoft.NET.Test.Sdk | none yet - see Phase 1 | Candidate supported by current .NET 10 documentation |
| API host | ASP.NET Core WebApplicationFactory/TestServer | none yet - see Phase 1 | Exercise minimal APIs through HTTP rather than calling handlers directly |
| database integration | Isolated PostgreSQL test database | none yet - see Phase 1 | Required where transaction, locking, constraints, or provider behavior is the signal |
| external push boundary | Deterministic fake gateway | none yet - see Phase 3 | Assert intent and outcome classification; never wait for Firebase |
| Android e2e | Appium | none yet - see Phase 4 | One north-star flow only; do not repeat API integration coverage |
| AI-native review | GitHub Copilot CLI - checked: 2026-09-13 | n/a | Review tests for oracle and implementation-mirror defects; never use as an executable gate or replacement for deterministic assertions |

**Stack grounding tools (current session):**
- Docs: Context7 (`/dotnet/docs`) - checked .NET 10 xUnit and ASP.NET Core test-host guidance; checked: 2026-09-13
- Search: Exa.ai - found current Microsoft .NET MAUI 10 Appium guidance; checked: 2026-09-13
- Runtime/browser: no browser or Playwright MCP available in current session; Appium was not run; checked: 2026-09-13
- Provider/platform: GitHub tools are available for inspecting workflow gates; no database provider MCP is available; checked: 2026-09-13

## 5. Quality Gates

The existing deployment workflows restore and publish application
artifacts but do not run tests and are not pull-request quality gates.
Phase 4 must map the required commands into the existing CI model rather
than creating an unrelated pipeline.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| solution build and nullable/type checks | local + CI | required now; CI enforcement after §3 Phase 4 | compile, target-framework, and nullable drift |
| unit + integration | local + CI | required locally after §3 Phase 1; required in CI after §3 Phase 4 | business-rule, authorization, transaction, and persistence regressions |
| Android north-star e2e | pre-release + CI-capable Android runner | required after §3 Phase 4 | broken create-to-accept-to-contact user path |
| deterministic notification contract | local + CI | required locally after §3 Phase 3; required in CI after §3 Phase 4 | outbox, recipient, retry, and idempotency regressions |
| AI test-oracle review | local agent loop | recommended after §3 Phase 4 | tautological assertions and implementation mirrors |
| real Firebase delivery smoke | physical device or controlled emulator | manual, selective | environment credentials, device registration, and delivery latency |

## 6. Cookbook Patterns

These recipes fill in as rollout phases ship.

### 6.1 Adding a shared-rule unit test

- TBD - see §3 Phase 1 for independent-oracle validation and policy tests.

### 6.2 Adding an API/database integration test

- TBD - see §3 Phase 1 for multi-identity privacy and capacity patterns.

### 6.3 Adding a concurrency regression test

- TBD - see §3 Phase 1 for coordinated final-slot attempts and §3 Phase 2
  for interleaved lifecycle transitions.

### 6.4 Adding a notification-pipeline test

- TBD - see §3 Phase 3 for outbox, recipient, retry, and fake-gateway
  patterns that never wait for Firebase.

### 6.5 Adding an Android critical-flow test

- TBD - see §3 Phase 4 for the single north-star Appium smoke pattern.

### 6.6 Per-rollout-phase notes

- TBD - each rollout phase appends concise lessons and canonical references.

## 7. What We Deliberately Don't Test

- **Automated waits for Firebase delivery** - external timing and emulator
  behavior make this expensive and flaky. Prove deterministic intent,
  retry, and gateway contracts in automation; keep one selective delivery
  smoke for the real environment. (Source: Phase 2 interview Q5.)
- **iOS behavior** - iOS is outside the Android-only MVP. Re-evaluate only
  if the product scope changes. (Source: PRD Non-Goals and repository
  rules.)

## 8. Freshness Ledger

- Strategy (§1-§5) last reviewed: 2026-09-13
- Stack versions last verified: 2026-09-13
- AI-native tool references last verified: 2026-09-13

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes,
- §7 negative-space no longer matches what the team believes.
