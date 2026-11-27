# Appendix: Persistence (EF Core 10 + PostgreSQL)

Detailed rules for mapping the domain to EF Core and PostgreSQL.

## DbContext per module

- One `DbContext` per module: `OperationsDbContext`, `RegistryDbContext`, etc.
- `HasDefaultSchema("operations")` declared in `OnModelCreating`.
- Own migration history table: `MigrationsHistoryTable("__ef_migrations_history", "operations")`.
- Never share a `DbContext` between modules.
- All modules live in one database, one schema each, behind one connection string `ConnectionStrings:Database` (locally the Docker Compose database `priamus_trips`).

## Configurations

- Only Fluent Configuration via `IEntityTypeConfiguration<T>`.
- Files in `Persistence/Configurations/{EntityName}Configuration.cs`.
- Apply via `modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`.
- No data annotations on domain entities.
- Every entity calls `ToTable("{name}", "{schema}")` explicitly.
- Every required relationship declares `.OnDelete(...)` explicitly.

## Naming conventions

| Element          | Convention                         | Example                             |
| ---------------- | ---------------------------------- | ----------------------------------- |
| Schema           | snake_case English                 | `operations`                        |
| Table            | snake_case plural English          | `trips`, `trip_attachments`         |
| Column           | snake_case English                 | `reference_number`, `created_at`    |
| Index            | `ix_{table}_{columns}`             | `ix_trips_driver_id_execution_date` |
| Foreign key      | `fk_{table}_{referenced}_{column}` | `fk_trip_attachments_trips_trip_id` |
| Check constraint | `chk_{table}_{rule}`               | `chk_trips_composition_trailer`     |
| Unique           | `uq_{table}_{columns}`             | `uq_trips_reference_number`         |

Apply via `.HasConstraintName(...)`, `.HasDatabaseName(...)`.

## Strongly-typed IDs

Each ID needs a value converter:

```csharp
public sealed class TripIdConverter : ValueConverter<TripId, Guid>
{
    public TripIdConverter() : base(id => id.Value, value => new TripId(value)) { }
}

// In configuration:
builder.Property(t => t.Id)
    .HasConversion(new TripIdConverter())
    .HasColumnName("id")
    .ValueGeneratedNever();
```

All converters in `Persistence/Converters/`.

## Enums

An enum is stored by the name of its C# member, spelled exactly as in code (`Tractor`, `StandaloneVehicle`), in a `text` column guarded by a check constraint listing every member. The value is then the same in C#, in the API (enums travel by name, `slices-and-handlers.md`), in the OpenAPI contract, in the frontend types and in SQL: no table to decode, and reordering the enum cannot change the meaning of stored rows. Renaming a member is a migration that rewrites the values and the constraint.

```csharp
builder.Property(v => v.Type)
    .HasConversion<string>()
    .HasColumnName("type")
    .IsRequired();

builder.ToTable("vehicles", "registry", t =>
    t.HasCheckConstraint("chk_vehicles_type", "type IN ('Tractor', 'Trailer', 'StandaloneVehicle')"));
```

Identity predates this rule and still stores `role` and `status` as integers (`docs/backlog.md`).

## Sealed types mapping

Two patterns:

### Pattern A — Owned with discriminator (when fields are queried)

Used for `VehicleComposition`, `Parameters`:

```csharp
builder.OwnsOne(t => t.VehicleComposition, vc =>
{
    vc.Property<int>("composition_type").IsRequired();
    vc.HasDiscriminator<int>("composition_type")
        .HasValue<VehicleComposition.TractorWithTrailer>(1)
        .HasValue<VehicleComposition.StandaloneTractor>(2)
        .HasValue<VehicleComposition.StandaloneVehicle>(3);

    vc.Property<VehicleId>("PrimaryVehicleId")
        .HasConversion(new VehicleIdConverter())
        .HasColumnName("primary_vehicle_id")
        .IsRequired();

    vc.Property<VehicleId?>("TrailerId")
        .HasConversion(new VehicleIdConverter())
        .HasColumnName("trailer_id");
});
```

### Pattern B — JSON serialization (when fields are never queried)

Used for `RateSnapshot`:

```csharp
builder.Property(t => t.RateSnapshot)
    .HasColumnName("rate_snapshot")
    .HasColumnType("jsonb")
    .HasConversion(new RateSnapshotJsonConverter())
    .IsRequired();
```

The JSON converter uses `System.Text.Json` (never Newtonsoft) with custom converters for sealed types stored inside (discriminator-based, e.g., `"type": "TieredOverage"`).

