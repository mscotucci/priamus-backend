# Appendix: Persistence (EF Core 10 + PostgreSQL)

Detailed rules for mapping the domain to EF Core and PostgreSQL.

## DbContext per module

- One `DbContext` per module: `OperationsDbContext`, `RegistryDbContext`, etc.
- `HasDefaultSchema("operations")` declared in `OnModelCreating`.
- Own migration history table: `MigrationsHistoryTable("__ef_migrations_history", "operations")`.
- Never share a `DbContext` between modules.

## Configurations

- Only Fluent Configuration via `IEntityTypeConfiguration<T>`.
- Files in `Persistence/Configurations/{EntityName}Configuration.cs`.
- Apply via `modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())`.
- No data annotations on domain entities.
- Every entity calls `ToTable("{name}", "{schema}")` explicitly.
- Every required relationship declares `.OnDelete(...)` explicitly.

## Naming conventions

| Element | Convention | Example |
|---|---|---|
| Schema | snake_case English | `operations` |
| Table | snake_case plural English | `trips`, `trip_attachments` |
| Column | snake_case English | `reference_number`, `created_at` |
| Index | `ix_{table}_{columns}` | `ix_trips_driver_id_execution_date` |
| Foreign key | `fk_{table}_{referenced}_{column}` | `fk_trip_attachments_trips_trip_id` |
| Check constraint | `chk_{table}_{rule}` | `chk_trips_composition_trailer` |
| Unique | `uq_{table}_{columns}` | `uq_trips_reference_number` |

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

## Decimal precision

| Type | Precision |
|---|---|
| Money amounts | `HasPrecision(12, 2)` |
| Tons | `HasPrecision(10, 3)` |
| Kilometers | `HasPrecision(10, 2)` |
| Hours (if stored) | `HasPrecision(8, 2)` |

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

Every aggregate root has `version: int`:

```csharp
builder.Property<int>("Version")
    .HasColumnName("version")
    .IsConcurrencyToken()
    .HasDefaultValue(0);
```

The aggregate increments it manually in modification methods. Persistence only configures it as token. `DbUpdateConcurrencyException` propagates to global middleware which maps to HTTP 409.

## Check constraints (defense in depth)

For critical invariants, add `HasCheckConstraint` even if the domain enforces them:

```csharp
builder.ToTable("trips", "operations", t =>
{
    t.HasCheckConstraint(
        "chk_trips_composition_trailer",
        "(composition_type = 1 AND trailer_id IS NOT NULL) OR " +
        "(composition_type IN (2, 3) AND trailer_id IS NULL)");
});
```

## Repositories

One interface per aggregate root. In `Application/Abstractions/I{Aggregate}Repository.cs`:

```csharp
public interface ITripRepository
{
    Task<Trip?> LoadAsync(TripId id, CancellationToken cancellationToken);
    Task AddAsync(Trip trip, CancellationToken cancellationToken);
}
```

Implementation in `Persistence/Repositories/{Aggregate}Repository.cs`:

- Always load aggregate intero with child entities via `Include(...)`.
- Filter soft-deleted children: `.Include(t => t.Attachments.Where(a => a.SoftDeletedAt == null))`.
- Use `.AsSplitQuery()` when multiple Include cause cartesian explosion.
- No `Update` method. EF tracks changes; `TransactionFilter` calls `SaveChangesAsync`.
- No query methods on the repository. Read queries go through dedicated query handlers using `DbContext` directly.

## Outbox pattern

Each module has an outbox table:

```sql
CREATE TABLE operations.outbox_messages (
    id              UUID PRIMARY KEY,
    occurred_at     TIMESTAMPTZ NOT NULL,
    event_type      VARCHAR(200) NOT NULL,
    payload         JSONB NOT NULL,
    processed_at    TIMESTAMPTZ,
    error           TEXT,
    attempt_count   INT NOT NULL DEFAULT 0
);

CREATE INDEX ix_outbox_messages_unprocessed
    ON operations.outbox_messages(occurred_at)
    WHERE processed_at IS NULL;
```

Events are written to outbox in the same transaction as aggregate changes via `SaveChangesAsync` override:

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    var aggregates = ChangeTracker.Entries<AggregateRoot>()
        .Select(e => e.Entity)
        .Where(a => a.DomainEvents.Count > 0)
        .ToList();

    var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
    foreach (var domainEvent in events)
        OutboxMessages.Add(OutboxMessage.From(domainEvent, _clock.UtcNow));

    aggregates.ForEach(a => a.ClearDomainEvents());
    return await base.SaveChangesAsync(cancellationToken);
}
```

An `OutboxProcessor` `BackgroundService` reads unprocessed events and dispatches to handlers. At-least-once delivery: handlers must be idempotent.

## Migrations

Naming: `{Verb}{Entity}` in PascalCase. Examples: `AddTripsTable`, `AddIssueAcceptanceReason`, `CreateOutboxIndex`. No auto-generated timestamp-only names.

Generation:

```bash
dotnet ef migrations add AddTripsTable \
    --project src/Modules/Operations/Priamus.Trips.Operations \
    --startup-project src/Priamus.Trips.Host \
    --context OperationsDbContext \
    --output-dir Persistence/Migrations
```

Rules:
- Forward-only: never edit a migration after merging to main.
- For destructive changes (drop column, rename table), produce two migrations across two releases: additive first, cleanup second.
- Apply: dev auto on startup via `MigrateAsync()`; prod via dedicated pre-deploy job. Never `EnsureCreated()`.

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

- `.AsNoTracking()` on every read-only query.
- `.AsSplitQuery()` when multiple Include cause cartesian explosion warning.
- Indexes declared explicitly via `.HasIndex(...)`. Never rely on EF defaults.
- Partial indexes for soft-deleted filters: `.HasFilter("soft_deleted_at IS NULL")`.
- No N+1: verify via `Microsoft.EntityFrameworkCore.Diagnostics` warnings as errors in tests.
