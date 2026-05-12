# Appendix: Testing

Conventions for unit and integration tests in the Priamus Trips project.

## Stack

- **xUnit 2.x** — test framework
- **Shouldly 4.x+** — fluent assertions
- **NSubstitute 5.x+** — mock application dependencies
- **Testcontainers.PostgreSql 4.x+** — real DB for integration tests
- **Microsoft.AspNetCore.Mvc.Testing** — end-to-end HTTP tests

## Project layout

Two test projects per module:

```
tests/
├── Priamus.Trips.{Module}.UnitTests/
│   ├── Domain/{Aggregate}/
│   ├── Features/
│   └── TestData/TestDataBuilders.cs
└── Priamus.Trips.{Module}.IntegrationTests/
    ├── Fixtures/{Module}DatabaseFixture.cs
    ├── Persistence/
    └── Features/
```

## Naming convention

Test method: `Method_Scenario_ExpectedResult`. Test class: `{TypeUnderTest}Tests`.

Examples:
- `Register_WithTractorRequiringTrailerButNoTrailerProvided_ThrowsDomainException`
- `Calculate_ByHourWithNighttimeStart_AppliesNightRateAndCallFee`
- `LoadAsync_WhenTripExistsWithAttachments_ReturnsFullAggregate`
- `HandleAsync_WhenCustomerNotFound_ReturnsCustomerNotFoundError`

## AAA pattern with explicit markers

```csharp
[Fact]
public void Register_WithTractorRequiringTrailerButNoTrailerProvided_ThrowsDomainException()
{
    // Arrange
    var tractor = TestData.Tractor.RequiringTrailer();
    var context = TestData.TripRegistrationContext.With(primaryVehicle: tractor, trailer: null);
    var input = TestData.TripRegistrationInput.Default();
    var pricingEngine = Substitute.For<IPricingEngine>();
    var clock = new FakeClock(DateTime.Parse("2026-05-11T10:00:00Z"));

    // Act
    var act = () => Trip.Register(context, input, pricingEngine, clock, TestData.DriverIds.Alice);

    // Assert
    var ex = act.ShouldThrow<DomainException>();
    ex.ErrorCode.ShouldBe(TripErrors.TrailerRequired.Code);
}
```

Comments `// Arrange`, `// Act`, `// Assert` are mandatory. Blank lines separate the three blocks.

## Test data builders

Centralize fixture construction in `TestData/TestDataBuilders.cs`:

```csharp
public static class TestData
{
    public static class DriverIds
    {
        public static readonly DriverId Alice = new(Guid.Parse("11111111-..."));
        public static readonly DriverId Bob = new(Guid.Parse("22222222-..."));
    }

    public static class Tractor
    {
        public static TractorReference RequiringTrailer() => new(
            Id: new VehicleId(Guid.NewGuid()),
            RequiresTrailer: true,
            IsActive: true);

        public static TractorReference Standalone() => /* ... */;
    }

    public static class TripRegistrationContext
    {
        public static Domain.Trips.TripRegistrationContext With(/* ... */) { /* ... */ }
    }
}
```

Goals: tests stay focused on the scenario; default values implicit; non-default visible in the test.

## Unit tests — domain

Coverage target: ≥ 90% line coverage on Domain layer (`AggregateRoot`, value objects, sealed types, domain services).

What to test:
- Every public factory: valid construction + each invariant violation.
- Every domain operation (state-changing method).
- Pattern matching exhaustiveness on sealed types.
- Value object edge cases (boundaries, equality, immutability).

Example structure for a sealed type:

```csharp
public class VehicleCompositionTests
{
    [Fact]
    public void Create_WithTractorRequiringTrailerAndTrailerProvided_ReturnsTractorWithTrailer()
    {
        // Arrange
        var tractor = TestData.Tractor.RequiringTrailer();
        var trailer = TestData.Trailer.Active();

        // Act
        var result = VehicleComposition.Create(tractor, trailer);

        // Assert
        result.ShouldBeOfType<VehicleComposition.TractorWithTrailer>();
    }

    [Fact]
    public void Create_WithTractorRequiringTrailerAndNoTrailer_ThrowsTrailerRequired()
    {
        // Arrange
        var tractor = TestData.Tractor.RequiringTrailer();

        // Act
        var act = () => VehicleComposition.Create(tractor, trailer: null);

        // Assert
        var ex = act.ShouldThrow<DomainException>();
        ex.ErrorCode.ShouldBe(TripErrors.TrailerRequired.Code);
    }
}
```

## Unit tests — handlers

Handler dependencies are mocked via NSubstitute. Domain types are NOT mocked — they are real instances built from `TestData`.

