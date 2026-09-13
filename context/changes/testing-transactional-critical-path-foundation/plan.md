# Transactional Critical-Path Test Foundation Implementation Plan

## Overview

Establish ChoNaBojo's first automated test foundation with two complementary layers: fast unit tests for pure shared rules and production-shaped HTTP integration tests against disposable PostgreSQL/PostGIS. The integration layer will prove the two highest-ranked risks in `context\foundation\test-plan.md`: concurrent acceptance cannot overfill an event, and authentication never implies access to another user's event relationship or contact data.

The plan deliberately preserves the real boundaries that create signal: ASP.NET Core minimal APIs, production JWT validation, EF Core/Npgsql, separate database sessions, PostgreSQL row locks, migrations, and raw HTTP JSON. It does not replace those boundaries with mocked handlers, EF InMemory, SQLite, or copied authorization predicates.

## Current State Analysis

The repository has no test projects, test runner configuration, API fixture, database reset mechanism, or committed automated tests. `solutions\ChoNaBojo.slnx` currently contains the app, server, and three dependency-free shared projects only.

The API already serializes every transition to `Accepted` through an explicit transaction and an event-row `FOR UPDATE` lock. Manual acceptance and auto-accept converge on the same slot claim and recount accepted requests after the lock. This is an application protocol rather than a database-enforced cross-row invariant, so a real PostgreSQL concurrency regression test is required.

Privacy is enforced at several HTTP projection and mutation boundaries. Venue listings expose only caller-relative request state, `/me/events` filters by caller relationship, the organizer queue omits contacts and user IDs, `/contacts` is pairwise, and privileged probes use response-level 404 cloaking. These boundaries need a marker-based multi-identity matrix rather than tests that reproduce the production predicates.

The current authenticated-user helper throws when a cryptographically valid principal has no valid GUID identity claim. The planning decision is to reject that token at the bearer-authentication boundary and return 401 before an event handler executes.

## Desired End State

The solution contains separate `net10.0` unit and server-integration test projects. Unit tests run without Docker. Integration tests start a pinned disposable PostGIS container, apply the production migrations, host the real API through `WebApplicationFactory<Program>`, issue production-shaped JWTs for deterministic users, reset mutable data between tests, and prevent hosted workers from racing the fixture.

The privacy suite proves the complete Events API relationship matrix using raw JSON marker assertions and fresh authorized reads after forbidden mutations. The concurrency suite deterministically holds the event row, observes both API sessions waiting on the PostgreSQL lock, releases them together, and verifies winner-independent HTTP and persisted outcomes for manual/manual, auto/auto, and manual/auto final-slot races.

Completion requires the real integration suite to pass against a compatible Linux Docker engine. If that engine is unavailable, implementation is explicitly blocked; compilation or mocks are not accepted as a substitute for the required database signal.

### Key Discoveries:

- Every API-owned transition to `Accepted` locks the event row before capacity is recounted and status is committed (`server\Events\EventEndpoints.cs:787-1002`, `server\Events\EventEndpoints.cs:1162-1211`).
- No database constraint independently enforces `1 + accepted requests <= ParticipantLimit`; the lock protocol is the invariant under test (`server\Data\ChoNaBojoContext.cs:257-261`, `server\Data\ChoNaBojoContext.cs:348-392`).
- All `/api` event routes share JWT authorization, but resource authorization and response projection remain route-specific (`server\Program.cs:50-75`, `server\Program.cs:140-144`, `server\Events\EventEndpoints.cs:201-728`).
- Contact access is intentionally pairwise: organizers see accepted participants, accepted participants see the organizer, and participants never see one another (`server\Events\EventEndpoints.cs:650-728`).
- Closed events retain accepted-pair contact access; cancelled events revoke it (`server\Events\EventEndpoints.cs:654-662`, `context\archive\2026-09-11-event-lifecycle-ops\reviews\impl-review.md:36-51`).
- Production migrations require both PostGIS geometry support and `pgcrypto`; plain PostgreSQL, SQLite, and EF InMemory cannot supply the required test signal (`server\Migrations\20260714180831_InitialCreate.cs:14-17`, `server\Migrations\20260716215146_AddAuthTables.cs:14-18`).
- `PushDeliveryWorker` and `EventAutoCloseWorker` start with the API and can independently read or mutate fixture state, so the test host must remove them (`server\Program.cs:112-113`).

