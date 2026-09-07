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

## API error bodies must follow one recorded shape per status code

- **Context**: `server/Auth/AuthEndpoints.cs:67,93` returns conflicts as `Results.Conflict(new { message })` and `app/ChoNaBojoApp/Services/ApiService.cs:179-180` parses that anonymous shape; the S-03 event-creation plan introduces a structurally different, typed `EventConflictResponse` (`Code`, `Field`, `Message`) for 409 on `POST /api/events`.
- **Problem**: Two structurally different bodies for the same status code force the client to branch per endpoint, make error handling untestable as a unit, and let the API drift into competing conventions with no decision recorded anywhere — the next endpoint author picks whichever they happened to read first.
- **Rule**: For each error status code, the API has exactly one body shape, named as a shared Contracts DTO (not an anonymous object): `409` uses the typed conflict DTO with a stable machine-readable `Code`, an optional affected `Field`, and a human `Message`; `400` uses the existing `ValidationProblemResponse` — do not add a parallel RFC-7807 parser. When a new endpoint deliberately introduces a better shape than an older one, say so in the plan and mark the older shape legacy-to-be-aligned, so the divergence is a recorded, temporary decision rather than silent drift.
- **Applies to**: plan, plan-review, implement, impl-review

## User-authored free text becomes a contact-leak channel the moment it is shown to other users

- **Context**: `shared/ChoNaBojo.Contracts/DTOs/EventDTOs.cs:41-52`, `server/Events/EventEndpoints.cs:209-232`, `app/ChoNaBojoApp/Views/VenueEventsPage.xaml:312-334` — organizer-authored event `Title`/`Description` projected into the venue event listing and bound on the card.
- **Problem**: The listing projects `Title` and `Description` to every authenticated user, while `EventValidation` only checks blank/length. The product's hard privacy rule is that contact info reaches only approved participants, but an organizer typing a phone number or messenger handle into the description bypasses the acceptance-gated reveal entirely — and nothing stops a user pasting a third party's details there. The fields were created in a slice where only the author could see them; the later listing slice is what silently made them public, so no single change looked like it was introducing an exposure.
- **Rule**: When a change makes an existing user-authored free-text field visible to a wider audience, treat that change as the one introducing the exposure — not the change that created the field. Before shipping, either (a) add a contact-pattern guard in `shared/ChoNaBojo.Validation` enforced on the write path, or (b) record an explicit, dated deferral naming the slice that owns enforcement and the precondition (e.g. "before real users"). Never let a privacy-gated data class have an unguarded self-publish path.
- **Applies to**: plan, plan-review, implement, impl-review
- **Closure (2026-09-08)**: `approval-and-contact-reveal` closed the deferral with `shared/ChoNaBojo.Validation/ContactPatternGuard.cs`, enforced for event titles and descriptions by `EventValidation`.
