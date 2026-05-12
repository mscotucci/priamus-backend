# Appendix: Errors and Results

The `Result<T>` and `Error` pattern is the universal way to handle expected failures without exceptions. This appendix details the conventions.

## Types in SharedKernel

```csharp
namespace Priamus.Trips.SharedKernel.Results;

public enum ErrorType
{
    None = 0,
    Validation = 1,     // -> HTTP 400
    NotFound = 2,       // -> HTTP 404
    Conflict = 3,       // -> HTTP 409
    Forbidden = 4,      // -> HTTP 403
    Unauthorized = 5,   // -> HTTP 401
    Domain = 6,         // -> HTTP 422
    Unexpected = 7,     // -> HTTP 500
}

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Validation(string code, string description);
    public static Error NotFound(string code, string description);
    public static Error Conflict(string code, string description);
    public static Error Forbidden(string code, string description);
    public static Error Unauthorized(string code, string description);
    public static Error Domain(string code, string description);
    public static Error Unexpected(string code, string description);
}

public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public Error Error { get; }

    public static Result Success();
    public static Result Failure(Error error);
    public static implicit operator Result(Error error);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public T Value { get; }       // throws if IsFailure
    public Error Error { get; }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure);
    public Result<TOut> Map<TOut>(Func<T, TOut> mapper);
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> binder);
    public Task<Result<TOut>> BindAsync<TOut>(Func<T, Task<Result<TOut>>> binder);

    public static implicit operator Result<T>(T value);
    public static implicit operator Result<T>(Error error);
}
```

`Result` and `Result<T>` are `readonly struct` to avoid heap allocation on the common success path.

## Module-specific errors — `*Errors` classes

Every aggregate, application concern, or feature declares its errors in a static class colocated with the code that owns them. NEVER in SharedKernel.

```csharp
// File: src/Modules/Operations/Priamus.Trips.Operations/Domain/Trips/TripErrors.cs
namespace Priamus.Trips.Operations.Domain.Trips;

using Priamus.Trips.SharedKernel.Results;

public static class TripErrors
{
    // ─── Vehicle composition (PRD §5.2, RF-08quater) ──────────────────────

    public static readonly Error TrailerRequired = Error.Domain(
        "Trip.TrailerRequired",
        "A tractor that requires a trailer cannot be registered without one.");

    // ─── Edit window (RF-07) ──────────────────────────────────────────────

    public static readonly Error EditWindowExpired = Error.Conflict(
        "Trip.EditWindowExpired",
        "The driver's edit window for this trip has expired.");

    // ─── Not found ────────────────────────────────────────────────────────

    public static readonly Error NotFound = Error.NotFound(
        "Trip.NotFound",
        "The requested trip was not found.");
}
```

Rules:
- File `{Aggregate}Errors.cs` next to the aggregate.
- Static class with `public static readonly Error` fields.
- Codes: `{Context}.{ErrorName}` PascalCase.
- Always via factory (`Error.Domain(...)`, etc.), never `new Error(...)`.
- English technical descriptions. End-user messages from localization keyed by `Code`.
- Group by category with comment headers and PRD references.

## Choosing the right ErrorType

| ErrorType | When to use | HTTP |
|---|---|---|
| `Validation` | Input error beyond attribute validation, structural issue | 400 |
| `Unauthorized` | Auth missing or invalid | 401 |
| `Forbidden` | Authenticated but not allowed at business level | 403 |
| `NotFound` | Entity does not exist | 404 |
| `Conflict` | State conflict, uniqueness, concurrency, edit window expired | 409 |
| `Domain` | Business invariant violated (the most common in domain code) | 422 |
| `Unexpected` | Should be rare; truly unanticipated | 500 |

## DomainException from value objects

The domain layer throws `DomainException` for invariant violations. The exception carries the error code and message:

```csharp
// In SharedKernel
public sealed class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(Error error) : base(error.Description)
    {
        ErrorCode = error.Code;
    }
}
```