## What We're NOT Doing

- No Android UI, Appium, emulator, or iOS tests; Android north-star coverage belongs to rollout Phase 4.
- No Firebase delivery, push retry, outbox idempotency, or gateway-contract suite; notification behavior belongs to rollout Phase 3.
- No lifecycle race matrix for cancel/remove/leave versus accept; those interleavings belong to rollout Phase 2.
- No timing-indistinguishability guarantee for 404 cloaking. This phase protects equal HTTP status/body outcomes, not contention-based response timing.
- No deadlock, lock-timeout, connection-failure, or database-unavailable HTTP contract; no typed product behavior exists for those operational failures.
- No assertion about which contender wins, request fairness, array/insertion order, exact English error text, or the current conflict `Field` value.
- No mocked `DbSet`, direct calls to private endpoint helpers, copied authorization predicates, EF InMemory, or SQLite substitutes.
- No shared development, Supabase, Railway, or other externally configured database fallback for destructive integration tests.
- No CI workflow enforcement yet; converting the local test floor into a pull-request gate belongs to rollout Phase 4.
- No production hosted-worker feature flag or new registration abstraction; removing the two hosted services in `WebApplicationFactory` is sufficient.
- No database schema migration or independent database capacity constraint.
- No mutation-testing package or repository-wide coverage target in this phase.

## Implementation Approach

Create two test projects under `tests\`: one references the dependency-free shared validation/contracts projects, and one references the server. The integration project owns a serialized xUnit collection backed by one `postgis/postgis:17-3.5` Testcontainers instance. It applies migrations once, preserves migration/reference tables, and uses Respawn to clear mutable tables before each scenario.

Expose the top-level minimal API through `public partial class Program`. The custom factory injects a test connection string and deterministic JWT/Firebase settings before startup, replaces the production `ChoNaBojoContext` registration so the fixture controls seeding and connection identity, and removes both hosted workers. It keeps real Npgsql/NetTopologySuite and real JWT bearer validation.

Add a non-throwing stable-user-ID parser to the existing authentication helper and invoke it from JWT bearer validation. A signed token without a valid GUID identity fails authentication and receives 401; authorized endpoint handlers retain the canonical `GetUserId` accessor after that gate.

Build privacy scenarios from fixed identities with unique marker values for login email, shareable email, phone, communicator handle, user ID, request ID, and event ID. Inspect raw response JSON to assert permitted literals and the absence of forbidden keys/markers. For forbidden mutations, follow the response assertion with a fresh authorized read and database query to prove no state, timestamp, roster, or contact entitlement changed.

For concurrency, use separate API, control, and observer PostgreSQL application names. A control transaction locks the event row, both HTTP requests start, the observer confirms both API sessions are waiting on that lock through `pg_stat_activity` and `pg_blocking_pids`, and only then releases the control transaction. Fresh persisted-state assertions determine the winner-independent result.

## Critical Implementation Details

### Timing & lifecycle

The concurrency tests must prove overlap at the database lock boundary. `Task.WhenAll`, a start barrier, or repeated execution alone does not demonstrate that both requests reached the critical section. Polling must be bounded and diagnostic; timeout output must include backend PID, state, wait event, current query, and blockers, and the control lock must always be released in `finally`.

The fixture must remove `PushDeliveryWorker` and `EventAutoCloseWorker` before the test host starts. Otherwise the workers can consume outbox rows or close scenario events independently, making privacy and persistence assertions nondeterministic.

### State sequencing

Each concurrency assertion must await both HTTP responses, dispose their request scopes, and then query persisted state through a fresh context/connection. Assertions against entities tracked by the seed context can report stale values and would not prove committed state.

Database reset cannot wrap API requests in one ambient rollback transaction because the API requires separate visible connections and real row locks. Reset happens between tests, while each test commits production transactions normally.

## Phase 1: Bootstrap the Test Host and PostGIS Fixture

### Overview

Add the test projects and the reusable infrastructure needed for every later phase. This phase establishes deterministic configuration, production-shaped authentication, disposable database lifecycle, migration/reset behavior, worker isolation, and a controlled 401 outcome for invalid identity claims.

### Changes Required:

#### 1. Unit test project

**File**: `tests\ChoNaBojo.UnitTests\ChoNaBojo.UnitTests.csproj`

**Intent**: Create a Docker-independent xUnit project for pure shared validation and policy tests.

**Contract**: Target `net10.0` with nullable reference types and implicit usings enabled. Pin `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, and `xunit.runner.visualstudio` 3.1.4; reference `shared\ChoNaBojo.Contracts` and `shared\ChoNaBojo.Validation`.

