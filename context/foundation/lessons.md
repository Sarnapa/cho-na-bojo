# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Persisted enums must be guarded at both the application and database layers

- **Context**: server/Auth/AuthEndpoints.cs (register validation) and server/Data/ChoNaBojoContext.cs (User model config); enum property CommunicatorPlatform.
- **Problem**: Validation only checked `HasValue`, and the column had no CHECK constraint. Any integer (e.g. 99) would bind to the nullable enum and persist, storing a value that maps to no defined enum member — silent data corruption.
- **Rule**: For any enum persisted to the database, guard it at BOTH layers: (1) application-side validation with `Enum.IsDefined(value)` before accepting input, AND (2) a DB `CHECK` constraint restricting the column to the defined integer values (e.g. `IN (1,2,3)`), added via migration. Neither layer alone is sufficient — validation protects UX, the constraint protects the data at rest.
- **Applies to**: EF Core entities with enum properties stored as integers.

## Keep shared app/server code in three dependency-free projects

- **Context**: Any change that decides where a code element belongs when it is (or could be) shared between the mobile app (`app/ChoNaBojoApp`) and the server (`server/`) — i.e. what goes into the `shared/` projects: `ChoNaBojo.Contracts`, `ChoNaBojo.Utils`, `ChoNaBojo.Validation`.
- **Problem**: Without clear placement rules, wire DTOs leak persistence/framework types across the boundary (e.g. the `CommunicatorPlatform` enum living in server EF entities coupled the client to the data layer), business rules like the 2–300 participant limit get duplicated and drift between app and server, and shared projects accidentally take dependencies on ASP.NET Core/EF Core/MAUI — breaking the privacy boundary and causing client/server validation mismatches.
- **Rule**: Put cross-boundary code in three dependency-free net10.0 projects and nowhere else: `ChoNaBojo.Contracts` (DTOs, cross-boundary enums, domain constants — never EF entities or secrets like `PasswordHash`), `ChoNaBojo.Utils` (pure stateless helpers), and `ChoNaBojo.Validation` (validators over Contracts DTOs, returning a framework-neutral `ValidationResult`, never `IResult`). Keep the dependency direction acyclic: Contracts and Utils are leaves; Validation → Contracts + Utils; server and app reference all three. No shared project may ever reference ASP.NET Core, EF Core, Npgsql, or MAUI. Share pure invariants/validation only; keep stateful or privileged logic (per-event authorization, contact-reveal gating, transactional slot claims) server-only.
- **Applies to**: plan, plan-review, implement, impl-review
