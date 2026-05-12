# Appendix: Domain Modeling Rules

Detailed rules for designing aggregates, value objects, sealed types, and domain events in the Priamus Trips project.

## Aggregate roots

- Extend `AggregateRoot` from SharedKernel.
- Strongly-typed ID: `public readonly record struct {Name}Id(Guid Value)` with `static New()` factory.
- Parameterless constructor `private` (for EF only), marked `// EF only`.
- Public creation via static factory methods: `Create(...)`, `Register(...)`. Never `new`.
- State changes only via domain methods (verbs). Never public property setters.
- Properties have `private set` or `init`.
- Collections exposed as `IReadOnlyList<T>`, never `List<T>`.
- Domain events raised via `RaiseEvent(...)` inherited from `AggregateRoot`.

## Value objects

- Implemented as `record` (reference) or `readonly record struct` (small value types).
- Immutable: no setters, no mutation methods.
- All invariants validated in the factory method or constructor.
- On invariant violation: `throw new DomainException(SomeErrors.SomeError)`.
- Structural equality from `record`.

## Sealed types (discriminated unions)

Pattern:

```csharp
public abstract record VehicleComposition
{
    public abstract VehicleId PrimaryVehicleId { get; }

    public sealed record TractorWithTrailer(
        TractorReference Tractor,
        TrailerReference Trailer) : VehicleComposition
    {
        public override VehicleId PrimaryVehicleId => Tractor.Id;
    }

    public sealed record StandaloneTractor(TractorReference Tractor) : VehicleComposition
    {
        public override VehicleId PrimaryVehicleId => Tractor.Id;
    }

    public sealed record StandaloneVehicle(StandaloneVehicleReference Vehicle) : VehicleComposition
    {
        public override VehicleId PrimaryVehicleId => Vehicle.Id;
    }

    public static VehicleComposition Create(
        TractorReference? tractor,
        TrailerReference? trailer,
        StandaloneVehicleReference? standalone)
    {
        // factory enforces all valid combinations and rejects invalid ones
    }
}
```

Rules:
- `public abstract record` base with `public sealed record` variants nested inside.
- Abstract methods/properties for polymorphic behavior.
- Static `Create` factory on the base enforces invariants and returns the correct variant.
- Pattern matching with `switch` expressions to dispatch:
  ```csharp
  var description = composition switch
  {
      VehicleComposition.TractorWithTrailer t => $"{t.Tractor.Plate} + {t.Trailer.Plate}",
      VehicleComposition.StandaloneTractor t => t.Tractor.Plate,
      VehicleComposition.StandaloneVehicle v => v.Vehicle.Plate,
      _ => throw new UnreachableException("All variants must be handled."),
  };
  ```

## Domain events

- Implement `IDomainEvent` marker (from SharedKernel).
- Immutable `record` types.
- Names in past tense: `TripRegistered`, `IssueOpened`, `AttachmentUploaded`.
- Raised from inside aggregate methods.
- Published via outbox by the `SaveChangesAsync` interceptor (not directly by handlers).

## Money and quantities

- Monetary amounts always `Money` from SharedKernel.
- Quantities (`Tons`, `Kilometers`, `Hours`) are value objects, never bare `decimal`.
- Internal precision: amounts 12,2; tons 10,3; kilometers 10,2; hours 8,2.
- No `double`, no `float`.
- No implicit rounding during calculation. Round only at persistence or presentation.

## Time

- Inject `IClock` into factory methods that need time.
- Never static `DateTime.UtcNow`.

## Domain errors — `*Errors` classes

Every aggregate (or significant concept) declares its errors in a static class colocated with the aggregate, NOT in SharedKernel.

```csharp
// File: src/Modules/Operations/Priamus.Trips.Operations/Domain/Trips/TripErrors.cs
namespace Priamus.Trips.Operations.Domain.Trips;

using Priamus.Trips.SharedKernel.Results;

public static class TripErrors
{
    // PRD §5.2, RF-08quater
    public static readonly Error TrailerRequired = Error.Domain(
        "Trip.TrailerRequired",
        "A tractor that requires a trailer cannot be registered without one.");

    public static readonly Error TrailerNotExpected = Error.Domain(
        "Trip.TrailerNotExpected",
        "A trailer was provided for a vehicle composition that does not require one.");

    // PRD §5.4
    public static readonly Error PickupLocationRequired = Error.Domain(
        "Trip.PickupLocationRequired",
        "The contract billing level requires a pickup location.");

    // RF-07
    public static readonly Error EditWindowExpired = Error.Conflict(
        "Trip.EditWindowExpired",
        "The driver's edit window for this trip has expired.");
}
```

