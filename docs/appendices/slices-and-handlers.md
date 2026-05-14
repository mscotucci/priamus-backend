# Appendix: Slices and Handlers (Vertical Slice Architecture)

Rules for implementing vertical slices (use cases) in the Priamus Trips project.

## Anatomy

Every slice lives under `Features/{UseCase}/` and consists of:

```
Features/{UseCase}/
├── {UseCase}Request.cs       # HTTP input DTO + validation
├── {UseCase}Response.cs      # HTTP output DTO
├── {UseCase}Handler.cs       # Application logic returning Result<T>
└── {UseCase}Endpoint.cs      # HTTP mapping
```

Naming: verb + noun in English. `RegisterTrip`, `UploadAttachment`, `ListDriverTrips`.

## Request DTO (command)

```csharp
[ValidatableType]
public sealed record RegisterTripRequest(
    [Required] Guid CustomerId,
    [Required] Guid PrimaryVehicleId,
    Guid? TrailerId,
    Guid? PickupLocationId,
    Guid? DeliveryLocationId,
    DateOnly ExecutionDate,
    DateTime StartTime,
    DateTime? EndTime,
    [Range(0.001, 10000)] decimal? Tons,
    [Range(0.1, 10000)] decimal? Kilometers,
    [MaxLength(10)] IReadOnlyList<AttachmentUploadDto> Attachments
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EndTime.HasValue && EndTime <= StartTime)
            yield return new ValidationResult(
                "End time must be after start time.",
                new[] { nameof(EndTime) });

        if (Tons.HasValue && Kilometers.HasValue)
            yield return new ValidationResult(
                "Cannot specify both tons and kilometers.",
                new[] { nameof(Tons), nameof(Kilometers) });
    }
}
```

Rules:

- `sealed record` always.
- `[ValidatableType]` for validation discovery.
- DataAnnotations for structural validation.
- `IValidatableObject` for cross-field rules.
- No domain validation here. Domain enforces its own invariants.
- Bare `Guid` at the HTTP boundary. Strongly-typed IDs only after entering domain.

## Response DTO

```csharp
public sealed record RegisterTripResponse(
    Guid TripId,
    string ReferenceNumber);
```

Plain `sealed record`, no behavior. Use bare `Guid` at HTTP boundary.

## Handler

```csharp
public sealed class RegisterTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistrationContextFactory _contextFactory;
    private readonly IPricingEngine _pricingEngine;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<RegisterTripHandler> _logger;

    public RegisterTripHandler(/* DI */) { /* ... */ }

    public async Task<Result<RegisterTripResponse>> HandleAsync(
        RegisterTripRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.DriverId is not DriverId currentDriver)
            return AuthErrors.NotADriver;  // implicit Error -> Result<T>

        var contextResult = await _contextFactory.BuildAsync(
            new CustomerId(request.CustomerId),
            new VehicleId(request.PrimaryVehicleId),
            request.TrailerId.HasValue ? new VehicleId(request.TrailerId.Value) : null,
            request.ExecutionDate,
            cancellationToken);

        if (contextResult.IsFailure)
            return contextResult.Error;

        try
        {
            var trip = Trip.Register(
                contextResult.Value,
                MapToInput(request),
                _pricingEngine,
                _clock,
                currentDriver);

            _db.Trips.Add(trip);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Trip {TripId} registered for driver {DriverId}",
                trip.Id.Value, currentDriver.Value);

            return new RegisterTripResponse(trip.Id.Value, trip.ReferenceNumber);
        }
        catch (DomainException ex)
        {
            return Error.Domain(ex.ErrorCode, ex.Message);
        }
    }

    private static TripRegistrationInput MapToInput(RegisterTripRequest request) => new(
        ExecutionDate: request.ExecutionDate,
        // ... explicit field-by-field mapping, no AutoMapper ...
    );
}
```

Rules:

