using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Operations.IntegrationTests.Infrastructure;

/// <summary>
/// Hand-written <see cref="ITripRegistry"/> over in-memory lists and results a test fills. Lists answer
/// in the order the test gives them, as the real registry's order is proven by Registry's own tests.
/// Every search the registry receives is recorded.
/// </summary>
public sealed class FakeTripRegistry : ITripRegistry
{
    /// <summary>The driver <see cref="FindDriverAsync"/> finds, by identifier.</summary>
    public Dictionary<Guid, TripDriver> Drivers { get; } = [];

    /// <summary>The active vehicles of any role; a leading vehicle is any kind but a trailer.</summary>
    public List<TripVehicle> Vehicles { get; } = [];

    /// <summary>The customers <see cref="ListCustomersAsync"/> offers, whatever the day.</summary>
    public List<TripCustomer> Customers { get; } = [];

    /// <summary>The active locations <see cref="ListLocationsAsync"/> offers.</summary>
    public List<TripLocation> Locations { get; } = [];

    /// <summary>What <see cref="FindQuestionsAsync"/> answers, by customer and day; no entry means <c>null</c>.</summary>
    public Dictionary<(Guid CustomerId, DateOnly Day), TripQuestions> Questions { get; } = [];

    /// <summary>What <see cref="FindMeasureAsync"/> answers, by customer and day; no entry means <c>null</c>.</summary>
    public Dictionary<(Guid CustomerId, DateOnly Day), TripMeasureKind> Measures { get; } = [];

    /// <summary>What <see cref="CheckAnswersAsync"/> answers.</summary>
    public TripAnswersCheck AnswersCheck { get; set; } = new(true, true, true, true, true);

    /// <summary>What <see cref="ValidateTripAsync"/> answers.</summary>
    public Result ValidateTripResult { get; set; } = Result.Success();

    /// <summary>The answers of every call of <see cref="ValidateTripAsync"/>, as received.</summary>
    public List<TripAnswers> ValidatedAnswers { get; } = [];

    /// <summary>What <see cref="PriceTripAsync"/> answers; a test that prices must set it.</summary>
    public Result<PricedTrip>? PricedTripResult { get; set; }

    /// <summary>The search text of every call of <see cref="ListVehiclesAsync"/>, as received.</summary>
    public List<string?> VehicleSearches { get; } = [];

    /// <summary>The search text of every call of <see cref="ListCustomersAsync"/>, as received.</summary>
    public List<string?> CustomerSearches { get; } = [];

    /// <summary>The day of every call of <see cref="ListCustomersAsync"/>, as received.</summary>
    public List<DateOnly> CustomerDays { get; } = [];

    /// <summary>The answers of every call of <see cref="CheckAnswersAsync"/>, as received.</summary>
    public List<TripAnswers> CheckedAnswers { get; } = [];

    /// <summary>The search text of every call of <see cref="ListLocationsAsync"/>, as received.</summary>
    public List<string?> LocationSearches { get; } = [];

    /// <inheritdoc />
    public Task<TripDriver?> FindDriverAsync(Guid driverId, CancellationToken cancellationToken) =>
        Task.FromResult(Drivers.GetValueOrDefault(driverId));

    /// <inheritdoc />
    public Task<IReadOnlyList<TripVehicle>> ListVehiclesAsync(
        TripVehicleRole role, string? search, CancellationToken cancellationToken)
    {
        VehicleSearches.Add(search);
        IReadOnlyList<TripVehicle> vehicles = Vehicles
            .Where(v => (v.Kind == TripVehicleKind.Trailer) == (role == TripVehicleRole.Trailer))
            .ToList();
        return Task.FromResult(vehicles);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TripCustomer>> ListCustomersAsync(
        DateOnly day, string? search, CancellationToken cancellationToken)
    {
        CustomerSearches.Add(search);
        CustomerDays.Add(day);
        return Task.FromResult<IReadOnlyList<TripCustomer>>([.. Customers]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TripLocation>> ListLocationsAsync(string? search, CancellationToken cancellationToken)
    {
        LocationSearches.Add(search);
        return Task.FromResult<IReadOnlyList<TripLocation>>([.. Locations]);
    }

    /// <inheritdoc />
    public Task<TripQuestions?> FindQuestionsAsync(Guid customerId, DateOnly day, CancellationToken cancellationToken) =>
        Task.FromResult(Questions.GetValueOrDefault((customerId, day)));

    /// <inheritdoc />
    public Task<TripMeasureKind?> FindMeasureAsync(Guid customerId, DateOnly day, CancellationToken cancellationToken) =>
        Task.FromResult(Measures.TryGetValue((customerId, day), out var measure) ? measure : (TripMeasureKind?)null);

    /// <inheritdoc />
    public Task<TripNames> DescribeAsync(
        IReadOnlyCollection<Guid> vehicleIds,
        IReadOnlyCollection<Guid> customerIds,
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TripNames(
            [.. Vehicles.Where(v => vehicleIds.Contains(v.Id))],
            [.. Customers.Where(c => customerIds.Contains(c.Id))],
            [.. Locations.Where(l => locationIds.Contains(l.Id))]));

    /// <inheritdoc />
    public Task<TripAnswersCheck> CheckAnswersAsync(TripAnswers answers, CancellationToken cancellationToken)
    {
        CheckedAnswers.Add(answers);
        return Task.FromResult(AnswersCheck);
    }

    /// <inheritdoc />
    public Task<Result> ValidateTripAsync(TripAnswers answers, CancellationToken cancellationToken)
    {
        ValidatedAnswers.Add(answers);
        return Task.FromResult(ValidateTripResult);
    }

    /// <inheritdoc />
    public Task<Result<PricedTrip>> PriceTripAsync(TripToPrice trip, CancellationToken cancellationToken) =>
        Task.FromResult(PricedTripResult
            ?? throw new InvalidOperationException("The test did not set PricedTripResult."));
}