#### 2. Server integration test project

**File**: `tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj`

**Intent**: Create the HTTP/database integration project without introducing an alternate persistence provider or authentication stack.

**Contract**: Target `net10.0` with nullable reference types and implicit usings enabled. Use the same test SDK/xUnit versions plus `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Testcontainers.PostgreSql` 4.15.0, and `Respawn` 7.0.0; reference `server\server.csproj` and the shared contracts project.

#### 3. Solution registration

**File**: `solutions\ChoNaBojo.slnx`

**Intent**: Make both test projects discoverable from the repository's canonical solution build.

**Contract**: Add a `/tests/` solution folder containing the unit and integration project paths. Do not modify archived solution artifacts or add a second solution file.

#### 4. Minimal API host seam and identity validation

**Files**:

- `server\Program.cs`
- `server\Auth\CurrentUser.cs`

**Intent**: Expose the top-level host to `WebApplicationFactory` and fail malformed stable-identity claims at authentication instead of allowing endpoint helpers to throw.

**Contract**: Declare a public partial `Program` type after `app.Run()`. Add a non-throwing GUID identity parser used by JWT bearer validation; call `Fail` during token validation when neither `sub` nor `NameIdentifier` supplies a valid GUID so protected endpoints return 401. Keep `GetUserId` as the canonical accessor for principals that passed authentication.

#### 5. Disposable PostGIS lifecycle

**Files**:

- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\PostgisFixture.cs`
- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\IntegrationTestCollection.cs`

**Intent**: Start one isolated real database per serialized integration-test collection and provide a reliable reset boundary between tests.

**Contract**: Pin `postgis/postgis:17-3.5`, assign a distinctive test database name, apply production migrations once, verify `postgis` and `pgcrypto`, and initialize Respawn for PostgreSQL. Preserve `__EFMigrationsHistory`, `Sports`, and `spatial_ref_sys`; reset all mutable application tables before each test. Never read an external `AppDb` value as a fallback.

#### 6. Production-shaped API factory

**Files**:

- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\ChoNaBojoApiFactory.cs`
- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\TestJwtFactory.cs`
- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\TestConfiguration.cs`

**Intent**: Host the real endpoint pipeline with deterministic credentials and no background interference.

**Contract**: Use environment `Testing` and an HTTPS test-client base address. Inject `ConnectionStrings:AppDb`, JWT issuer/audience/signing key, and non-secret Firebase placeholders before startup. Replace the production context registration with the fixture-controlled Npgsql/NetTopologySuite registration, set an API application name, omit production CSV seeding, and remove only `PushDeliveryWorker` and `EventAutoCloseWorker`. Generate signed tokens with the same `sub` and `NameIdentifier` contract as `TokenService`, plus explicit malformed variants for authentication tests.

#### 7. Infrastructure smoke tests

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\TestHostSmokeTests.cs`

