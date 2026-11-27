# Appendix: Slices and Handlers (Vertical Slice Architecture)

Rules for implementing vertical slices (use cases) in the Priamus Trips backend.

## Anatomy

```
Features/{UseCase}/
├── {UseCase}Request.cs       # HTTP input DTO + validation
├── {UseCase}Response.cs      # HTTP output DTO
├── {UseCase}Handler.cs       # Application logic returning Result<T>
└── {UseCase}Endpoint.cs      # HTTP mapping
```

Naming: verb + noun in English, taken from the PRD use case (`AddTrip`, `StartTrip`, `ListTripCustomers`). The XML doc of the handler cites the PRD sections and requirements it implements (`PRD §5.2`, `RF-08quater`).

## Request DTO (command)

```csharp
[ValidatableType]
public sealed record AddTripRequest(
    [Required(ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Required))]
    Guid? CustomerId,
    [Required(ErrorMessageResourceType = typeof(OperationsMessages), ErrorMessageResourceName = nameof(OperationsMessages.Validation_Required))]
    Guid? VehicleId,
    Guid? TrailerId,
    Guid? PickupLocationId,
    Guid? DeliveryLocationId,
    bool Start);
```

Rules:
- `sealed record`, `[ValidatableType]`, DataAnnotations for structure, `IValidatableObject` for cross-field rules.
- No domain validation here; the domain enforces its own invariants.
- A required field of a value type (enum, `decimal`) is declared nullable (`T?`) so an omitted field binds to `null` instead of the type's zero value, and `Validate()` rejects `null` (`"X is required."`, member `X`). Every write endpoint carrying such a field has an exact-400 test for the omitted field.
- Enums travel by name only, on requests, responses and in OpenAPI; an integer is rejected. One convention for every module, set in the host (`Program.cs`, `JsonStringEnumConverter` with `allowIntegerValues: false`).
- A rule several requests restate (a validity pair, a date window) is a pure function in the domain that their `Validate()` calls (ADR 0012).
- Bare `Guid` ids and `DateOnly` at the HTTP boundary; strongly-typed ids only inside the domain.
- A driver-facing request or response never carries pricing, rates or totals.

## Response DTO

Plain `sealed record`, no behaviour, bare `Guid` ids.

## Handler

```csharp
internal sealed class AddTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistry _registry;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public AddTripHandler(OperationsDbContext db, ITripRegistry registry, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _registry = registry;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<Result<AddTripOutcome>> HandleAsync(
        Guid tripId, AddTripRequest request, CancellationToken cancellationToken)
    {
        var driverId = _currentUser.DriverId
            ?? throw new InvalidOperationException("The session names no driver.");

        var today = _clock.TodayInItaly();
        var validated = await _registry.ValidateTripAsync(
            new TripAnswers(
                request.VehicleId, request.TrailerId, request.CustomerId, today,
                request.PickupLocationId, request.DeliveryLocationId),
            cancellationToken);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var trip = Trip.Add(
            new TripId(tripId), driverId, request.CustomerId!.Value, request.VehicleId!.Value,
            request.TrailerId, request.PickupLocationId, request.DeliveryLocationId,
            _clock.UtcNow, today, _currentUser.UserId);

        _db.Trips.Add(trip);
        await _db.SaveChangesAsync(cancellationToken);
        return new AddTripOutcome(await trip.ToDtoAsync(_registry, cancellationToken), Created: true);
    }
}
```

Rules:
- `internal sealed class` (nothing outside the module resolves a handler, and its constructor takes internal ports), constructor injection, one public `HandleAsync(request, ct)` returning `Result<T>` or `Result`.
- Inject the module `DbContext` directly (ADR 0009). No repositories.
- Implicit conversions: `return SomeErrors.NotFound;`, `return new Response(...);`.
- An aggregate method that can refuse returns a `Result` (the handler returns its `Error`); the handler catches no exception except the ones mapped in "Unit of work" below.
- Map DTO → domain input in private static methods, field by field.
- `IClock` for time, `CancellationToken` on every await, structured logging.
- The handler calls `SaveChangesAsync` explicitly; see "Unit of work" below.
- Handlers over ~80 lines or with business logic inside: move the logic to the domain or an application service.

## Endpoint

```csharp
public static class AddTripEndpoint
{
    public static IEndpointRouteBuilder MapAddTrip(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/operations/driver/trips/{tripId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("AddTrip")
            .WithTags("Operations.Driver")
            .Produces<TripDto>(StatusCodes.Status201Created)
            .Produces<TripDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromRoute] Guid tripId,
        [FromBody] AddTripRequest request,
        [FromServices] AddTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tripId, request, cancellationToken);

        return result.Match(
            onSuccess: outcome => outcome.Created
                ? Results.Created($"/api/operations/driver/trips/{outcome.Trip.Id}", outcome.Trip)
                : Results.Ok(outcome.Trip),
            onFailure: error => error.ToProblemDetails());
    }
}
```

