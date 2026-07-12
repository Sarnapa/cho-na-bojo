# Npgsql reference for F-01 (data-layer-foundation)

> Fetched via Context7 MCP (`/npgsql/efcore.pg`, `/websites/npgsql`) on 2026-07-12.
> Scope: what F-01 needs — Supabase Postgres connection, EF Core provider wiring, IDENTITY keys, migrations, and seeding of `Sports` / `Venues` / `VenueSports`.

## 1. Packages

```bash
dotnet add server package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add server package Microsoft.EntityFrameworkCore.Design   # for migrations
```

## 2. DbContext registration (DI, minimal API)

```csharp
// Program.cs
builder.Services.AddNpgsql<ChoNaBojoContext>(
    builder.Configuration.GetConnectionString("Default"),
    npgsql => npgsql.SetPostgresVersion(15, 0));

// or, equivalently:
builder.Services.AddDbContext<ChoNaBojoContext>(opts =>
    opts.UseNpgsql(connectionString));
```

`UseNpgsql(connectionString, Action<NpgsqlDbContextOptionsBuilder>?)` is the entry point.

> **Hard rule:** the connection string lives in **user-secrets / environment variables**, never in `appsettings.json`.

## 3. Connection string for Supabase (hosted → needs SSL)

```
Host=<pooler-host>;Port=6543;Database=postgres;Username=<user>;Password=<pw>;SSL Mode=Require;Pooling=true;Maximum Pool Size=<n>
```

- **SSL Mode**: default is `Prefer` (enables SSL but does not enforce or validate certs).
  Supabase requires TLS → use `Require`, or `VerifyFull` with the CA cert for
  full man-in-the-middle protection.
- **Pooling params** (from `connection-string-parameters.html`):
  - `Pooling` (bool, default `true`)
  - `Minimum Pool Size` (default `0`), `Maximum Pool Size` (default `100`)
  - `Connection Idle Lifetime` (default `300s`), `Connection Lifetime` (default `3600s`)
- On Supabase's **transaction pooler (pgbouncer, port 6543)** keep Npgsql pooling modest
  and avoid prepared-statement caching conflicts (transaction pooling does not preserve
  session-level prepared statements).

Programmatic construction via `NpgsqlConnectionStringBuilder` (properties: `ApplicationName`,
`CommandTimeout`, `SslMode`, `Pooling`, `MaxPoolSize`, ...).

## 4. IDENTITY keys (for `Sports` / `Venues` PKs)

Npgsql uses IDENTITY columns by default (PostgreSQL 10+).

```csharp
modelBuilder.Entity<Sport>()
    .Property(s => s.Id)
    .UseIdentityByDefaultColumn(seed: 1, increment: 1)
    .HasIdentityOptions(minValue: 1, maxValue: long.MaxValue, cycle: false);
```

> The roadmap requires sports to be referenced by **stable id/code**. Prefer a fixed
> `Code` string column plus **explicit `Id` values in the seed** (deterministic across
> environments) rather than relying on DB-generated identities for seeded reference rows.

`UseSerialColumns()` exists but is **deprecated** since PostgreSQL 10 — use `UseIdentityColumns()`.

## 5. Migrations

```bash
dotnet ef migrations add InitialSeed --project server
dotnet ef database update --project server
```

## 6. Seeding (`Sports`, `Venues`, `VenueSports`)

`HasData` is a **core EF Core feature (2.1+), not Npgsql-specific**. It is *model-managed
data*: migrations compute the insert/update/delete needed to reach the seed state, and the
migration script is generated **without connecting to the database**. Details below are from
the official EF Core `data-seeding` docs.

### 6a. How model-managed seeding behaves (`HasData`)

- Seeding is declared in `OnModelCreating` as part of model configuration.
- Migrations diff the seed set between versions and emit `INSERT`/`UPDATE`/`DELETE`.
- Runs at **`dotnet ef database update` / migration apply time**, not at app startup.

### 6b. Limitations (drive our seed design)

- **Primary key must be specified explicitly**, even when normally DB-generated — the PK is
  used to detect changes between migrations.
- **Changing a seeded row's PK deletes the old row and inserts a new one** (not an update).
  → Lock the `Sports` ids permanently; treat them as stable contract keys.
- No FK references to other seeded rows can be resolved for you — **set FK values explicitly**.

### 6c. `Sports` — locked 10-entry list (FR-003)