```csharp
[Fact]
public async Task HandleAsync_WhenContextFactoryFails_ReturnsTheError()
{
    // Arrange
    var repository = Substitute.For<ITripRepository>();
    var contextFactory = Substitute.For<ITripRegistrationContextFactory>();
    var pricingEngine = Substitute.For<IPricingEngine>();
    var currentUser = new FakeCurrentUser(driverId: TestData.DriverIds.Alice);
    var clock = new FakeClock(DateTime.Parse("2026-05-11T10:00:00Z"));
    var logger = NullLogger<RegisterTripHandler>.Instance;

    var expectedError = CustomerErrors.NotFound;
    contextFactory
        .BuildAsync(Arg.Any<CustomerId>(), Arg.Any<VehicleId>(), Arg.Any<VehicleId?>(),
                    Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
        .Returns(Result<TripRegistrationContext>.Failure(expectedError));

    var handler = new RegisterTripHandler(repository, contextFactory, pricingEngine,
                                          currentUser, clock, logger);
    var request = TestData.RegisterTripRequest.Default();

    // Act
    var result = await handler.HandleAsync(request, CancellationToken.None);

    // Assert
    result.IsFailure.ShouldBeTrue();
    result.Error.ShouldBe(expectedError);
    await repository.DidNotReceive().AddAsync(Arg.Any<Trip>(), Arg.Any<CancellationToken>());
}
```

What NOT to test:
- Aggregate's internal logic (covered by domain tests)
- Repository persistence (covered by integration tests)
- HTTP behavior (covered by end-to-end tests)

## Asserting on Result and Error

Reference the `*Errors` class, never hardcode the code string:

```csharp
// CORRECT
result.Error.ShouldBe(TripErrors.TrailerRequired);

// WRONG
result.Error.Code.ShouldBe("Trip.TrailerRequired");
```

Optional helpers in `TestUtilities/ResultAssertions.cs`:

```csharp
public static class ResultAssertions
{
    public static void ShouldBeSuccessful<T>(this Result<T> result, T expectedValue) { /* ... */ }
    public static void ShouldBeFailure<T>(this Result<T> result, Error expectedError) { /* ... */ }
}

// Usage
result.ShouldBeFailure(TripErrors.TrailerRequired);
```

## Integration tests — repositories

Real PostgreSQL via Testcontainers. Migrations applied. Roundtrip behavior verified.

```csharp
public sealed class OperationsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("priamus_trips_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public OperationsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "operations"))
            .Options;
        return new OperationsDbContext(options);
    }
}

[CollectionDefinition("OperationsDatabase")]
public class OperationsDatabaseCollection : ICollectionFixture<OperationsDatabaseFixture> { }
```

Test class:

```csharp
[Collection("OperationsDatabase")]
public class TripRepositoryTests
{
    private readonly OperationsDatabaseFixture _fixture;

    public TripRepositoryTests(OperationsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadAsync_WithExistingTripAndAttachments_LoadsFullAggregate()
    {
        // Arrange
        var trip = TestData.Trip.WithAttachments(count: 2);

        await using (var arrangeContext = _fixture.CreateContext())
        {
            await arrangeContext.Trips.AddAsync(trip);
            await arrangeContext.SaveChangesAsync();
        }

        // Act
        await using var actContext = _fixture.CreateContext();
        var repository = new TripRepository(actContext);
        var loaded = await repository.LoadAsync(trip.Id, CancellationToken.None);

        // Assert
        loaded.ShouldNotBeNull();
        loaded.Attachments.Count.ShouldBe(2);
    }
}
```

Cleanup: transaction rollback per test (fastest); `Respawn` for table truncate; fresh container per class (slowest, last resort).

## Integration tests — end-to-end slices

`WebApplicationFactory<Program>` with overridden DB.

```csharp
[Collection("OperationsDatabase")]
public class RegisterTripEndToEndTests : IClassFixture<HostFixture>
{
    [Fact]
    public async Task PostTrips_WithValidPayload_Returns201AndPersistsTrip()
    {
        // Arrange
        var client = _host.CreateAuthenticatedClient(
            role: "driver", driverId: TestData.DriverIds.Alice.Value);
        var request = new RegisterTripRequest(/* ... */);

        // Act
        var response = await client.PostAsJsonAsync("/api/trips", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<RegisterTripResponse>();
        body.ShouldNotBeNull();
        body.TripId.ShouldNotBe(Guid.Empty);

        await using var db = _host.CreateContext();
        var persisted = await db.Trips.FindAsync(new TripId(body.TripId));
        persisted.ShouldNotBeNull();
    }
}
```

## Pagination test cases (mandatory)

For every paginated endpoint:
1. Single page (TotalCount ≤ PageSize)
2. Multi-page, first page (HasNext=true, HasPrevious=false)
3. Multi-page, middle page
4. Multi-page, last page
5. Empty result set
6. Invalid Page/PageSize values coerced correctly
7. Unknown sort column falls back to default
8. Sort ascending and descending produce expected order
9. Sort column case-insensitivity

## N+1 prevention

Enable EF Core warning escalation in test setup:

```csharp
options.ConfigureWarnings(w => w.Throw(
    RelationalEventId.MultipleCollectionIncludeWarning));
```

If a test triggers cartesian explosion, the test fails. Fix with `.AsSplitQuery()`.

## Forbidden patterns

- `Microsoft.EntityFrameworkCore.InMemory` — vietato. Use Testcontainers.
- `EnsureCreated()` — use `MigrateAsync()`.
- `Substitute.For<DomainType>()` — never mock domain (only mock application dependencies).
- Hardcoded error code strings (use `{Aggregate}Errors` references).
- Time-dependent tests without `FakeClock` (use injected clock with fixed value).
- `.Result`, `.Wait()`, `GetAwaiter().GetResult()` — always async.
- Skipping AAA comments in tests.
