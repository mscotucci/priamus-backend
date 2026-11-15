# Appendix: Domain Modeling

Rules for aggregates, value objects and sealed types in the Priamus Trips backend.

## What becomes an aggregate

- An entity with behaviour and invariants that span several fields or child entities: **Trip** (vehicle composition, route, parameters, rate snapshot, attachments, issues), **Contract** with its **Rates**, **User**, **RefreshToken**, **DailySummary**.
- A catalog entry with a description and no rules (a location type, a vehicle make) is a plain entity: no aggregate ceremony, no domain events.

## Aggregate shape

```
Trip : AggregateRoot
├── Id: TripId (Guid)
├── ReferenceNumber: string  // format "T-{YYYY}-{NNNN}"
├── DriverId, CustomerId, ContractId (typed ids; reference to another aggregate: bare id, never navigation)
├── VehicleComposition (sealed: TractorWithTrailer | StandaloneTractor | StandaloneVehicle)
├── Route (value object: PickupLocationId?, DeliveryLocationId?)
├── Parameters (sealed: ByHour | ByWeight | ByDistance | ByTrip)
│   └── Period: TripPeriod (executionDate, startTime, endTime?)
├── RateSnapshot (immutable value object, jsonb in DB)
├── FinalAmount: Money (denormalized for queries)
├── AppliedTimeBand: TimeBand (sealed: Daytime | Nighttime, denormalized)
├── Attachments, Issues: IReadOnlyList<T> (child entities)
├── Audit: CreatedAt/CreatedBy/LastModifiedAt/LastModifiedBy (from ICurrentUser + IClock)
└── Version (concurrency token)
```

- Strongly-typed id: `public readonly record struct {Name}Id(Guid Value)` with a static `New()`.
- Static factory (`Create(...)`, `Register(...)`) and state-changing methods (`ModifyParameters`, `UploadAttachment`, `AcceptIssueWithReason`) enforce invariants and throw `DomainException({Aggregate}Errors.X)`. Never `new` from outside, never public setters; collections exposed as `IReadOnlyList<T>`.
- The parameterless constructor is `private`, marked `// EF only`.
- Cross-aggregate rules (the customer exists, the contract is valid at the execution date, the vehicle is active) are checked by the handler or an application service, which passes the needed facts to the aggregate (e.g. `TripRegistrationContext`), never by the aggregate querying the database.
- Every rule cites its PRD source in the XML doc or in the comment above the check: `// PRD §5.4 and RF-04: the billing level "PerCoppia" requires both locations.`

Key invariants of `Trip` and their PRD sources:
- `VehicleComposition` rules → PRD §5.2, RF-08quater
- `Route` location requirements depend on the contract billing level → PRD §5.4
- `ByHourParameters` requires `Period.EndTime != null` → PRD §5.5
- `RateSnapshot` immutable once persisted → PRD §5.3, §5.9
- `TimeBand` classified by start time only → PRD §5.8

## Value objects

- `readonly record struct` for ids and small measures; `sealed record` for composite values. `Money`, `Tons`, `Kilometers`, `Hours` come from SharedKernel; module-specific values (`Username`, `PasswordHash`, `Route`, `TripPeriod`, `RateSnapshot`) stay in the module.
- A value object where the rule is reused or composed; a primitive validated in the entity where the rule is local and used once.
- Constructors and factories validate and throw `DomainException`. No public setters, no mutation methods.
- A value object holding a collection the domain does not bound (trips of a day, rows of a report — not a fixed set like weekdays) must state what it lets callers *answer*, not only what it holds. List the members its consumers actually call: when they are only predicates and aggregates (`Any`, `Count`, a sum), give it a second factory that builds it from that summary, or every query behind it is forced to materialise the whole collection to compute a number — the shape of the type, not the query, is what decides the cost.

## Money and quantities

- Monetary amounts are always `Money`; quantities are `Tons`, `Kilometers`, `Hours`. Never bare `decimal`, never `double` or `float`.
- Precision: amounts 12,2; tons 10,3; kilometers 10,2; hours 8,2.
- No implicit rounding during calculation: round only at persistence or presentation. The pricing engine documents its rounding (`pricing-engine.md`).

## Sealed types for states and variants

```csharp
public abstract record VehicleComposition
{
    public abstract VehicleId PrimaryVehicleId { get; }

    public sealed record TractorWithTrailer(TractorReference Tractor, TrailerReference Trailer) : VehicleComposition
    {
        public override VehicleId PrimaryVehicleId => Tractor.Id;
    }

    public sealed record StandaloneTractor(TractorReference Tractor) : VehicleComposition { /* ... */ }

    public sealed record StandaloneVehicle(StandaloneVehicleReference Vehicle) : VehicleComposition { /* ... */ }

    public static VehicleComposition Create(
        TractorReference? tractor, TrailerReference? trailer, StandaloneVehicleReference? standalone)
    {
        // the factory enforces every valid combination and rejects the others
    }
}
```

- `public abstract record` base with `public sealed record` variants nested inside; a static `Create` on the base enforces the invariants and returns the right variant.
- Make invalid states unrepresentable where the type system allows it: a tractor that requires a trailer exists only as `TractorWithTrailer`. Where it cannot, the factory throws `DomainException`.
- Pattern match with `switch` expressions and `throw new UnreachableException(...)` for exhaustiveness; never `if/else` on type.

## Domain events

- Immutable `record` implementing `IDomainEvent`, named in the past tense (`TripRegistered`, `IssueOpened`), raised inside aggregate methods with `RaiseEvent(...)`.
- Persisted by the `SaveChangesAsync` interceptor of the module (audit log by default, outbox when another module must react; see `persistence.md`), never published by a handler.

## Time

Calendar concepts (execution date, validity ranges) are `DateOnly`; instants (audit, events, trip start and end) are UTC `DateTime` from `IClock`, injected into the factory methods that need it. Never `DateTime.UtcNow` or `DateTime.Today`.

## Do not

- Put EF Core concerns (navigation collections for lazy loading, public parameterless constructors) in the domain.
- Reference another module's types; use bare `Guid` ids and `*.Contracts` DTOs.
- Let pricing data reach a type that a driver-facing response is built from.
