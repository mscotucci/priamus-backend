# Appendix: Errors and Results

`Result<T>` and `Error` are the universal way to handle expected failures without exceptions (ADR 0002). Types live in `Priamus.Trips.SharedKernel.Results`; the HTTP mapping in `Priamus.Trips.SharedKernel.Http` (ADR 0006).

## Types

```csharp
public enum ErrorType { None, Validation, NotFound, Conflict, Forbidden, Unauthorized, Domain, Unexpected }

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None;
    public static Error Validation(string code, string description);
    public static Error NotFound(...); Conflict(...); Forbidden(...); Unauthorized(...); Domain(...); Unexpected(...);
}

public readonly struct Result { IsSuccess; IsFailure; Error; Success(); Failure(Error); Match(...); implicit from Error }
public readonly struct Result<T> { ...; Value; Match; Map; Bind; BindAsync; implicit from T and from Error }
```

## Module-specific errors — `{Aggregate}Errors`

```csharp
// src/Modules/Operations/Priamus.Trips.Operations/Domain/Trips/TripErrors.cs
public static class TripErrors
{
    // ─── Vehicle composition (PRD §5.2, RF-08quater) ──────────────────────
    public static readonly Error TrailerRequired = Error.Domain(
        "Operations.Trip.TrailerRequired",
        "A tractor that requires a trailer cannot be registered without one.");

    // ─── Edit window (RF-07) ──────────────────────────────────────────────
    public static readonly Error EditWindowExpired = Error.Conflict(
        "Operations.Trip.EditWindowExpired",
        "The driver's edit window for this trip has expired.");

    public static readonly Error NotFound = Error.NotFound(
        "Operations.Trip.NotFound",
        "The requested trip was not found.");
}
```

Rules:
- File `{Aggregate}Errors.cs` next to the aggregate; static class with `public static readonly Error` fields built through the factories, never `new Error(...)`.
- Codes `{Module}.{Entity}.{ErrorName}` PascalCase (e.g. `Identity.User.CannotDisableSelf`); descriptions in English; the Italian user message lives in a `.resx` keyed by the code.
- Group by category with comment headers citing the PRD section or requirement.
- Never *module* errors in SharedKernel. Self-contained kernel value objects (`Money`, `Tons`, `Kilometers`, `Hours`) own their errors under the `Kernel.{Type}.{ErrorName}` prefix; anything context-specific belongs to its module.

## Italian messages

`Error.Description` stays English: it is the log text and the fallback. The text the user reads is the ProblemDetails `detail`, taken from a `.resx` keyed by `Error.Code`.

- File: `src/Modules/{Module}/Priamus.Trips.{Module}/Resources/{Module}Messages.resx`, neutral culture holding the Italian text (Italian is the only language, so there is no request-localization configuration). One entry per error code, one per request-validation message (`Validation.Required`, `Validation.MaxLength`, ...; `{0}`, `{1}` are format arguments filled by the validation attribute or by the caller).
- Lookup: `Add{Module}Module` calls `AddLocalization()` and adds one `ProblemDetailsOptions.CustomizeProblemDetails` handler that reads `IStringLocalizer<{Module}Messages>[errorCode]` and replaces `detail`. `Results.Problem` passes through it, so no endpoint changes and `ToProblemDetails()` is untouched.
- Values in a message: an error whose Italian entry carries `{0}`, `{1}`... gets them from a scoped `ErrorMessageArguments` service in `Application/Services/`. The handler calls `Set(...)` immediately before returning the error; `Error.Description` stays the English text without values.
- Unique codes: every module's `CustomizeProblemDetails` handler runs for every ProblemDetails the host answers, so an error code must be unique across all modules' `.resx` files. The module prefix of the code guarantees it.
- Fallback: a code with no entry keeps `Error.Description`.
- Validation attributes: `[Required(ErrorMessageResourceType = typeof({Module}Messages), ErrorMessageResourceName = nameof({Module}Messages.Validation_Required))]`; messages built in `IValidatableObject.Validate` read the same class. The csproj asks MSBuild to generate the public accessor class, so `dotnet build` produces it with no designer file in the repository.
- Tests assert the Italian text of at least one error and one validation message per module.

## Choosing the ErrorType

| ErrorType | When | HTTP |
|---|---|---|
| Validation | structural input problem beyond attributes | 400 |
| Unauthorized | auth missing/invalid | 401 |
| Forbidden | authenticated but not allowed (business level) | 403 |
| NotFound | entity does not exist | 404 |
| Conflict | uniqueness, overlap, concurrency, state conflict | 409 |
| Domain | business invariant violated (most common) | 422 |
| Unexpected | truly unanticipated | 500 |

## DomainException

Value objects and aggregates throw `DomainException(SomeErrors.X)`; handlers catch only `DomainException` and return `ex.Error` — the original error, unchanged. Everything else propagates to the global `ProblemDetails` handler.

Never rebuild the error as `Error.Domain(ex.ErrorCode, ex.Message)`: that discards `Error.Type`, and since the HTTP mapping goes by type alone, every conflict invariant (edit window expired, already taken, concurrent change) would reach the client as 422 instead of 409.

## HTTP mapping

`error.ToProblemDetails()` maps by `Error.Type` only and adds `errorCode` and `errorType` extensions. Never branch on `Error.Code` strings, never map status codes by hand in endpoints. Adding an error in a module never requires touching SharedKernel.

## Forbidden patterns

- `result.Error.Code == "X"` in code or tests (compare `Error` instances from the `*Errors` class)
- `new Error(...)`, `Result.Failure(Error.None)`
- throwing for business errors in handlers
- declaring module errors in SharedKernel (kernel value-object errors under `Kernel.*` are the one exception)