**Intent**: Fail early when the fixture silently targets the wrong database, misses an extension/migration, or leaves a worker active.

**Contract**: Prove `/health` responds through the factory, the database name and application name are test-only, all migrations are applied, required extensions exist, hosted workers are absent, and invalid/missing stable-identity claims receive 401 on a protected Events route.

### Success Criteria:

#### Automated Verification:

- Test projects restore successfully: `dotnet restore solutions\ChoNaBojo.slnx`
- Unit and integration projects compile: `dotnet build tests\ChoNaBojo.UnitTests\ChoNaBojo.UnitTests.csproj && dotnet build tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj`
- Host and database smoke tests pass: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --filter "FullyQualifiedName~TestHostSmokeTests"`
- Canonical solution builds with both test projects: `dotnet build solutions\ChoNaBojo.slnx`

#### Manual Verification:

- With the Linux Docker engine stopped, the integration command reports a clear unmet environment prerequisite and does not skip, mock, or target an external database.
- With Docker running, the disposable PostGIS container is removed after the test process completes and no ChoNaBojo development data is changed.

**Implementation Note**: After completing this phase and all automated verification passes, pause for confirmation that the Docker failure/success behavior and cleanup were observed before proceeding.

---

## Phase 2: Protect Independent Pure Rules

### Overview

Add fast, parameterized tests whose expected values come from the PRD, shared constants, and accepted privacy rules rather than the current method branches. These tests establish the Docker-independent feedback loop promised by the rollout.

### Changes Required:

#### 1. Contact-pattern privacy guard

**File**: `tests\ChoNaBojo.UnitTests\Validation\ContactPatternGuardTests.cs`

**Intent**: Protect the rule that public event free text cannot become a pre-acceptance contact-leak channel.

**Contract**: Parameterize representative email, phone, URL, and handle forms plus legitimate sports/time text. Include null, empty, whitespace, separator variants, boundary digit counts, and a bounded-scan timeout/adversarial input case. Assert only the public `ContainsContactPattern` behavior, not individual regular expressions.

#### 2. Registration contact requirements

**File**: `tests\ChoNaBojo.UnitTests\Validation\AuthValidationTests.cs`

**Intent**: Prove that every registrant supplies at least one shareable contact method and that communicator platform/handle values are paired and defined.

**Contract**: Use literal request examples derived from FR-011 and the persisted-enum lesson. Cover phone-only, contact-email-only, valid communicator pairs, no contact, platform-only, handle-only, undefined platform, and whitespace normalization.

#### 3. Event participant and time boundaries

**File**: `tests\ChoNaBojo.UnitTests\Validation\EventValidationTests.cs`

**Intent**: Protect participant-limit and time rules that feed the capacity-critical API scenarios without duplicating implementation calculations.

**Contract**: Assert the explicit 2 and 300 inclusive participant bounds, 1 and 301 rejection, end-after-start, maximum 24-hour duration, zero UTC offset, and the documented two-minute start-clock tolerance using fixed instants. Keep contact-text cases focused in `ContactPatternGuardTests`.

### Success Criteria:

#### Automated Verification:

- All unit tests pass without Docker: `dotnet test tests\ChoNaBojo.UnitTests\ChoNaBojo.UnitTests.csproj`
- Unit tests remain dependency-free from ASP.NET Core, EF Core, Npgsql, MAUI, and the server project.
- Parameterized boundary cases include both accepted and rejected values for every protected rule.

#### Manual Verification:

- Review each test name and literal expected outcome against the cited PRD/lesson/constant and confirm no expected value is computed with the production method under test.

**Implementation Note**: After completing this phase and all automated verification passes, pause for the oracle review before proceeding.

---

## Phase 3: Prove the Event Privacy and Authorization Matrix

### Overview

Build deterministic multi-identity scenarios and assert the complete Events API disclosure and mutation boundary. This phase protects response-level 404 cloaking, pairwise contact reveal, closed-versus-cancelled lifecycle behavior, and the rule that forbidden operations cannot partially mutate state.

### Changes Required:

#### 1. Marker-rich scenario builder

**Files**:

- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\EventScenarioBuilder.cs`
- `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\JsonPrivacyAssertions.cs`