Rules:
- File `{Aggregate}Errors.cs` in the aggregate folder.
- Static class with `public static readonly Error` fields.
- Codes: `{Context}.{ErrorName}` in PascalCase.
- Always via factory (`Error.Domain(...)`, `Error.NotFound(...)`, etc.). Never `new Error(...)`.
- Choose the right `ErrorType` based on semantics:
  - `Domain` → business invariant violated (HTTP 422)
  - `Conflict` → state conflict (HTTP 409)
  - `NotFound` → entity does not exist (HTTP 404)
  - `Forbidden` → authenticated but not allowed (HTTP 403)
  - `Validation` → input error beyond attribute validation (HTTP 400)
  - `Unauthorized` → auth missing/invalid (HTTP 401)
- Descriptions in English, technical (developer-facing). End-user messages come from localization keyed by `Code`.
- Group errors with comment headers and PRD references.

## Throwing DomainException

```csharp
public static VehicleComposition Create(TractorReference? tractor, TrailerReference? trailer)
{
    // PRD §5.2, RF-08quater: a tractor that requires a trailer cannot exist without one.
    if (tractor is { RequiresTrailer: true } && trailer is null)
        throw new DomainException(TripErrors.TrailerRequired);

    // ...
}
```

`DomainException` accepts an `Error` and preserves both `ErrorCode` and `Message`. Handlers catch `DomainException` and convert to `Result.Failure(Error.Domain(...))`.

## Stati non rappresentabili

Use the type system to make invalid states unrepresentable when possible. Example: a `TractorRequiringTrailer` without trailer is impossible because the only variant carrying this combination, `TractorWithTrailer`, requires both. The factory enforces "tractor with RequiresTrailer flag must go into TractorWithTrailer".

When the type system cannot prevent it, the constructor of the value object throws `DomainException`.

## PRD references in comments

Every invariant comment cites the PRD section/RF:

```csharp
// PRD §5.4 and RF-04: the billing level "PerCoppia" requires both locations.
if (level is BillingLevel.PerLocationPair && (pickup is null || delivery is null))
    throw new DomainException(TripErrors.LocationsRequired);
```

## Trip aggregate canonical structure

```
Trip (AggregateRoot)
├── Id: TripId
├── ReferenceNumber: string  // format "T-{YYYY}-{NNNN}"
├── DriverId, CustomerId, ContractId
├── VehicleComposition (sealed: 3 variants — see above)
├── Route (value object: PickupLocationId?, DeliveryLocationId?)
├── Parameters (sealed: ByHour | ByWeight | ByDistance | ByTrip)
│   └── Period: TripPeriod (executionDate, startTime, endTime?)
├── RateSnapshot (immutable value object, jsonb in DB)
├── FinalAmount: Money (denormalized for queries)
├── AppliedTimeBand: TimeBand (sealed: Daytime | Nighttime, denormalized)
├── Attachments: IReadOnlyList<Attachment> (child entities)
├── Issues: IReadOnlyList<Issue> (child entities)
├── CreatedAt, CreatedBy, LastModifiedAt, LastModifiedBy
└── Version (concurrency token)
```

Public operations:

```csharp
public sealed class Trip : AggregateRoot
{
    public static Trip Register(
        TripRegistrationContext context,
        TripRegistrationInput input,
        IPricingEngine pricingEngine,
        IClock clock,
        DriverId currentDriver);

    public void ModifyParameters(...);
    public AttachmentId UploadAttachment(...);
    public void RemoveAttachment(AttachmentId id, UserId author);
    public void AcceptIssueWithReason(IssueId id, string reason, UserId author);

    public bool HasOpenIssues();
    public bool IsStillEditableByDriver(IClock clock, TimeSpan window);
}
```

Key invariants and PRD sources:
- `VehicleComposition` rules → PRD §5.2, RF-08quater
- `Route` location requirements depend on contract billing level → PRD §5.4
- `ByHourParameters` requires `Period.EndTime != null` → PRD §5.5
- `RateSnapshot` immutable once persisted → PRD §5.3, §5.9
- `TimeBand` classified by start time only → PRD §5.8
