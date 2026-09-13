---
date: 2026-09-13T10:18:26.680+02:00
researcher: GitHub Copilot
git_commit: 4189ab75e42ff8a043352a4ad51a3870844c3547
branch: master
repository: Sarnapa/cho-na-bojo
topic: "Transactional capacity and privacy test foundation"
tags: [research, codebase, testing, events, postgresql, authorization]
status: complete
last_updated: 2026-09-13
last_updated_by: GitHub Copilot
---

# Research: Transactional Capacity and Privacy Test Foundation

**Date**: 2026-09-13T10:18:26.680+02:00
**Researcher**: GitHub Copilot
**Git Commit**: 4189ab75e42ff8a043352a4ad51a3870844c3547
**Branch**: master
**Repository**: Sarnapa/cho-na-bojo

## Research Question

Open rollout Phase 1, "Transactional critical-path foundation", and ground tests for:

- Risk #1: concurrent accepts or auto-accepts must not overfill an event's final slot, and losing attempts must receive the specified outcome.
- Risk #2: organizers, accepted participants, users in other request states, strangers, and unauthenticated callers must receive only the event and contact fields permitted to them.
- Phase 1 infrastructure: unit tests plus real-database API integration tests.

## Summary

The live API currently serializes every API-owned transition to `Accepted` by locking the event row with PostgreSQL `SELECT ... FOR UPDATE`, recounting accepted requests after the lock, and committing the request status and notification outbox entry in one transaction. Manual acceptance and auto-accept converge on the same slot-claim helper. There is no database constraint that independently enforces capacity, so the invariant remains an application lock protocol that needs a real-PostgreSQL concurrency regression test.

The capacity oracle is specific enough to test without mirroring the implementation. For a single final slot, exactly one contender succeeds. A losing manual acceptance receives `409 event_full` and remains `Pending`; a losing auto-accept join receives `409 event_full` and creates no request row. Winner identity, exact English message text, and lock-timeout/deadlock behavior are not product contracts and must not be pinned.

Privacy is enforced principally at the HTTP projection boundary. All `/api` routes require JWT authentication. Venue and personal event views expose event shells and only caller-relative request state; the organizer request queue omits contacts and user IDs; `/contacts` returns pairwise data only between the organizer and accepted participants. Privileged probes use a historical 404-cloaking policy, although the PRD does not specify 404 versus 403. Phase 1 needs a multi-identity API matrix with marker values and negative JSON assertions, not tests that reproduce production predicates.

The repository has no test projects or runner setup. The recommended foundation is separate `net10.0` unit and API integration projects, `WebApplicationFactory`, real production JWTs with deterministic identities, and a disposable PostGIS database because the production migrations require PostgreSQL extensions. The API host needs a `public partial class Program` seam, test configuration must replace the production connection and credentials, and both hosted workers must be removed from the test host.

## Detailed Findings

### Risk #1: Transactional Capacity Boundary

#### Manual acceptance and auto-accept converge

The protected accept route delegates to a shared transition operation ([`server\Events\EventEndpoints.cs:339-352`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L339-L352)). The transition:

- opens an explicit database transaction;
- locks the event row with `SELECT ... FOR UPDATE`;
- resolves the request and organizer relationship;
- accepts only a pending request, while treating a same-target retry as idempotent;
- recounts accepted participants under the lock;
- saves the status and outbox intent before committing.

The transaction and lock sequence is visible in [`server\Events\EventEndpoints.cs:787-804`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L787-L804), request resolution in [`server\Events\EventEndpoints.cs:878-943`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L878-L943), and atomic persistence in [`server\Events\EventEndpoints.cs:982-1002`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L982-L1002).

Join creation uses the same transition with `createIfMissing: true`. An ordinary event leaves the request pending; an auto-accept event uses the same slot claim as manual acceptance ([`server\Events\EventEndpoints.cs:319-336`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L319-L336), [`server\Events\EventEndpoints.cs:806-943`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L806-L943)). Every live server assignment to `Accepted` goes through `ClaimParticipantSlotUnderLockAsync`, which counts the organizer plus accepted requests ([`server\Events\EventEndpoints.cs:1162-1211`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L1162-L1211)).