```csharp
modelBuilder.Entity<Sport>(b =>
{
    b.Property(x => x.Code).IsRequired();
    b.HasData(
        new Sport { Id = 1,  Code = "football",       Name = "Football" },
        new Sport { Id = 2,  Code = "basketball",     Name = "Basketball" },
        new Sport { Id = 3,  Code = "volleyball",     Name = "Volleyball" },
        new Sport { Id = 4,  Code = "tennis",         Name = "Tennis" },
        new Sport { Id = 5,  Code = "running",        Name = "Running" },
        new Sport { Id = 6,  Code = "cycling",        Name = "Cycling" },
        new Sport { Id = 7,  Code = "rollerblading",  Name = "Rollerblading" },
        new Sport { Id = 8,  Code = "gym",            Name = "Gym" },
        new Sport { Id = 9,  Code = "street_workout", Name = "Street workout" },
        new Sport { Id = 10, Code = "swimming",       Name = "Swimming" });
});
```

### 6d. `Venues` — Warsaw seed with explicit FKs (Non-Goals §1)

```csharp
// FK values are set explicitly — EF does not resolve navigation references in seed data.
modelBuilder.Entity<Venue>().HasData(
    new Venue { Id = 1, Name = "…", /* lat, lng, address … */ });
```

### 6e. `VenueSports` — many-to-many via the explicit join entity

For many-to-many, EF Core seeds through the **join entity**, using an **anonymous object**
to supply shadow-state / FK properties. There are two supported shapes:

**Option A — explicit join entity class `VenueSport`** (recommended for this project, since
downstream code references the join directly):

```csharp
modelBuilder.Entity<VenueSport>().HasData(
    new { VenueId = 1, SportId = 1 },
    new { VenueId = 1, SportId = 3 });
```

**Option B — implicit join configured inline via `UsingEntity`** (per official docs, the
join can still be seeded even without a dedicated class):

```csharp
modelBuilder.Entity<Venue>(b =>
{
    b.HasMany(v => v.Sports)
     .WithMany(s => s.Venues)
     .UsingEntity(
        "VenueSport",
        r => r.HasOne(typeof(Sport)).WithMany().HasForeignKey("SportId").HasPrincipalKey(nameof(Sport.Id)),
        l => l.HasOne(typeof(Venue)).WithMany().HasForeignKey("VenueId").HasPrincipalKey(nameof(Venue.Id)),
        je =>
        {
            je.HasKey("VenueId", "SportId");
            je.HasData(
                new { VenueId = 1, SportId = 1 },
                new { VenueId = 1, SportId = 3 });
        });
});
```

> Key correction vs. the earlier draft: an implicit join table **can** be seeded — but only
> by configuring it through `UsingEntity(...).HasData(...)`. You cannot `HasData` a pure
> skip-navigation without that configuration. Prefer **Option A** here for clarity.

### 6f. Alternative: runtime seeding (`UseSeeding` / `UseAsyncSeeding`)

For data that shouldn't be tracked as model-managed (e.g. large/volatile venue imports),
EF Core (8+) offers `UseSeeding`/`UseAsyncSeeding` on the options builder — runs at
`EnsureCreated`/`Migrate` time with normal DB access and upsert-style guards:

```csharp
optionsBuilder
    .UseNpgsql(connectionString)
    .UseSeeding((context, _) => { /* check-then-add */ context.SaveChanges(); })
    .UseAsyncSeeding(async (context, _, ct) => { /* check-then-add */ await context.SaveChangesAsync(ct); });
```

For F-01's small, fixed reference set, **`HasData` (model-managed) is the right choice**;
`UseSeeding` is noted only as the escape hatch if the Warsaw venue list grows large.

## Sources

- `/npgsql/efcore.pg` — `UseNpgsql`, `AddNpgsql`, DbContext setup, `UseIdentityByDefaultColumn`, `HasIdentityOptions`, value generation.
- `/websites/npgsql` — connection string parameters, pooling, SSL/TLS (`SSL Mode`: Disable/Allow/Prefer/Require/VerifyCA/VerifyFull), `NpgsqlConnectionStringBuilder`.
- `/dotnet/entityframework.docs` & `/websites/learn_microsoft_en-us_ef_core` — `data-seeding.md`: `HasData` for simple entities, FK relationships, many-to-many join entities (`UsingEntity(...).HasData(...)`), owned types, model-managed-data limitations, and `UseSeeding`/`UseAsyncSeeding`.