- `sealed class`. Constructor injection.
- One public method `HandleAsync(request, ct)` returning `Result<TResponse>` or `Result`.
- Inject the module's `DbContext` directly (per [ADR 0009](../decisions/0009-no-repository-pattern.md)). No repository abstractions.
- Use implicit conversions: `return SomeErrors.NotFound;` for failures, `return new MyResponse(...);` for success.
- Map DTO → domain input via private static methods.
- Wrap calls to aggregate factories in `try/catch (DomainException ex)` and convert to `Error.Domain(ex.ErrorCode, ex.Message)`.
- Do NOT catch other exceptions. They propagate to global middleware.
- Use structured logging with placeholders.
- `IClock` for time. `CancellationToken` propagated to every await.
- Call `SaveChangesAsync` explicitly at the persistence point of the handler. See [Unit of work: the handler is the commit point](#unit-of-work-the-handler-is-the-commit-point) below for the conventions on success path, failure path, deliberate side effects, and exception-to-error mapping.

## Endpoint

```csharp
public static class RegisterTripEndpoint
{
    public static IEndpointRouteBuilder MapRegisterTrip(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/trips", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("RegisterTrip")
            .WithTags("Trips")
            .Produces<RegisterTripResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        RegisterTripRequest request,
        RegisterTripHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            onSuccess: response => Results.Created($"/api/trips/{response.TripId}", response),
            onFailure: error => error.ToProblemDetails());
    }
}
```

Rules:

- `public static class`, single mapping method `Map{UseCase}` as extension on `IEndpointRouteBuilder`.
- Returns `IEndpointRouteBuilder` for chaining.
- Private static `HandleAsync` for the delegate.
- Handler injected as parameter; ASP.NET Core DI resolves it.
- Always declare:
    - `.RequireAuthorization(...)` with a specific policy (or `.AllowAnonymous()` explicitly when intended)
    - `.WithName(...)`, `.WithTags(...)` for OpenAPI
    - `.Produces<>` and `.ProducesProblem(...)` for documented status codes
- Use `error.ToProblemDetails()`. Mapping is automatic via `ErrorType`.
- HTTP status codes match the `ErrorType` mapping:

| `ErrorType`    | HTTP | Title                 |
| -------------- | ---- | --------------------- |
| `Validation`   | 400  | Bad Request           |
| `Unauthorized` | 401  | Unauthorized          |
| `Forbidden`    | 403  | Forbidden             |
| `NotFound`     | 404  | Not Found             |
| `Conflict`     | 409  | Conflict              |
| `Domain`       | 422  | Unprocessable Entity  |
| `Unexpected`   | 500  | Internal Server Error |

## Authorization policies

Defined in Host:

```csharp
public static class AuthorizationPolicies
{
    public const string Driver = "driver";
    public const string BackOffice = "backoffice";
    public const string Admin = "admin";
}
```

Every endpoint declares exactly one policy. Multi-role access via combined policy.

## Handler registration via Scrutor

In `{Module}ModuleExtensions.cs`:

```csharp
public static IServiceCollection AddOperationsModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // DbContext, options, etc.

    services.Scan(scan => scan
        .FromAssemblyOf<OperationsModuleMarker>()
        .AddClasses(c => c.Where(t => t.Name.EndsWith("Handler")))
        .AsSelf()
        .WithScopedLifetime());

    return services;
}
```

`OperationsModuleMarker` is an empty marker class for assembly identification.

## Endpoint registration

In `{Module}ModuleEndpoints.cs`, enumerate slices explicitly:

```csharp
public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
{
    app.MapRegisterTrip();
    app.MapModifyTrip();
    app.MapUploadAttachment();
    app.MapAcceptIssue();
    app.MapCompileDailySummary();
    app.MapListDriverTrips();
    return app;
}
```

Explicit enumeration makes the public surface of the module visible at a glance.

## Cross-module communication

When module A needs data from module B:

1. Module B exposes an interface in `Priamus.Trips.{B}.Contracts`.
2. Module B implements it in its main project.
3. Module A injects the interface via DI.
4. Module A's `.csproj` references `Priamus.Trips.{B}.Contracts` only.

When the call can fail, the interface returns `Result<T>`:

```csharp
// In Registry.Contracts
public interface ITripRegistrationContextFactory
{
    Task<Result<TripRegistrationContext>> BuildAsync(
        CustomerId customerId,
        VehicleId primaryVehicleId,
        VehicleId? trailerId,
        DateOnly executionDate,
        CancellationToken cancellationToken);
}
```

Errors from module B live in B's `*Errors` classes and flow to module A via Result.

## Application services (factories, policies)

Code shared between slices in the same module lives in `Application/Services/`:

- `ContextFactories` (build `TripRegistrationContext` etc. by querying other modules)
- `Policies` (e.g. `EditWindowPolicy.CanDriverStillEdit(trip, clock)`)

These are not slices. They are stateless services injected into handlers.

## Slice file boundary

A slice contains thin orchestration. If a handler exceeds ~80 lines or contains complex business logic, that logic belongs in the domain or in an application service.

The handler's job: validate context, call the aggregate, persist, return result. Nothing more.

## Unit of work: the handler is the commit point

Per [ADR 0011](../decisions/0011-handler-is-commit-point.md), the handler is the single commit point for the operation it executes. There is no transactional middleware, no Unit-of-Work abstraction, no filter that calls `SaveChangesAsync` on the handler's behalf.

### The default case: single `SaveChangesAsync`

The typical handler mutates the change tracker (adds entities, invokes aggregate methods) and persists once before returning:

```csharp
public async Task<Result<MyResponse>> HandleAsync(MyRequest request, CancellationToken ct)
{
    // ... load, validate, mutate aggregates ...

    _db.MyAggregates.Add(newAggregate);
    await _db.SaveChangesAsync(ct);

    return new MyResponse(...);
}
```

EF Core's implicit transaction around the single `SaveChangesAsync` call provides atomicity. No explicit `BeginTransactionAsync` is needed.

### Multi-step atomicity: explicit transaction local to the handler

When the handler genuinely requires atomicity across multiple `SaveChangesAsync` calls, or atomicity between EF operations and raw SQL, it opens a transaction locally:

```csharp
await using var tx = await _db.Database.BeginTransactionAsync(ct);
// ... multiple operations, each with its own SaveChangesAsync if needed ...
await tx.CommitAsync(ct);
```

This pattern stays local to the handler that needs it. It is never centralized in a filter or middleware.

### Conventions for the `Result.Failure` path

- A handler returning `Result.Failure` with no intent to persist anything **does not call** `SaveChangesAsync`. The change tracker's pending changes are discarded when the request scope ends.
- A handler returning `Result.Failure` with a deliberate side effect (typically an audit row, e.g. recording `LoginFailed` before returning `User.InvalidCredentials`) **calls** `SaveChangesAsync` explicitly before the `Failure` return. The side effect persists.
- A handler returning `Result.Success` **calls** `SaveChangesAsync` as the final persistence action before constructing the response.

### Exception-to-error mapping

Translating a specific infrastructure exception into a domain `Result.Failure` is the handler's responsibility. The handler scopes the try/catch narrowly — only around the persistence call that can raise the exception — and returns `Result.Failure(...)` from the catch block.

Canonical example: a refresh handler that catches `DbUpdateConcurrencyException` around its `SaveChangesAsync` call and returns a module-specific concurrency error.

```csharp
try
{
    await _db.SaveChangesAsync(ct);
}
catch (DbUpdateConcurrencyException)
{
    return RefreshTokenErrors.ConcurrentRefresh;
}

return new RefreshResponse(...);
```

The handler does not throw; it returns. Other exceptions propagate to global middleware.