At PostgreSQL's default `Read Committed` isolation, a contender waiting on `FOR UPDATE` executes its later count statement against a fresh snapshot after the winner commits. Separate HTTP requests receive separate scoped `ChoNaBojoContext` instances ([`server\Program.cs:41-46`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Program.cs#L41-L46)). This makes the current protocol capacity-safe when every writer follows it.

#### The invariant is not independently enforced by the database

The database constrains the participant-limit range and persisted request states, but no cross-row constraint can enforce `1 + AcceptedCount <= ParticipantLimit` ([`server\Data\ChoNaBojoContext.cs:257-261`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Data/ChoNaBojoContext.cs#L257-L261), [`server\Data\ChoNaBojoContext.cs:348-392`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Data/ChoNaBojoContext.cs#L348-L392)). A future writer that bypasses the event lock could overfill an event. The regression test must therefore protect the shared lock protocol rather than imply that PostgreSQL rejects over-capacity data by constraint.

#### Independent concurrency oracle

The organizer counts toward the participant limit (`context\foundation\prd.md:98-99`). The historical approval contract defines `409 event_full`, a pending manual loser, and no persisted row for an auto-accept loser ([`context\archive\2026-09-07-approval-and-contact-reveal\plan.md:166-177`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/plan.md#L166-L177)). The typed conflict code remains public in [`shared\ChoNaBojo.Contracts\Consts\EventConflictCodes.cs:8-15`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/shared/ChoNaBojo.Contracts/Consts/EventConflictCodes.cs#L8-L15).

For `ParticipantLimit = 2`:

| Scenario | Successful result | Losing result | Persisted invariant |
|---|---|---|---|
| Two manual accepts | Exactly one `200`, request status `Accepted` | Exactly one `409`, code `event_full`; request remains `Pending` with no acceptance timestamp | Exactly one accepted request; organizer + accepted count equals 2 |
| Two auto-accept joins | Exactly one `201`, request status `Accepted` | Exactly one `409`, code `event_full`; no request row for the loser | Exactly one accepted request row; organizer + accepted count equals 2 |
| Manual accept versus auto-accept join | Exactly one `200` or `201`, according to the winning path | Exactly one `409`, code `event_full`; loser persistence follows its path's rule | Exactly one accepted request; capacity never exceeds 2 |

The tests must not assert which identity wins, exact English error text, or a fairness/order guarantee. `Field = "eventId"` is current behavior but is not independently specified. Deadlock, lock-timeout, and connection-failure mappings are also unspecified; the implementation currently has no typed mapping for these operational failures.

#### Synchronization must prove overlap

`Task.WhenAll` or a start barrier alone does not prove concurrent requests reached the lock boundary. The strongest black-box arrangement is:

1. A control Npgsql transaction acquires `FOR UPDATE` on the target event.
2. Both HTTP requests start concurrently.
3. The fixture observes both API database sessions waiting on a lock in `pg_stat_activity`.
4. The control transaction releases the lock.
5. The test awaits both responses and queries persisted state through a fresh context.

Distinct PostgreSQL `Application Name` values should identify control and API sessions. This retains the real HTTP, EF Core, Npgsql, and PostgreSQL path while making overlap deterministic.

### Risk #2: Authorization and Contact Projection

#### Authentication and route projections

All `/api` endpoints inherit JWT authorization ([`server\Program.cs:50-75`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Program.cs#L50-L75), [`server\Program.cs:140-144`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Program.cs#L140-L144)). Server-issued tokens contain both `sub` and `NameIdentifier`, and handlers derive the caller from those claims rather than a client-supplied user ID ([`server\Auth\TokenService.cs:34-46`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Auth/TokenService.cs#L34-L46), [`server\Auth\CurrentUser.cs:12-32`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Auth/CurrentUser.cs#L12-L32)).

| Route surface | Authorization basis | Permitted response |
|---|---|---|
| `GET /api/venues/{venueId}/events` | Any authenticated user | Event shell, accepted count, `IsOrganizer`, and only the caller's request status; no contacts, user IDs, or request IDs ([`server\Events\EventEndpoints.cs:201-280`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L201-L280)) |
| `GET /api/me/events` | Caller ID in organized/requested predicates | Only caller-related events and the caller's own request ID/status; no contacts ([`server\Events\EventEndpoints.cs:500-597`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L500-L597)) |
| `GET /api/events/{eventId}/join-requests` | Event ID and organizer ID in one query | Organizer sees request IDs, display keys, states, and timestamps; no requester user IDs or contacts ([`server\Events\EventEndpoints.cs:600-646`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L600-L646)) |
| `GET /api/events/{eventId}/contacts` | Organizer or accepted request relationship; event not cancelled | Organizer sees accepted participants only; accepted participant sees organizer only; participants never see one another ([`server\Events\EventEndpoints.cs:650-728`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L650-L728)) |
| Accept/reject/remove/cancel | Event/request tuple plus organizer ownership | Authorized transition result; unauthorized, cross-event, fabricated, and missing identifiers are cloaked as `404` ([`server\Events\EventEndpoints.cs:731-783`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L731-L783), [`server\Events\EventEndpoints.cs:1069-1150`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L1069-L1150)) |
| Join/leave | Caller relationship only | Join returns only the caller's request DTO; accepted caller may leave; callers without a relationship receive `404`, while non-accepted own states receive typed `409` |

The contact DTO is deliberately distinct from credential email. Contact fields originate from separately validated user properties ([`server\Data\Entities\User.cs:11-35`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Data/Entities/User.cs#L11-L35), [`shared\ChoNaBojo.Validation\AuthValidation.cs:35-66`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/shared/ChoNaBojo.Validation/AuthValidation.cs#L35-L66)).

#### Role and request-state oracle

| Caller | Event shell and own state | Organizer request queue | Contacts | Privileged mutations |
|---|---|---|---|---|
| Organizer | Visible; `IsOrganizer = true` | All requests, IDs, pseudonyms, states; no contacts/user IDs | Accepted participants only | Allowed subject to event/request state |
| Accepted participant | Visible; own `Accepted` state/request ID | `404` | Organizer only | Leave allowed; organizer operations `404` |
| Pending requester | Visible; own `Pending` state/request ID | `404` | `404` | Leave returns `409 participant_not_accepted`; organizer operations `404` |
| Rejected requester | Visible; own historical state/request ID | `404` | `404` | Own resolved-state conflicts only; organizer operations `404` |
| Removed requester | Visible; own historical state/request ID | `404` | `404` | Own resolved-state conflicts only; organizer operations `404` |
| Left requester | Visible; own historical state/request ID | `404` | `404` | May re-request; organizer operations `404` |
| Stranger | Event shell only; no request status | `404` | `404` | May request to join; all organizer operations and leave return `404` |
| Unauthenticated caller | No event data | `401` | `401` | `401` |

The PRD establishes authentication and relationship-based disclosure, but not the `404` versus `403` policy (`context\foundation\prd.md:42-45,135-162`). The 404 cloak comes from the approval/contact plan ([`context\archive\2026-09-07-approval-and-contact-reveal\plan.md:293-312`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/plan.md#L293-L312)) and is consistently implemented for privileged routes.

#### Negative assertions must use independent markers

Fixtures should assign unmistakable, unique marker values to every user's login email, shareable email, phone, and communicator handle. Tests should inspect raw JSON and assert that forbidden marker values and keys are absent. This avoids rebuilding the same relationship predicate inside the assertion.

Minimum API matrix:

| Test family | Required signal |
|---|---|
| Authentication barrier | Every Events route returns `401` without a valid bearer token and exposes no event/contact marker |
| Venue listing | All authenticated roles receive the same event shell, but only literal expected caller-relative flags/state; no request ID, user ID, login email, contact field, password, or token key |
| My-events isolation | Each caller receives only events they organized or requested; foreign request GUIDs and all contact markers are absent |
| Organizer queue | Organizer receives all request states without contacts/user IDs; every other identity, a cross-event tuple, and a fabricated identifier receive the same cloaked outcome |
| Contacts | Organizer receives accepted participants only; each accepted participant receives organizer only; pending, rejected, removed, left, and stranger identities receive the same `404` body as a missing event |
| Unauthorized mutations | Non-organizers cannot accept, reject, remove, or cancel; subsequent authorized reads prove no state changed |
| Leave states | Accepted becomes `Left`; pending/rejected/removed/left receives `409 participant_not_accepted`; stranger/missing receives `404` |
| Contact self-publish guard | Contact-like title/description values return `400`, while safe text remains visible without contact markers |

The last case protects the accepted lesson that public free text can bypass contact gating. The guard is implemented in [`shared\ChoNaBojo.Validation\ContactPatternGuard.cs:8-51`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/shared/ChoNaBojo.Validation/ContactPatternGuard.cs#L8-L51) and enforced by [`shared\ChoNaBojo.Validation\EventValidation.cs:50-71`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/shared/ChoNaBojo.Validation/EventValidation.cs#L50-L71).

#### Privacy-sensitive inconsistencies

- Accept/reject/remove/cancel lock an event by ID before applying organizer ownership. Bodies are cloaked, but an unauthorized caller can contend on an existing event row and create a timing distinction from a nonexistent ID. This conflicts with the historical wording that cases should be indistinguishable including timing order.
- The contacts query projects organizer contact values into server memory before completing the entitlement branch. No wire leak exists, but the query is broader than least privilege.
- A missing or invalid user-ID claim throws instead of producing a controlled authorization response. Server-issued tokens always contain a valid claim, so this is a malformed/misconfigured issuer case rather than an ordinary caller path.
- The server permits organizer/accepted-participant contact access after `Closed` but denies it after `Cancelled`. The later lifecycle review explicitly selected that policy ([`context\archive\2026-09-11-event-lifecycle-ops\reviews\impl-review.md:36-51`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-11-event-lifecycle-ops/reviews/impl-review.md#L36-L51)). The app's history view does not currently expose the server capability, so API tests can pin the server contract while the client-accessibility mismatch remains separate work.

### Phase 1 Test Infrastructure

#### Current readiness

The server and shared projects target `net10.0` with nullable reference types and implicit usings enabled ([`server\server.csproj:4-7`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/server.csproj#L4-L7)). The server currently references EF Core Design 10.0.3 and Npgsql EF provider 10.0.3 ([`server\server.csproj:14-22`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/server.csproj#L14-L22)).

There are no test projects, test files, API factory, database fixture, reset mechanism, or solution test entries. The solution contains only the app, server, and three shared projects ([`solutions\ChoNaBojo.slnx:143-148`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/solutions/ChoNaBojo.slnx#L143-L148)). Existing workflows publish application artifacts and do not run tests; CI enforcement belongs to rollout Phase 4 rather than this change.

The production model and migrations require PostgreSQL-specific behavior and PostGIS ([`server\Data\ChoNaBojoContext.cs:104-105`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Data/ChoNaBojoContext.cs#L104-L105)). EF InMemory or SQLite would erase the lock, transaction, raw SQL, extension, and provider behavior that supplies Risk #1's signal.

#### Recommended project split and packages

Use two `net10.0` projects so unit tests remain runnable without Docker:

| Project | Purpose | Initial dependencies |
|---|---|---|
| `tests\ChoNaBojo.UnitTests` | Pure shared validation and policy tests | `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4 |
| `tests\ChoNaBojo.Server.IntegrationTests` | HTTP, authorization, EF Core, transaction, and PostgreSQL tests | Same xUnit packages, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Testcontainers.PostgreSql` 4.15.0, optional `Respawn` 7.0.0 |

These versions match the installed .NET 10 template/runtime and current stable package evidence gathered on 2026-09-13. The implementation plan should verify them during restore and record any resolver-selected adjustments. Tests should reuse the production Npgsql provider rather than install an alternate EF provider.

Current authoritative patterns:

- [Microsoft ASP.NET Core integration testing](https://github.com/dotnet/docs/blob/main/docs/architecture/modern-web-apps-azure/test-asp-net-core-mvc-apps.md): `WebApplicationFactory`, `TestServer`, and a public partial `Program`.
- [EF Core testing strategy](https://github.com/dotnet/entityframework.docs/blob/main/entity-framework/core/testing/choosing-a-testing-strategy.md): avoid InMemory when relational transactions, raw SQL, and provider behavior matter.
- [Testcontainers for .NET PostgreSQL module](https://github.com/testcontainers/testcontainers-dotnet/blob/develop/docs/modules/postgres.md): xUnit-managed disposable PostgreSQL fixtures.

#### Required host and configuration seams

Top-level `Program` is currently internal. Add `public partial class Program;` after `app.Run()` so `WebApplicationFactory<Program>` can address it ([`server\Program.cs:146`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Program.cs#L146)).

The factory must inject these values before host startup:

- `ConnectionStrings:AppDb`
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:SigningKey`
- non-secret Firebase placeholders sufficient for option validation

The test host should remove `PushDeliveryWorker` and `EventAutoCloseWorker`. Both start with the application and can independently read or mutate the same database ([`server\Push\PushDeliveryWorker.cs:19-37`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Push/PushDeliveryWorker.cs#L19-L37), [`server\Events\EventAutoCloseWorker.cs:23-62`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventAutoCloseWorker.cs#L23-L62)).

Use real production JWTs with fixed test user GUIDs and a test-only signing key. This exercises the production claim contract without introducing a divergent test authentication handler.

#### Database isolation

The practical Phase 1 default is one migrated PostGIS container per serialized integration-test collection, with mutable tables reset between tests. Preserve `__EFMigrationsHistory`; seed only deterministic users, venues, events, and requests needed by each case. Do not wrap concurrency tests in one ambient rollback transaction because the API requests need separate visible connections and real locks.

`Respawn` can reset shared mutable state, but explicit SQL is acceptable if the reset list stays small and intentional. Database-per-test is stronger but more expensive; use it only if shared-container reset proves unreliable. Do not use the shared development/Supabase database for destructive, concurrent integration tests.

The exact PostGIS image tag remains a plan decision. Production migrations need PostGIS, so plain `postgres` is not sufficient. The current workstation has Docker CLI but no reachable Linux Docker engine; implementation can create the harness, but real integration execution remains blocked until a compatible engine is running.

#### Unit-test scope

Unit tests add independent signal for pure rules:

- parameterized contact-pattern examples and legitimate sports text through `ContactPatternGuard`;
- registration contact requirements and communicator pairing through `AuthValidation`;
- participant limit and event-time validation through shared validation/constants.

Do not unit-test copied organizer/participant predicates, mocked `DbSet` queries, DTO constructors, or the private slot-claim helper. Those would mirror implementation or remove PostgreSQL behavior.

## Code References

- [`server\Events\EventEndpoints.cs:787-1002`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L787-L1002) - Shared transaction, event lock, request transition, persistence, and commit.
- [`server\Events\EventEndpoints.cs:1162-1257`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L1162-L1257) - Capacity count and join/auto-accept response mapping.
- [`server\Events\EventEndpoints.cs:600-728`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L600-L728) - Organizer queue and pairwise contact projections.
- [`server\Events\EventEndpoints.cs:1069-1150`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Events/EventEndpoints.cs#L1069-L1150) - Remove/leave relationship and state checks.
- [`shared\ChoNaBojo.Contracts\DTOs\EventDTOs.cs:41-121`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs#L41-L121) - Event, request, queue, contact, and conflict response boundaries.
- [`server\Data\ChoNaBojoContext.cs:348-403`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Data/ChoNaBojoContext.cs#L348-L403) - Join-request status constraints and indexes.
- [`server\Auth\TokenService.cs:26-52`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Auth/TokenService.cs#L26-L52) - Production JWT claims usable by deterministic test identities.
- [`server\Program.cs:17-146`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/Program.cs#L17-L146) - Startup configuration, authentication, hosted services, endpoint group, and host seam.
- [`server\server.csproj:4-22`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/server/server.csproj#L4-L22) - Target framework and production package versions.

## Architecture Insights

- Capacity safety is a shared server-only lock protocol, correctly kept out of dependency-free shared projects. Manual and auto-accept paths converge before mutating accepted state.
- The event row is the aggregate lock root. Any future accepted-state writer must lock it before reading capacity or changing request state.
- Contact privacy is projection-based and relationship-specific, not a blanket "authenticated may view" rule. Tests must cover identities and states, not only endpoint status codes.
- Pairwise contact projection deliberately prevents accepted participants from discovering one another.
- 404 cloaking is an API convention established by historical change artifacts, not an explicit PRD rule. Timing behavior currently weakens the cloak even though response bodies remain indistinguishable.
- The test architecture must preserve production boundaries: HTTP host, real JWT validation, EF Core/Npgsql, real PostgreSQL/PostGIS, and separate connections. Pure validation remains suitable for fast unit tests.
- Hosted background workers are part of the production host and must be explicitly excluded from deterministic API/database fixtures.

## Historical Context

- The data-layer plan proposed disposable PostGIS/Testcontainers integration testing but did not implement it ([`context\archive\2026-07-12-data-layer-foundation\plan.md:274`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-07-12-data-layer-foundation/plan.md#L274)).
- Event creation explicitly deferred a committed test harness ([`context\archive\2026-09-02-event-creation\plan.md:428`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-02-event-creation/plan.md#L428)).
- The listing/join slice intentionally excluded organizer/requester IDs and contact fields from event discovery ([`context\archive\2026-09-06-event-listing-and-join-request\plan.md:17-35`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-06-event-listing-and-join-request/plan.md#L17-L35)).
- Approval/contact reveal changed auto-accept from deferred pending state to inline transactional acceptance and adopted 404 cloaking plus pairwise contact projection ([`context\archive\2026-09-07-approval-and-contact-reveal\plan.md:134-177`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/plan.md#L134-L177), [`context\archive\2026-09-07-approval-and-contact-reveal\plan.md:293-328`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/plan.md#L293-L328)).
- Its plan review caught an accept-versus-reject last-writer-wins design and required all resolutions to share the event lock ([`context\archive\2026-09-07-approval-and-contact-reveal\reviews\plan-review.md:31-38`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/reviews/plan-review.md#L31-L38)).
- The implementation review closed the race by static inspection but recorded that no automated concurrency suite existed ([`context\archive\2026-09-07-approval-and-contact-reveal\reviews\impl-review.md:38-51`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/reviews/impl-review.md#L38-L51)).
- Lifecycle review later retained contact entitlement after closure but revoked it on cancellation ([`context\archive\2026-09-11-event-lifecycle-ops\reviews\impl-review.md:36-51`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-11-event-lifecycle-ops/reviews/impl-review.md#L36-L51)).

## Related Research

- `context\foundation\test-plan.md` - Current risk map, Phase 1 rollout contract, and quality strategy. This file is modified in the working tree at research time, so references in this document intentionally use local line numbers rather than a stale commit permalink.
- [`context\archive\2026-09-07-approval-and-contact-reveal\research.md`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-07-approval-and-contact-reveal/research.md) - Original approval and contact-boundary codebase research.
- [`context\archive\2026-09-11-event-lifecycle-ops\research.md`](https://github.com/Sarnapa/cho-na-bojo/blob/4189ab75e42ff8a043352a4ad51a3870844c3547/context/archive/2026-09-11-event-lifecycle-ops/research.md) - Lifecycle and post-acceptance relationship research.

## Open Questions

1. Which exact PostGIS image tag and PostgreSQL major should the fixture pin?
2. Should integration isolation use one serialized container with Respawn, explicit SQL reset, or a database/schema per test? A serialized shared container is the recommended starting point.
3. Should the current EF Design and local `dotnet-ef` 10.0.3 pins be normalized to EF Core 10.0.4, which Npgsql 10.0.3 resolves?
4. Should the test host use a dedicated `Testing` environment, and how should HTTPS redirection be disabled without adding a production behavior flag?
5. Should workers be removed only in `WebApplicationFactory`, or should production expose an explicit hosted-worker registration seam?
6. Does the product intend 404 cloaking to include timing indistinguishability? The current lock-before-authorization sequence leaks an event-existence timing signal under contention.
7. Should malformed-but-cryptographically-valid JWTs with a missing/invalid subject produce `401` instead of an unhandled exception?
8. Should closed-event contacts remain part of the Phase 1 API oracle despite the app not exposing them after refresh? The latest server lifecycle decision says yes.
9. What typed HTTP response should represent deadlock, lock-timeout, or connection failure? No current product contract exists, so Phase 1 tests should not invent one.
