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

Naming: verb + noun in English, taken from the PRD use case (`RegisterTrip`, `UploadAttachment`, `ListDriverTrips`). The XML doc of the handler cites the PRD sections and requirements it implements (`PRD §5.2`, `RF-08quater`).

## Request DTO (command)

```csharp
[ValidatableType]
public sealed record RegisterTripRequest(
    [Required] Guid CustomerId,
    [Required] Guid PrimaryVehicleId,
    Guid? TrailerId,
    DateOnly ExecutionDate,
    DateTime StartTime,
    DateTime? EndTime,
    [Range(0.001, 10000)] decimal? Tons,
    [Range(0.1, 10000)] decimal? Kilometers
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EndTime.HasValue && EndTime <= StartTime)
        {
            yield return new ValidationResult("End time must be after start time.", [nameof(EndTime)]);
        }
    }
}
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
internal sealed class RegisterTripHandler
{
    private readonly OperationsDbContext _db;
    private readonly ITripRegistrationContextFactory _contextFactory;
    private readonly IPricingEngine _pricingEngine;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<RegisterTripHandler> _logger;

    public RegisterTripHandler(/* DI */) { /* ... */ }

    /// <summary>PRD §5.2, §5.4, RF-08quater.</summary>
    public async Task<Result<RegisterTripResponse>> HandleAsync(
        RegisterTripRequest request, CancellationToken ct)
    {
        var context = await _contextFactory.BuildAsync(/* ids from the request */, ct);
        if (context.IsFailure)
        {
            return context.Error;
        }

        try
        {
            var trip = Trip.Register(context.Value, MapToInput(request), _pricingEngine, _clock, _currentUser.UserId);
            _db.Trips.Add(trip);
            await _db.SaveChangesAsync(ct);
            return new RegisterTripResponse(trip.Id.Value, trip.ReferenceNumber);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }
    }
}
```

Rules:
- `internal sealed class` (nothing outside the module resolves a handler, and its constructor takes internal ports), constructor injection, one public `HandleAsync(request, ct)` returning `Result<T>` or `Result`.
- Inject the module `DbContext` directly (ADR 0009). No repositories.
- Implicit conversions: `return SomeErrors.NotFound;`, `return new Response(...);`.
- Wrap aggregate calls in `try/catch (DomainException)` and return `ex.Error`; do not catch anything else.
- Map DTO → domain input in private static methods, field by field.
- `IClock` for time, `CancellationToken` on every await, structured logging.
- The handler calls `SaveChangesAsync` explicitly; see "Unit of work" below.
- Handlers over ~80 lines or with business logic inside: move the logic to the domain or an application service.

## Endpoint

```csharp
public static class RegisterTripEndpoint
{
    public static IEndpointRouteBuilder MapRegisterTrip(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/operations/trips", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.Driver)
            .WithName("RegisterTrip")
            .WithTags("Operations.Trips")
            .Produces<RegisterTripResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] RegisterTripRequest request,
        [FromServices] RegisterTripHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request, ct);
        return result.Match(
            onSuccess: r => Results.Created($"/api/operations/trips/{r.TripId}", r),
            onFailure: e => e.ToProblemDetails());
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

Code shared between slices of the same module (context factories such as the trip registration context, policies such as "can the driver still edit this trip") lives in `Application/Services/`: stateless, injected into handlers, not slices. It is extracted from working slices, never written ahead of them (ADR 0012).

## Unit of work: the handler is the commit point (ADR 0011)

- Default: one `SaveChangesAsync` at the persistence point; EF Core's implicit transaction gives atomicity.
- Multi-step atomicity: `await using var tx = await _db.Database.BeginTransactionAsync(ct); ... await tx.CommitAsync(ct);` local to the handler.
- `Result.Failure` with nothing to persist: do not call `SaveChangesAsync`. `Result.Failure` with a deliberate side effect (audit row, e.g. `LoginFailed`): call it before returning.
- Infrastructure exceptions are mapped to domain errors by the handler, with a try/catch scoped to the `SaveChangesAsync` that can raise them (`DbUpdateConcurrencyException` → the aggregate's concurrency error; a unique violation → the aggregate's "already taken" error).