Usage in domain code:

```csharp
public static VehicleComposition Create(TractorReference? tractor, TrailerReference? trailer)
{
    // PRD §5.2, RF-08quater
    if (tractor is { RequiresTrailer: true } && trailer is null)
        throw new DomainException(TripErrors.TrailerRequired);

    // ...
}
```

## Handlers catch DomainException

Handlers convert `DomainException` to `Result<T>` failure:

```csharp
try
{
    var trip = Trip.Register(/* ... */);
    await _repository.AddAsync(trip, ct);
    return new RegisterTripResponse(trip.Id.Value, trip.ReferenceNumber);
}
catch (DomainException ex)
{
    return Error.Domain(ex.ErrorCode, ex.Message);
}
```

Other exceptions are NOT caught. They propagate to global middleware.

## Implicit conversions

Use them for readable handler code:

```csharp
public async Task<Result<RegisterTripResponse>> HandleAsync(...)
{
    var customer = await _customers.FindAsync(...);
    if (customer is null)
        return CustomerErrors.NotFound;  // Error -> Result<T>

    // ...

    return new RegisterTripResponse(/* ... */);  // T -> Result<T>
}
```

Prefer this over explicit `Result<T>.Failure(...)` / `Result<T>.Success(...)`.

## HTTP mapping

`ResultHttpExtensions` lives in `Priamus.Trips.SharedKernel.Http`, **not** in `Priamus.Trips.SharedKernel` core. SharedKernel core is intentionally framework-agnostic. The HTTP bridge is a separate project so that future non-web consumers (workers, batch jobs) can use the Result/Error primitives without taking a dependency on ASP.NET Core.

It converts errors to ProblemDetails:

```csharp
public static IResult ToProblemDetails(this Error error)
{
    var (status, title) = error.Type switch
    {
        ErrorType.Validation   => (StatusCodes.Status400BadRequest, "Bad Request"),
        ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Unauthorized"),
        ErrorType.Forbidden    => (StatusCodes.Status403Forbidden, "Forbidden"),
        ErrorType.NotFound     => (StatusCodes.Status404NotFound, "Not Found"),
        ErrorType.Conflict     => (StatusCodes.Status409Conflict, "Conflict"),
        ErrorType.Domain       => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
        ErrorType.Unexpected   => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        _ => throw new InvalidOperationException("..."),
    };

    return Results.Problem(
        statusCode: status,
        title: title,
        detail: error.Description,
        extensions: new Dictionary<string, object?>
        {
            ["errorCode"] = error.Code,
            ["errorType"] = error.Type.ToString(),
        });
}
```

Mapping is based on `Error.Type`, NEVER on `Error.Code` strings. This means adding a new error in a module does not require modifying SharedKernel or SharedKernel.Http.

## Endpoint usage

```csharp
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
```

## What goes where — quick reference

| Concern | Layer | Mechanism |
|---|---|---|
| DTO structural validation | DTO + DataAnnotations | `[Required]`, `[Range]`, etc. |
| DTO cross-field validation | DTO + IValidatableObject | `Validate(context)` |
| Domain invariant | Value object factory | `throw new DomainException(SomeErrors.X)` |
| Application orchestration error | Handler | `return SomeErrors.X` |
| Concurrency conflict | EF / middleware | `DbUpdateConcurrencyException` → 409 |
| Unexpected | Middleware | Generic 500, log full exception |

## Forbidden patterns

- `result.Error.Code == "Trip.SomeError"` to switch on error semantics (use `Error.Type` instead, or compare full `Error` instances)
- `new Error(...)` directly (use `Error.Domain(...)` factories)
- Throwing exceptions from handlers for business errors (use `Result<T>`)
- Hardcoded error code strings in tests (use `TripErrors.SomethingHappened`)
- Declaring module-specific errors in SharedKernel
- Returning `Result.Failure(Error.None)` (this is illegal — `Error.None` is the success sentinel)
