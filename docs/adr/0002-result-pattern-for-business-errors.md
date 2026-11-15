# 0002 — Result pattern for business errors instead of exceptions

## Status

Accepted (2026-05)

## Context

Application handlers need a way to communicate failure to the HTTP layer. The two dominant approaches in .NET are:

1. **Exceptions for control flow** — handlers throw on failure; middleware catches and maps to HTTP responses.
2. **Result pattern** — handlers return a `Result<T>` that explicitly carries either success value or error; the HTTP layer maps the result.

Both are valid. The decision affects how every handler is written, how tests are structured, and how the boundary with HTTP works.

## Decision

Use the **Result pattern** for all expected business errors. Exceptions are reserved for:
- Programming errors (`InvalidOperationException`, `ArgumentNullException`)
- Domain invariant violations (`DomainException`, immediately caught and converted to `Result.Failure(error)` by handlers)
- Truly unexpected failures (let propagate to global middleware which produces a generic 500 response)

Specifically:

- `Result` and `Result<T>` are readonly structs in `Priamus.Trips.SharedKernel.Results`.
- `Error` is a record with `Code`, `Description`, and `Type` (an `ErrorType` enum: `Validation`, `NotFound`, `Conflict`, `Forbidden`, `Unauthorized`, `Domain`, `Unexpected`).
- Module-specific errors live in static `{Aggregate}Errors` classes inside each module, never in SharedKernel.
- HTTP status mapping is driven by `Error.Type`, never by `Error.Code` string. The mapping lives in `ResultHttpExtensions.ToProblemDetails(...)`.
- Implicit conversions from `T` and from `Error` to `Result<T>` make handler code idiomatic: `return SomeErrors.NotFound;` and `return new MyResponse(...);` both work.

## Consequences

**Positive:**
- Failure is part of the type system, not a hidden control flow. Forgetting to handle a failure case is caught at compile time.
- Handlers are easier to test: assert `result.IsSuccess`, `result.Error.ShouldBe(SomeErrors.X)`, no exception capture.
- HTTP mapping is centralized in `ResultHttpExtensions`. Adding a new `ErrorType` updates one switch in one place.
- Stack traces for true bugs remain meaningful because we don't pollute them with expected failures.

**Negative:**
- More verbose than `throw new` in places where the happy path is overwhelmingly the norm.
- Two layers of indirection between domain (`DomainException`) and HTTP (`Result<T>`): handlers must catch and convert.
- Developers used to exception-based flow need to adapt.

**Neutral:**
- Async variants are added only when needed (`BindAsync` for chaining); no premature `MatchAsync`/`MapAsync`.

## Alternatives considered

**Exceptions for all business errors** was rejected because it conflates "expected failure" (user typed wrong password) with "programming error" (null reference), making both harder to reason about. Stack traces for true bugs get diluted.

**OneOf / discriminated unions library** was considered but rejected as unnecessary dependency. Our `Result<T>` covers the same use case with less surface area, no external library, and full control over the API.