**Intent**: Centralize deterministic seeding and privacy assertions without centralizing the expected authorization predicate.

**Contract**: Provide fixed organizer, accepted, pending, rejected, removed, left, and stranger identities. Give every identity unique login-email, contact-email, phone, communicator-handle, user-ID, and request-ID markers. Seed active, closed, cancelled, foreign, and missing/cross-event cases. JSON helpers may assert key/value absence and marker presence, but must not decide who is authorized.

#### 2. Authentication barrier and malformed identity

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Events\EventAuthenticationTests.cs`

**Intent**: Prove that every Events route is inaccessible without a valid bearer identity.

**Contract**: Parameterize all mapped Events routes and methods. Missing, invalid-signature, expired, missing-subject, and non-GUID-subject tokens return 401 and expose no seeded event/contact marker. Do not assert framework-generated English challenge text.

#### 3. Discovery and caller-relative state

**Files**:

- `tests\ChoNaBojo.Server.IntegrationTests\Events\VenueEventPrivacyTests.cs`
- `tests\ChoNaBojo.Server.IntegrationTests\Events\MyEventsPrivacyTests.cs`

**Intent**: Prove that authenticated discovery exposes only the shared event shell and the caller's own relationship.

**Contract**: Exercise all seeded identities. Venue listings may expose event fields, counts, `IsOrganizer`, and only the caller-relative request status; they must not expose request IDs, user IDs, login emails, shareable contacts, password/token fields, or another caller's state. `/me/events` returns only organized/requested events and the caller's own request ID/status, with all foreign relationship and contact markers absent.

#### 4. Organizer queue and response-level cloaking

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Events\JoinRequestQueuePrivacyTests.cs`

**Intent**: Protect the organizer-only queue while preserving pseudonymous request handling.

**Contract**: The organizer sees every request state, request ID, display key, and timestamps without requester user IDs or contact markers. Accepted, pending, rejected, removed, left, stranger, cross-event, fabricated-request, and missing-event probes receive the same 404 status and canonical typed-body values. Timing is explicitly not asserted.

#### 5. Pairwise contacts and lifecycle boundary

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Events\EventContactPrivacyTests.cs`

**Intent**: Prove that contact fields cross the API boundary only for the accepted pair and remain unavailable to all other relationships.

**Contract**: The organizer receives accepted participants only; each accepted participant receives the organizer only; accepted participants never receive one another. Pending, rejected, removed, left, stranger, cross-event, and missing-event callers receive the same 404 status/body. Repeat the entitled cases for a Closed event and the denied cases for a Cancelled event. Assert forbidden raw markers and keys are absent, not null.

#### 6. Forbidden mutations and leave-state outcomes

**Files**:

- `tests\ChoNaBojo.Server.IntegrationTests\Events\UnauthorizedEventMutationTests.cs`
- `tests\ChoNaBojo.Server.IntegrationTests\Events\LeaveEventAuthorizationTests.cs`

**Intent**: Prove that a cloaked failure cannot be paired with a hidden state change and that leave outcomes reveal no foreign relationship.

**Contract**: Non-organizers, cross-event tuples, fabricated IDs, and missing IDs cannot accept, reject, remove, or cancel. After each attempt, use a fresh authorized read and database context to prove event/request status, resolution timestamp, participant count, roster, and contact entitlement are unchanged. Accepted callers can leave; pending/rejected/removed/left callers receive typed `409 participant_not_accepted`; strangers and missing relationships receive 404.

#### 7. Public free-text write guard

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Events\EventContactPublishGuardTests.cs`

**Intent**: Prove that API write validation closes the self-publish bypass before public title/description projection.

**Contract**: Contact-like title and description marker values return the existing 400 validation shape and persist no event. Safe sports/time text succeeds and later listing JSON contains the safe text without any contact marker.

