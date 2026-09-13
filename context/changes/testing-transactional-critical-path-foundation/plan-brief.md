# Transactional Critical-Path Test Foundation — Plan Brief

> Full plan: `context\changes\testing-transactional-critical-path-foundation\plan.md`
> Research: `context\changes\testing-transactional-critical-path-foundation\research.md`

## What & Why

ChoNaBojo needs its first automated test foundation around the two most expensive failure classes: concurrent acceptance overfilling an event and authenticated callers crossing relationship/contact boundaries. The plan adds fast pure-rule tests plus production-shaped HTTP integration tests against disposable PostgreSQL/PostGIS, protecting behavior rather than mirroring implementation.

## Starting Point

The API already uses an event-row `FOR UPDATE` protocol for every transition to `Accepted` and relationship-specific response projections for privacy. There are no test projects, however, and neither the lock protocol nor the full multi-identity privacy matrix has executable regression protection.

## Desired End State

Unit tests run quickly without Docker. Integration tests host the real minimal API, validate real signed JWTs, apply production migrations to an isolated PostGIS container, reset mutable state between scenarios, and exclude background workers.

All three final-slot race pairings prove exactly one accepted participant, while a full Events API identity matrix proves that callers receive only their own relationship state and permitted pairwise contacts.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| Test layers | Separate unit and real-database integration projects | Keeps pure feedback fast without weakening PostgreSQL/HTTP signal | Research |
| Database | One serialized `postgis/postgis:17-3.5` container | Production migrations require PostGIS and concurrency needs real locks | Research |
| Isolation | Respawn reset between committed API scenarios | Preserves real separate connections without container-per-test cost | Plan |
| API host | `WebApplicationFactory<Program>` with production JWT validation | Protects routing, auth, serialization, EF, and middleware together | Research |
| Background jobs | Remove both hosted workers in the test factory | Prevents independent outbox consumption and event closure | Research |
| 404 policy | Equal response status/body only | Protects the established cloak without adding timing-hardening scope | Plan |
| Invalid identity claim | Reject as 401 during bearer validation | Fails closed before endpoint code can throw | Plan |
| Privacy breadth | Full Events API surface | Protects disclosure and unauthorized state changes, not contacts alone | Plan |
| Contact lifecycle | Available after Closed; revoked after Cancelled | Preserves the latest accepted lifecycle contract | Research |
| Concurrency cases | Manual/manual, auto/auto, and mixed | Proves both writers converge on the same capacity protocol | Plan |
| Overlap proof | Control lock plus `pg_stat_activity` observer | A start barrier alone cannot prove both requests reached the lock | Research |
| Docker requirement | Required but explicitly blockable | Compilation or mocks cannot substitute for the real database signal | Plan |
| CI enforcement | Deferred to rollout Phase 4 | This phase establishes the local floor first | Research |

## Scope

**In scope:** Two `net10.0` xUnit projects; host/JWT seams; disposable migrated PostGIS with reset and worker isolation; pure-rule tests; the full Events API privacy/mutation matrix; three final-slot races; and Phase 1 cookbook updates.

**Out of scope:** Android/Appium/iOS; push delivery; lifecycle races; timing-indistinguishable 404s; operational database error contracts; external database fallback; CI gates; coverage targets; and mutation tooling.

## Architecture / Approach

`Unit tests -> shared validation/contracts`

`Integration HttpClient -> WebApplicationFactory<Program> -> minimal API -> EF Core/Npgsql -> disposable PostGIS`

The factory uses real signed JWTs, deterministic configuration, and no hosted workers; Respawn resets mutable state. Privacy scenarios use unique raw markers rather than copied predicates. Concurrency scenarios hold the event row, observe both API sessions blocked, release them, and assert winner-independent committed state.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Test host and PostGIS fixture | Projects, host seam, JWT hardening, migrations/reset, worker isolation | Fixture silently diverges from production or targets the wrong database |
| 2. Independent pure rules | Fast contact, registration, participant, and time oracles | Mirror tests encode current implementation instead of product behavior |
| 3. Privacy and authorization matrix | Marker-based protection across every Events route family | A forbidden field or mutation escapes one role/state combination |
| 4. Final-slot serialization | Deterministic three-pair race suite and canonical cookbook | Requests do not truly overlap or assertions pin a winner |

**Prerequisites:** .NET 10 SDK and a reachable Linux Docker engine for integration execution. Docker image retrieval must be possible before Phase 1 can pass.

**Estimated effort:** ~4 focused implementation sessions across 4 phases; Phase 3 is the largest matrix, and Phase 4 carries the highest fixture-debugging risk.

## Open Risks & Assumptions

- The local Docker CLI exists, but the Linux engine was unreachable during research; integration completion depends on restoring that environment.
- `postgis/postgis:17-3.5` is the committed fixture baseline because production's exact PostgreSQL major is undocumented.
- Respawn is expected to reset all mutable tables while preserving migrations and reference rows; if it proves unreliable, the implementation must fix the reset contract rather than weaken isolation.
- Response-level 404 cloaking is protected; contention-based timing differences remain accepted out of scope.

## Success Criteria (Summary)

- Unit tests pass without Docker and use independent literal oracles.
- Every Events route rejects unauthenticated or malformed identities and the full role/state matrix exposes no forbidden marker or hidden mutation.
- All three final-slot races pass deterministically and 20 consecutive runs never exceed capacity.
