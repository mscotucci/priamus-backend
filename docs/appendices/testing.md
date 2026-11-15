# Appendix: Testing

Conventions for unit and integration tests in the Priamus Trips backend.

## Stack

- **xUnit v3** on **Microsoft.Testing.Platform** (opt-in in `global.json`; run with `dotnet test --project <csproj>`)
- **Shouldly** assertions, **NSubstitute** for application dependencies only
- **Testcontainers.PostgreSql** for integration tests (Docker required)
- **Microsoft.AspNetCore.Mvc.Testing** for end-to-end HTTP tests

## Layout

```
tests/
├── Priamus.Trips.{Module}.UnitTests/
│   ├── Domain/{Aggregate}/
│   ├── Features/
│   └── TestData/TestDataBuilders.cs
├── Priamus.Trips.IntegrationTesting/   # shared rig (class library, not a test project)
│   └── {PostgresFixture, ApiFactory, ProblemDetailsAssertions, FixedClockStub, FakeCurrentUser}.cs
└── Priamus.Trips.{Module}.IntegrationTests/
    ├── Infrastructure/{Module}IntegrationTestBase.cs
    ├── Persistence/
    ├── Features/{UseCase}/        # handler tests and endpoint tests of the slice
    └── CrossSlice/                # sweeps over every endpoint or public type
```

## Naming and structure

- Test class `{TypeUnderTest}Tests`; method `Method_Scenario_ExpectedResult`.
- `// Arrange`, `// Act`, `// Assert` comments mandatory, blank lines between blocks.
- Test data through builders in `TestData/`; defaults implicit, non-defaults visible in the test.
- Time through `FixedClockStub`; never `DateTime.Now`.

## Traceability to the spec

Every acceptance criterion of the slice's `SPEC.md` (`AC-n`) has at least one test that names it. Put the id in the test name suffix or in a `[Trait("AC", "AC-3")]`; `strip-plan-refs` removes the scaffolding once the spec is graduated. Tests of the pricing engine cite the PRD (`PRD §12`) and use its numeric examples: every example of PRD §12 has a test with the same numbers.

## Unit tests — domain

Coverage target ≥ 90% on the domain layer (100% on pricing types): every factory (valid construction + every invariant), every state-changing method, exhaustiveness of sealed types, value-object boundaries. Assert on the `DomainException` error against the `*Errors` field, never a string literal.

## Unit tests — handlers

Only for handlers whose behaviour does not depend on persistence (rare under ADR 0009). Mock application dependencies (`IClock`, `ICurrentUser`, contracts of other modules), never domain types and never `DbContext`.

## Integration tests — persistence and handlers

Real PostgreSQL. `PostgresFixture` starts one container, applies the migrations once to a template database, and `{Module}IntegrationTestBase` gives every test its own database cloned from the template, with a DI container that registers the module persistence as in production. Handler tests exercise the full path including `SaveChangesAsync` and assert on the persisted state with a fresh scope.

```csharp
public sealed class DisableUserHandlerTests : IdentityIntegrationTestBase
{
    public DisableUserHandlerTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task HandleAsync_WhenTargetIsTheCaller_ReturnsCannotDisableSelf()
    {
        // Arrange
        await using var arrange = CreateAsyncScope();
        // seed the user ...

        // Act
        await using var act = CreateAsyncScope();
        var handler = act.ServiceProvider.GetRequiredService<DisableUserHandler>();
        var result = await handler.HandleAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        result.Error.ShouldBe(UserErrors.CannotDisableSelf);
    }
}
```

N+1 and cartesian explosion: the test setup escalates `RelationalEventId.MultipleCollectionIncludeWarning` to an error; fix with `.AsSplitQuery()`. Where a query must aggregate in the database, assert the emitted SQL instead of assuming the translation.

## End-to-end tests

`ApiFactory` builds `WebApplicationFactory<Program>` with the connection string overridden to the test database and deterministic JWT settings. One host serves a whole test class, because building the host dominates the cost of an HTTP test; each test still gets its own database. A test that needs a differently configured host (a fixed clock, a stubbed lookup) passes its overrides to `ApiFactory.Create`.

Identity tests authenticate with the real flow (`POST /api/identity/auth/login`), because the token is what they test. Tests of the other modules authenticate through a test scheme that issues the claims of a caller, without seeding a user and logging in (`docs/backlog.md`).

One happy path and one authorization failure per endpoint at minimum. Problem responses are asserted with `ShouldBeProblemAsync(status, {Aggregate}Errors.X)`. Driver-facing endpoints have a test asserting on the raw JSON that no pricing field is present.

## Cross-slice sweeps

`CrossSlice/` holds the tests that hold for every endpoint of the module, read from `/openapi/v1.json`: every endpoint declares a policy (the anonymous ones are an explicit list), every declared `ProducesProblem` matches what the handler can return, public request/response type names are unique across slices.

## Pagination cases (mandatory for every paginated endpoint)

Single page; multi-page first/middle/last; empty; invalid page values coerced; unknown sort column falls back; ascending and descending; sort column case-insensitive.

## Forbidden

- `Microsoft.EntityFrameworkCore.InMemory`, `EnsureCreated()`, the development database
- `Substitute.For<DomainType>()`, mocking `DbContext`
- hard-coded error code strings
- `.Result`, `.Wait()`, `GetAwaiter().GetResult()`
- tests without AAA comments