### Success Criteria:

#### Automated Verification:

- Authentication matrix passes: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --filter "FullyQualifiedName~EventAuthenticationTests"`
- Privacy read matrix passes: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --filter "FullyQualifiedName~PrivacyTests"`
- Forbidden mutation and leave-state tests pass: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --filter "FullyQualifiedName~UnauthorizedEventMutationTests|FullyQualifiedName~LeaveEventAuthorizationTests"`
- Contact self-publish guard tests pass: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --filter "FullyQualifiedName~EventContactPublishGuardTests"`
- Full integration project passes after database reset between every scenario: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj`

#### Manual Verification:

- Inspect a representative failing marker assertion and confirm its diagnostic names the route, caller role, forbidden key/value, and response body without printing secrets.
- Review the matrix against all mapped Events routes and confirm no route family was accidentally omitted.

**Implementation Note**: After completing this phase and all automated verification passes, pause for review of the marker diagnostics and route inventory before proceeding.

---

## Phase 4: Prove Final-Slot Serialization and Document the Recipes

### Overview

Add deterministic real-database concurrency tests for all acceptance path pairings, then record the canonical test patterns in the test-plan cookbook so later rollout phases reuse the fixture correctly.

### Changes Required:

#### 1. PostgreSQL lock-overlap coordinator

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Infrastructure\PostgresLockCoordinator.cs`

**Intent**: Turn the final-slot race from a probabilistic stress test into a deterministic exercise of the actual row-lock boundary.

**Contract**: Use separate `ChoNaBojo.Integration.Control` and `ChoNaBojo.Integration.Observer` connections alongside API connections identified as `ChoNaBojo.Integration.Api`. Acquire `FOR UPDATE` on the target event in the control transaction, expose its backend PID, start both HTTP operations, and poll `pg_stat_activity`/`pg_blocking_pids` until two matching API sessions are lock-waiting on the event-lock query. Apply a bounded timeout with detailed diagnostics and release/rollback the control transaction in `finally`.

#### 2. Final-slot capacity regressions

**File**: `tests\ChoNaBojo.Server.IntegrationTests\Events\FinalSlotCapacityTests.cs`

**Intent**: Protect the application-level capacity invariant for every writer pairing that can assign `Accepted`.

**Contract**: Seed `ParticipantLimit = 2`, so the organizer occupies one slot. Cover:

- two pending requests accepted manually;
- two users joining an auto-accept event;
- one pending manual acceptance racing one auto-accept join.

For every case assert exactly one success (`200` or `201` according to the winning path), exactly one typed `409 event_full`, exactly one accepted request, and organizer plus accepted count equal to two. A losing manual request remains Pending with no resolution timestamp; a losing auto-accept join creates no request row. Assertions must be winner-independent.

#### 3. Test-plan cookbook

**File**: `context\foundation\test-plan.md`

**Intent**: Replace the Phase 1 cookbook placeholders with concise, reusable instructions grounded in the shipped harness.

**Contract**: Update §6.1 with the shared-rule unit-test oracle pattern, §6.2 with the API/PostGIS fixture and marker-privacy pattern, §6.3 with the control-lock/observer concurrency pattern, and §6.6 with Phase 1 commands, Docker prerequisite, reset boundary, canonical files, and explicit anti-patterns. Do not alter the frozen §1-§5 strategy.

### Success Criteria:

#### Automated Verification:

- All three deterministic final-slot races pass: `dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --filter "FullyQualifiedName~FinalSlotCapacityTests"`
- Final-slot tests pass 20 consecutive times without a flaky retry: `1..20 | ForEach-Object { dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~FinalSlotCapacityTests"; if ($LASTEXITCODE) { exit $LASTEXITCODE } }`
- Unit and full integration suites pass together: `dotnet test tests\ChoNaBojo.UnitTests\ChoNaBojo.UnitTests.csproj && dotnet test tests\ChoNaBojo.Server.IntegrationTests\ChoNaBojo.Server.IntegrationTests.csproj`
- Canonical solution builds after cookbook and test additions: `dotnet build solutions\ChoNaBojo.slnx`
- Test-plan cookbook no longer contains Phase 1 TBD placeholders in §6.1, §6.2, §6.3, or §6.6.

