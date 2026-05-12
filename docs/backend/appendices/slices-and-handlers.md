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
    private readonly ITripRepository _tripRepository;
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

            await _tripRepository.AddAsync(trip, cancellationToken);

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
- Use implicit conversions: `return SomeErrors.NotFound;` for failures, `return new MyResponse(...);` for success.
- Map DTO → domain input via private static methods.
- Wrap calls to aggregate factories in `try/catch (DomainException ex)` and convert to `Error.Domain(ex.ErrorCode, ex.Message)`.
- Do NOT catch other exceptions. They propagate to global middleware.
- Use structured logging with placeholders.
- `IClock` for time. `CancellationToken` propagated to every await.
- Never call `SaveChangesAsync` directly. `TransactionFilter` handles transactions.

## Endpoint

```csharp
public static class RegisterTripEndpoint
{
    public static IEndpointRouteBuilder MapRegisterTrip(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/trips", HandleAsync)
            .AddEndpointFilter<TransactionFilter>()
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
  - `.AddEndpointFilter<TransactionFilter>()` on write operations (POST, PUT, PATCH, DELETE)
  - `.RequireAuthorization(...)` with a specific policy
  - `.WithName(...)`, `.WithTags(...)` for OpenAPI
  - `.Produces<>` and `.ProducesProblem(...)` for documented status codes
- Use `error.ToProblemDetails()`. Mapping is automatic via `ErrorType`.
- HTTP status codes match the `ErrorType` mapping:

| `ErrorType` | HTTP | Title |
|---|---|---|
| `Validation` | 400 | Bad Request |
| `Unauthorized` | 401 | Unauthorized |
| `Forbidden` | 403 | Forbidden |
| `NotFound` | 404 | Not Found |
| `Conflict` | 409 | Conflict |
| `Domain` | 422 | Unprocessable Entity |
| `Unexpected` | 500 | Internal Server Error |

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