Rules:
- `public static class`, one `Map{UseCase}` extension, private static `HandleAsync`.
- Always `.RequireAuthorization(AuthorizationPolicies.X)` with a nominal policy from `Priamus.Trips.Identity.Contracts`: one constant per policy (`Driver`, `BackOffice`, `Admin`, and the combined ones such as `BackOfficeOrAdmin`), never a string literal and never the parameterless overload. `.AllowAnonymous()` never, except login and refresh in Identity, explicitly.
- `.WithName`, `.WithTags`, `.Produces<>`, `.ProducesProblem` for every `ErrorType` the handler can return, and no other. `ProducesProblem(422)` only on an endpoint whose handler can return an `ErrorType.Domain` error; a searchable list declares `ProducesProblem(400)` for the search cap. A sweep in `CrossSlice/` asserts both against `/openapi/v1.json`.
- Handler and body parameters are annotated: `[FromServices]` on the handler, `[FromBody]` on the request.
- Public request/response types of different slices never share a simple name (OpenAPI keys schemas by simple name); a `CrossSlice/` test enforces it.
- Route prefix `/api/{module}/...`, kebab-case, plural resources.

| `ErrorType` | HTTP |
|---|---|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Domain | 422 |
| Unexpected | 500 |

## List endpoints

- **Response shape**: an unpaged list answers a bare JSON array (`IReadOnlyList<TItem>`, no wrapper record); a paged list answers `PagedResponse<T>`. Tests assert the shape on the raw JSON (`JsonElement.ValueKind == Array`), never through a DTO that would hide it.
- **Search is capped**: `[MaxLength(...)]` on the search field, sized to the column it searches, so a longer text, which can never match, answers 400 instead of reaching the query as an unbounded `LIKE` parameter.
- **Case-insensitivity is explicit**: PostgreSQL compares text case-sensitively, so a case-insensitive uniqueness check or search rests on a normalised value (as `Username`, always stored lowercase), never on the collation. For partial matches inside a value object see `persistence.md`, "Querying inside a value object".
- **Query parameter names are camelCase** on the wire and in OpenAPI (`page`, `pageSize`, `sortColumn`, `sortDirection`, `search`), like JSON bodies. Declare them as `[AsParameters]` types with PascalCase properties (`PagedRequest` lives in SharedKernel, which has no ASP.NET reference); the host's OpenAPI operation transformer camel-cases the `in: query` names in the document. Binding is case-insensitive, so nothing else is needed.

## Registration

Handlers: Scrutor scan in `{Module}ModuleExtensions` (classes ending in `Handler`, scoped) with `publicOnly: false`: handlers are internal, and Scrutor's default skips non-public types silently, so every request would fail at runtime with "No service for type ...Handler". Endpoints: explicit enumeration in `{Module}ModuleEndpoints.Map{Module}Endpoints`.

## Cross-module communication

Module B exposes an interface in `Priamus.Trips.{B}.Contracts` and implements it in its main project; module A injects it and references only the Contracts project. Calls that can fail return `Result<T>`; B's errors flow to A through the result.

## Application services

Code shared between slices of the same module (services that look up facts several handlers need, policies such as "can the driver still edit this trip") lives in `Application/Services/`: stateless, injected into handlers, not slices. It is extracted from working slices, never written ahead of them (ADR 0012).

## Unit of work: the handler is the commit point (ADR 0011)

- Default: one `SaveChangesAsync` at the persistence point; EF Core's implicit transaction gives atomicity.
- Multi-step atomicity: `await using var tx = await _db.Database.BeginTransactionAsync(ct); ... await tx.CommitAsync(ct);` local to the handler.
- `Result.Failure` with nothing to persist: do not call `SaveChangesAsync`. `Result.Failure` with a deliberate side effect (audit row, e.g. `LoginFailed`): call it before returning.
- Infrastructure exceptions are mapped to domain errors by the handler, with a try/catch scoped to the `SaveChangesAsync` that can raise them (`DbUpdateConcurrencyException` → the aggregate's concurrency error; a unique violation → the aggregate's "already taken" error).
- The trip acts (start, end, delete) are the exception to the 409 mapping of `DbUpdateConcurrencyException`: they save through `OperationsDbContext.TrySaveTripAsync`, which on a lost save reads the trip again, once, and answers through the handler's normal paths (a trip started meanwhile is answered as it is, a trip deleted meanwhile is not found), decided by Michele on 2026-11-25. See `persistence.md`, "Concurrency".
