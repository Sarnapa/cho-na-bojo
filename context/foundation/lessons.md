# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Persisted enums must be guarded at both the application and database layers

- **Context**: server/Auth/AuthEndpoints.cs (register validation) and server/Data/ChoNaBojoContext.cs (User model config); enum property CommunicatorPlatform.
- **Problem**: Validation only checked `HasValue`, and the column had no CHECK constraint. Any integer (e.g. 99) would bind to the nullable enum and persist, storing a value that maps to no defined enum member — silent data corruption.
- **Rule**: For any enum persisted to the database, guard it at BOTH layers: (1) application-side validation with `Enum.IsDefined(value)` before accepting input, AND (2) a DB `CHECK` constraint restricting the column to the defined integer values (e.g. `IN (1,2,3)`), added via migration. Neither layer alone is sufficient — validation protects UX, the constraint protects the data at rest.
- **Applies to**: EF Core entities with enum properties stored as integers.