#### Manual Verification:

- Inspect one successful concurrency run's observer diagnostics and confirm both API backend sessions were waiting on the control lock before release.
- Confirm a deliberately shortened observer timeout fails with actionable PID/wait/blocker diagnostics and still releases the control transaction and container resources.

**Implementation Note**: After completing this phase and all automated verification passes, pause for confirmation of the overlap evidence and timeout cleanup before declaring the change implemented.

---

## Testing Strategy

### Unit Tests:

- Test public pure-rule behavior only; do not reflect over private regular expressions or compute expected values with production validators.
- Use fixed literal oracles for contact patterns, communicator pairing, enum validity, participant limits, UTC offsets, duration, and clock tolerance.
- Keep the unit project runnable without Docker and without references to the server, ASP.NET Core, EF Core, Npgsql, or MAUI.

### Integration Tests:

- Exercise minimal API routes through `HttpClient` and real JWT bearer validation.
- Use one serialized PostGIS container, production migrations, committed API transactions, and reset between tests.
- Use raw JSON negative assertions for privacy; DTO deserialization alone can hide unexpected extra fields.
- Verify forbidden mutations with subsequent authorized reads and fresh database state.
- Prove concurrency overlap through PostgreSQL session state before judging HTTP and persistence outcomes.
- Keep expected winner identity, response timing, English message wording, and operational database failures outside the oracle.

### Regression Matrix:

| Risk | Scenario | Expected observable outcome | Regression caught |
|---|---|---|---|
| Capacity | Manual accept vs manual accept for one slot | One 200, one `409 event_full`; loser Pending | Removed/misordered event lock or stale capacity recount |
| Capacity | Auto-accept vs auto-accept for one slot | One 201, one `409 event_full`; no loser row | Auto-accept bypasses shared slot claim |
| Capacity | Manual accept vs auto-accept for one slot | One path wins; one typed conflict; one accepted row | Writers use different lock roots/protocols |
| Authentication | Missing/invalid identity claim | 401, no event/contact marker | Authenticated principal reaches handler without stable user ID |
| Discovery | Each authenticated role lists a venue | Only event shell and caller-relative state | Foreign relationship/contact projection |
| My events | Caller with organized/requested/foreign events | Only caller-related events/request IDs | Query lacks caller isolation |
| Queue | Non-organizer/cross-event/fabricated probe | Same response-level 404 cloak | Identifier or ownership enumeration |
| Contacts | Organizer, accepted, other states, stranger | Pairwise allow; all others same 404 | Contact leakage or participant-to-participant reveal |
| Lifecycle | Closed vs cancelled event contacts | Closed retains; cancelled revokes | Auto-close accidentally revokes or cancellation preserves access |
| Mutations | Non-organizer accept/reject/remove/cancel | Cloaked failure and unchanged state | Partial unauthorized mutation |
| Public text | Contact-like event title/description | 400 and no persisted event | Self-publish bypass of contact gating |

### Manual Testing Steps:

1. Run the integration suite once with Docker stopped and confirm it fails clearly as an environment prerequisite rather than skipping.
2. Start a compatible Linux Docker engine and run the host smoke filter.
3. Run the full privacy matrix and inspect one intentionally induced marker failure for safe, actionable diagnostics.
4. Run the final-slot filter and inspect observer evidence that two API sessions waited before release.
5. Run the 20-iteration final-slot stability command.
6. Confirm all Testcontainers resources are removed and the development database remains unchanged.

## Performance Considerations

Use one container per serialized integration collection to amortize image startup and migrations. Keep database-reset work bounded to mutable application tables, preserve reference/migration tables, and seed only the identities and events required by each scenario.