Never store sealed types in JSON when their fields need to be queried.

The pricing a trip keeps (`TripPricing`) is not stored this way: its total, its day or night and its rate are summed and filtered by the back-office trip list, so it is a `ComplexProperty` with one column per member (`TripConfiguration`).

## Querying inside a value object

A `ValueConverter<TVO, string>` (or any `ValueConverter<TVO, TStore>`) maps the
value object as a single atomic property in the EF Core model. Equality
comparisons and ordering on the whole VO translate naturally:

```csharp
query.Where(u => u.Username == knownUsername)   // ✅ translates
query.OrderBy(u => u.Username)                  // ✅ translates via converter
```

But the LINQ-to-SQL translator **cannot decompose member access** on a
converted property. Any expression like

```csharp
query.Where(u => u.Username.Value.Contains(needle))  // ❌ untranslatable
query.OrderBy(u => u.Username.Value)                 // ❌ untranslatable
```

fails at runtime with `InvalidOperationException: The LINQ expression … could
not be translated`. This is a known EF Core limitation (see efcore#33788).

### Pattern: `Unwrap` extension + `HasDbFunction`

Expose a query-only extension on the VO that gets routed through a `DbFunction`
translation. The extension throws when called client-side, mirroring the
`EF.Functions.*` convention:

```csharp
// Domain/Users/ValueObjects/Username.cs
public static class UsernameQueryExtensions
{
    /// <summary>
    /// Translated to the underlying <c>username</c> column in EF Core LINQ
    /// queries. Never call this method client-side — it throws.
    /// </summary>
    public static string Unwrap(this Username? username) =>
        throw new NotSupportedException(
            "Unwrap is a SQL-only translation marker. Use Username.Value for in-memory access.");
}
```

Register the translation in the owning `DbContext`'s `OnModelCreating`:

```csharp
modelBuilder
    .HasDbFunction(typeof(UsernameQueryExtensions)
        .GetMethod(nameof(UsernameQueryExtensions.Unwrap), [typeof(Username)])!)
    .HasTranslation(args => new SqlFunctionExpression(
        functionName: string.Empty,
        arguments: args,
        nullable: false,
        argumentsPropagateNullability: new[] { false },
        type: typeof(string),               // ← key: retypes the SQL expression as string
        typeMapping: null))
    .HasParameter("username")
        .HasStoreType("varchar(64)");       // ← key: bypasses the VO ValueConverter on the parameter
```

Use in queries — `Contains`, `StartsWith`, `EndsWith` compose as ordinary SQL `LIKE`:

```csharp
var needle = request.UsernameContains.Trim().ToLowerInvariant();
query = query.Where(u => u.Username.Unwrap().Contains(needle));
```

### Why each piece is needed

| Piece | Why |
|---|---|
| `HasStoreType` on the parameter | EF Core does not consult the property-level `ValueConverter` when binding `DbFunction` parameters (efcore#33788). Declaring the store type bypasses the converter and lets the provider send a plain string parameter. |
| `SqlFunctionExpression` wrapper with `type: typeof(string)` | Without this, `args[0]` keeps the VO's model type and downstream `string` operators (`Contains`, `LIKE`) refuse to compose. Wrapping retypes the SQL-side expression. |
| `functionName: string.Empty` | Produces `(column)` at the SQL level — i.e. the column wrapped in parentheses, no function emitted. Postgres tolerates the redundant parens; the result is an ordinary column reference that downstream operators consume normally. |
| `throw` in the extension body | Mirrors the `EF.Functions` convention. Client-side calls (e.g. accidentally filtering an already-materialized list) fail fast with a clear message rather than silently doing the right thing in memory while bypassing the database. |

### When to use this pattern

Only when a VO mapped through `ValueConverter` needs **partial-match** queries
(`Contains` / `StartsWith` / `EndsWith` / `Like`). Whole-value equality and
ordering already work without `Unwrap`.

For VOs that are queried this way frequently, consider whether the underlying
column should be a plain `string` with a domain factory (`Username.Create`)
called at write time instead. The `Unwrap` pattern is the right answer when
the VO **must** remain a distinct type in the domain model.

### Reference implementation

`Username.Unwrap` in `Identity` is the canonical example. See:
- `src/Modules/Identity/Priamus.Trips.Identity/Domain/Users/ValueObjects/Username.cs`
- `src/Modules/Identity/Priamus.Trips.Identity/Persistence/IdentityDbContext.cs` (`OnModelCreating`)
- `src/Modules/Identity/Priamus.Trips.Identity/Features/ListUsers/ListUsersHandler.cs`

## Decimal precision

| Type              | Precision             |
| ----------------- | --------------------- |
| Money amounts     | `HasPrecision(12, 2)` |
| Tons              | `HasPrecision(10, 3)` |
| Kilometers        | `HasPrecision(10, 2)` |
| Hours (if stored) | `HasPrecision(8, 2)`  |

```csharp
builder.Property(t => t.FinalAmount)
    .HasConversion(
        money => money.Amount,
        amount => new Money(amount, "EUR"))
    .HasColumnName("final_amount")
    .HasPrecision(12, 2)
    .IsRequired();
```

Currency hardcoded to "EUR" for MVP. Multi-currency support requires revisiting the converter.

## Concurrency

Every aggregate root has `Version: int`:

```csharp
builder.Property<int>("Version")
    .HasColumnName("version")
    .IsConcurrencyToken()
    .HasDefaultValue(0);
```

The aggregate increments it manually in modification methods. Persistence only configures it as token. There is no global handler: the handler catches `DbUpdateConcurrencyException` around its `SaveChangesAsync` and returns the aggregate's concurrency error, a Conflict that answers 409 (`slices-and-handlers.md`, "Unit of work").

The trip acts (start, end, delete) do not answer 409 on a lost save: they save through `OperationsDbContext.TrySaveTripAsync`, which clears the tracked changes and returns `false`, and the handler re-reads the trip once and answers through its normal paths (a trip already started or ended is answered as it is, a trip deleted meanwhile is not found). A second lost save on that one retry propagates (decided by Michele on 2026-11-25).

A write on an entity the user edited from a screen sends back the version it read (decided 2026-11-16). The handler sets it as the original value of the token before saving — `db.Entry(vehicle).Property(v => v.Version).OriginalValue = request.Version` — so a stale version fails the save with `DbUpdateConcurrencyException`. Every response that returns such an entity carries its `version`.

## Check constraints (defense in depth)

For critical invariants, add `HasCheckConstraint` even if the domain enforces them:

```csharp
builder.ToTable("trips", "operations", t =>
{
    t.HasCheckConstraint(
        "chk_trips_composition_trailer",
        "(composition_type = 'TractorWithTrailer' AND trailer_id IS NOT NULL) OR " +
        "(composition_type IN ('StandaloneTractor', 'StandaloneVehicle') AND trailer_id IS NULL)");
});
```

## Data access (no repositories)

Per [ADR 0009](../adr/0009-no-repository-pattern.md), the project does **not** use the classic Repository pattern. Handlers depend on the module's `DbContext` directly, both for write operations and for hydrating aggregates needed by a command.

### What this means in practice

- **No `I{Aggregate}Repository` interfaces.** Do not create them, even for new aggregates. If you find yourself writing one, the convention has been broken — re-read ADR 0009 first.
- **Handlers inject the module's `DbContext` directly.** Example:

```csharp
internal sealed class ChangePasswordHandler(
    IdentityDbContext dbContext,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<Result<Unit>> HandleAsync(
        ChangePasswordRequest request,
        CancellationToken ct)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null) return UserErrors.NotFound;

        user.ChangePassword(request.NewHash, clock, currentUser.Id);
        await dbContext.SaveChangesAsync(ct);
        return Result.Success(Unit.Value);
    }
}
```

- **`DbContext` is registered scoped via DI** in the module's persistence registration extensions.
- **`DbContext` is not exposed outside its module.** Other modules talk to this one through Contracts interfaces, not by injecting its `DbContext`.
- **`SaveChangesAsync` is called by the handler itself**, at the end of the command. There is no central transaction filter.

### Reusable queries

When the same filter or projection is used across two or more slices, factor it out — but **not** into a repository, and only once the second slice exists: shared persistence is extracted from working slices, never written ahead of them ([ADR 0012](../adr/0012-slices-before-shared-persistence.md)). Options, in order of preference:

1. **Extension method on `IQueryable<T>`**. Best for read filters that compose:

```csharp
public static IQueryable<User> WhereActive(this IQueryable<User> q)
    => q.Where(u => u.Status == UserStatus.Active);

public static IQueryable<User> ByUsername(this IQueryable<User> q, Username username)
    => q.Where(u => u.Username == username);

// usage:
var user = await dbContext.Users
    .WhereActive()
    .ByUsername(username)
    .FirstOrDefaultAsync(ct);
```

Place these extension methods in the module's `Persistence/Queries/` folder.

2. **Read-only query service**. For more complex multi-aggregate read projections that don't compose well as extension methods. Define as a small interface (`IUserQueryService`) exposing specific methods (`GetActiveUserSummaryAsync`), implement against the `DbContext`. This is a _query_ service, not a repository — only reads, returns DTOs, never mutates and never returns aggregates.

3. **Inline in the handler**. Always acceptable when the query is used by exactly one slice.

### Tests

Per `context.md` §6, persistence is tested against real PostgreSQL via Testcontainers. There are no mocks of `DbContext` or `DbSet<T>`. Handlers that touch persistence are tested as integration tests; pure domain logic (aggregate methods, value objects) is tested as unit tests with no persistence at all.

## Domain event persistence

Aggregates emit domain events (`UserPasswordChanged`, `TripEnded`, etc.) inside their state-change methods. These events serve two distinct purposes which are handled by two distinct mechanisms.

### Default: synchronous audit log

For events that record "what happened, by whom, when" — accountability, forensics, conformity — the default mechanism is a **synchronous audit log table** populated via an `ISaveChangesInterceptor`. The audit row is written in the same transaction as the aggregate state change, guaranteeing atomicity.

This covers the requirement in `context.md` §6 ("audit log of sensitive operations") and PRD RNF-08 without introducing asynchronous infrastructure.

Schema per module:

```sql
CREATE TABLE identity.audit_log (
    id              UUID PRIMARY KEY,
    occurred_at     TIMESTAMPTZ NOT NULL,
    actor_id        UUID,
    subject_id      UUID NOT NULL,
    event_type      VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL
);

CREATE INDEX ix_audit_log_subject_id_occurred_at
    ON identity.audit_log(subject_id, occurred_at DESC);

CREATE INDEX ix_audit_log_event_type_occurred_at
    ON identity.audit_log(event_type, occurred_at DESC);
```

Conventions:

- `event_type` is a stable string (`"identity.user.password_changed"`, `"operations.trip.registered"`). It is a de-facto compatibility contract — changing it breaks downstream consumers of the audit table. Collect the strings in an `AuditEventTypes` static class per module.
- `subject_id` is the identity of the entity the event is about. For events on a child aggregate, prefer the parent's id when there is a natural "owner" (e.g. for refresh-token events, `subject_id` is the `UserId`, not the `RefreshTokenId`).
- `actor_id` is resolved from `ICurrentUser`. It is nullable: some events (e.g. failed login attempts) have no authenticated actor.
- `payload` is the full event serialized as JSON via `System.Text.Json`. Never truncate — audit value depends on completeness.
- An audit row records a change, not a request (decided 2026-11-20). A modification method that would leave the aggregate as it is — the same data, or the status it already has — is a no-op: no event, no version bump, so no row. The handler still sets the version it received as the token's original value, so a stale version is refused with 409 even when nothing would change.

Implementation: an `ISaveChangesInterceptor` registered on the module's `DbContext`. In `SavingChangesAsync`, the interceptor iterates `ChangeTracker.Entries<AggregateRoot>()`, dequeues their domain events, transforms them into `AuditLogEntry` instances, and adds them to the ChangeTracker. The base flush writes everything in one transaction.

```csharp
public sealed class AuditInterceptor(IClock clock, ICurrentUser currentUser)
    : ISaveChangesInterceptor
{
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct = default)
    {
        var context = eventData.Context!;

        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var entry = MapToAuditEntry(domainEvent, currentUser.IdOrNull);
                context.Set<AuditLogEntry>().Add(entry);
            }
            aggregate.ClearDomainEvents();
        }

        return ValueTask.FromResult(result);
    }

    // Also override the synchronous SavingChanges as a defensive measure
    // for callers that bypass the async path.

    private static AuditLogEntry MapToAuditEntry(IDomainEvent e, UserId? actor) =>
        e switch
        {
            UserPasswordChanged ev => AuditLogEntry.For(ev, actor, AuditEventTypes.UserPasswordChanged),
            // ... one branch per event in the module
            _ => throw new UnreachableException($"Unmapped domain event: {e.GetType().Name}")
        };
}
```

The interceptor lives inside the owning module at `Persistence/Interceptors/`. **Do not pre-extract** to `SharedKernel`. Registry, the second module, copies the mechanism (decided 2026-11-16: the event mapping differs per module and the shared part is small); the shared pieces are promoted to a shared persistence library when a third module needs them (`docs/backlog.md`).

### Opt-in: outbox for cross-module communication

The outbox pattern is used **only** when an event needs to reach a consumer outside the emitting module — another module's domain logic, an external integration, a webhook. It is not the default mechanism for domain events.

A module that has no cross-module consumers for its events **does not implement an outbox table or worker**. Identity in the MVP is an example: its seven events (`UserCreated`, `UserPasswordChanged`, ...) are recorded in the audit log only.

When a cross-module consumer is identified, the emitting module adds the outbox infrastructure:

```sql
CREATE TABLE {module}.outbox_messages (
    id              UUID PRIMARY KEY,
    occurred_at     TIMESTAMPTZ NOT NULL,
    event_type      VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL,
    processed_at    TIMESTAMPTZ,
    error           TEXT,
    attempt_count   INT NOT NULL DEFAULT 0
);

CREATE INDEX ix_outbox_messages_unprocessed
    ON {module}.outbox_messages(occurred_at)
    WHERE processed_at IS NULL;
```

The same `ISaveChangesInterceptor` is extended to also write outbox rows for events that have at least one cross-module subscriber. Audit log and outbox can coexist on the same event: the audit row records that the event happened; the outbox row drives the asynchronous dispatch. They are independent in purpose.

Dispatch is handled by an `OutboxProcessor` `BackgroundService` registered in `Host`, which polls unprocessed messages and invokes the registered handlers. At-least-once delivery: handlers must be idempotent.

The decision to add an outbox to a module is documented in an ADR, with the cross-module consumer(s) named.

## Migrations

Naming: `{Module}_{Change}`, the change in PascalCase. Examples: `Identity_Initial`, `Registry_Vehicles`, `Identity_EnumsByName`. No auto-generated timestamp-only names.

Generation, from the repository root:

```bash
dotnet ef migrations add Operations_Trips \
    --project src/Modules/Operations/Priamus.Trips.Operations \
    --context OperationsDbContext \
    --output-dir Persistence/Migrations
```

No `--startup-project`: the EF Core design package and the design-time factory (`IdentityDbContextFactory`, `RegistryDbContextFactory`) live in each module, so the module is its own startup project. The generated migration, designer and snapshot use block namespaces: convert them to file-scoped namespaces (the build treats IDE0161 as an error).

Rules:

- Forward-only: never edit a migration after merging to main.
- For destructive changes (drop column, rename table), produce two migrations across two releases: additive first, cleanup second.
- Apply: Development only, at host startup, through a public startup extension of the module (`MigrateIdentityDatabaseAsync` for Identity) that the host calls after `builder.Build()`; prod via dedicated pre-deploy job. Never `EnsureCreated()`. After the migration step the host runs the bootstrap administrator step (`BootstrapAdministratorAsync` for Identity) in every environment: it creates the first `Admin` from `Identity:BootstrapAdmin` when no user exists, so in Production the schema must already be migrated or the host does not start.

## Design-time factory

Required for migration CLI to work:

```csharp
public sealed class OperationsDbContextFactory : IDesignTimeDbContextFactory<OperationsDbContext>
{
    public OperationsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=priamus_trips_design;Username=designtime;Password=designtime",
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "operations"))
            .Options;
        return new OperationsDbContext(options);
    }
}
```

Connection string is dummy; only used to scaffold migrations.

## Query performance

- `.AsNoTracking()` on every read-only query. With direct `DbContext` access, this is now the responsibility of each handler — there is no central layer that applies it automatically.
- Let the database aggregate: `CountAsync`/`AnyAsync`/`MinAsync`/`MaxAsync`/`SumAsync`, never `ToListAsync()` followed by `.Count`/`.Any()` on the client, and never rows materialised to compute a number nobody reads. When a domain type's only factory leaves no way to obey this, that is a modelling problem (see the value-object rule in `domain-modeling.md`): say so rather than load silently. Aggregates over a converted column can degrade to client evaluation or fail at runtime, so assert the emitted SQL in a test instead of assuming the translation.
- `.AsSplitQuery()` when multiple Include cause cartesian explosion warning.
- Indexes declared explicitly via `.HasIndex(...)`. Never rely on EF defaults.
- Partial indexes for soft-deleted filters: `.HasFilter("soft_deleted_at IS NULL")`.
- No N+1: verify via `Microsoft.EntityFrameworkCore.Diagnostics` warnings as errors in tests.