Do not parallelize tests sharing the fixture. Concurrency remains explicit inside `FinalSlotCapacityTests`; uncontrolled xUnit parallelism would compete with reset and make failures ambiguous. Unit tests remain independently parallelizable.

Bound every database-session poll and include diagnostics instead of using sleeps. The 20-run stability check is a selective local quality gate for the critical race, not a permanent multiplier on every normal test invocation.

## Migration Notes

No database schema migration is required. Integration setup applies the existing production migration chain to the disposable PostGIS database and verifies no migration is pending.

The only production behavior change is authentication hardening: a correctly signed token without a valid stable GUID identity now receives 401 before reaching route handlers. Server-issued tokens already contain both required claims, so valid clients retain existing behavior.

The `public partial class Program` declaration is a test-host discoverability seam and does not alter runtime routing or deployment.

## References

- Related research: `context\changes\testing-transactional-critical-path-foundation\research.md`
- Rollout strategy and risk map: `context\foundation\test-plan.md`
- Product privacy and capacity oracle: `context\foundation\prd.md`
- Accepted shared-code and contact-leak rules: `context\foundation\lessons.md`
- Transaction and lock protocol: `server\Events\EventEndpoints.cs:787-1002`
- Capacity recount and response mapping: `server\Events\EventEndpoints.cs:1162-1257`
- Privacy projections: `server\Events\EventEndpoints.cs:201-728`
- JWT claim issuance and extraction: `server\Auth\TokenService.cs:26-52`, `server\Auth\CurrentUser.cs:12-32`
- Host configuration and worker registration: `server\Program.cs:17-146`
- PostgreSQL model/extensions: `server\Data\ChoNaBojoContext.cs:104-105`
- ASP.NET Core integration-test pattern: `WebApplicationFactory<Program>` in Microsoft ASP.NET Core integration testing guidance
- Database test strategy: EF Core testing guidance and Testcontainers for .NET PostgreSQL module

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references\progress-format.md`.

### Phase 1: Bootstrap the Test Host and PostGIS Fixture

#### Automated

- [ ] 1.1 Test projects restore successfully
- [ ] 1.2 Unit and integration projects compile
- [ ] 1.3 Host and database smoke tests pass
- [ ] 1.4 Canonical solution builds with both test projects

#### Manual

- [ ] 1.5 Docker-unavailable execution fails clearly without a skip, mock, or external database fallback
- [ ] 1.6 Docker-backed execution cleans up its container without changing development data

### Phase 2: Protect Independent Pure Rules

#### Automated

- [ ] 2.1 All unit tests pass without Docker
- [ ] 2.2 Unit tests remain free of server and framework dependencies
- [ ] 2.3 Every protected rule has accepted and rejected boundary cases

#### Manual

- [ ] 2.4 Unit-test names and expected values pass independent-oracle review

### Phase 3: Prove the Event Privacy and Authorization Matrix

#### Automated

- [ ] 3.1 Authentication matrix passes
- [ ] 3.2 Privacy read matrix passes
- [ ] 3.3 Forbidden mutation and leave-state tests pass
- [ ] 3.4 Contact self-publish guard tests pass
- [ ] 3.5 Full integration project passes with reset between scenarios

#### Manual

- [ ] 3.6 Marker assertion diagnostics are specific and do not print secrets
- [ ] 3.7 Every mapped Events route family is represented in the matrix

### Phase 4: Prove Final-Slot Serialization and Document the Recipes

#### Automated

- [ ] 4.1 All three deterministic final-slot races pass
- [ ] 4.2 Final-slot tests pass 20 consecutive times without a flaky retry
- [ ] 4.3 Unit and full integration suites pass together
- [ ] 4.4 Canonical solution builds after cookbook and test additions
- [ ] 4.5 Phase 1 cookbook placeholders are replaced with shipped recipes

#### Manual

- [ ] 4.6 Observer evidence confirms both API sessions waited before lock release
- [ ] 4.7 Forced observer timeout reports diagnostics and releases database resources
