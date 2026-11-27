using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Contracts;

/// <summary>
/// What a trip needs from the Registry: the choices a driver is offered and the names of what a trip
/// refers to. It is the only way Operations reads the Registry (ADR 0017); its types carry no term of a
/// contract.
/// </summary>
public interface ITripRegistry
{
    /// <summary>The name of the driver, or <c>null</c> when no driver has that identifier.</summary>
    /// <param name="driverId">The identifier of the driver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TripDriver?> FindDriverAsync(Guid driverId, CancellationToken cancellationToken);

    /// <summary>The active vehicles that can take <paramref name="role"/>, by plate.</summary>
    /// <param name="role">Leading vehicles (tractors and standalone vehicles) or trailers.</param>
    /// <param name="search">Text matched against the plate and the description; <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TripVehicle>> ListVehiclesAsync(
        TripVehicleRole role, string? search, CancellationToken cancellationToken);

    /// <summary>The active customers with a contract in force on <paramref name="day"/>, by company name.</summary>
    /// <param name="day">The day of the trip.</param>
    /// <param name="search">Text matched against the company name; <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TripCustomer>> ListCustomersAsync(
        DateOnly day, string? search, CancellationToken cancellationToken);

    /// <summary>The active locations, by name then municipality.</summary>
    /// <param name="search">Text matched against the name and the municipality; <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TripLocation>> ListLocationsAsync(string? search, CancellationToken cancellationToken);

    /// <summary>
    /// What the contract in force for the customer on <paramref name="day"/> asks of a trip, or
    /// <c>null</c> when the customer is not active or has no contract in force that day.
    /// </summary>
    /// <param name="customerId">The identifier of the customer.</param>
    /// <param name="day">The day of the trip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TripQuestions?> FindQuestionsAsync(Guid customerId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>
    /// What the contract in force for the customer on <paramref name="day"/> measures, as
    /// <see cref="PriceTripAsync"/> reads it when the trip is ended: no status of the customer is checked.
    /// <c>null</c> only when no contract of the customer covers the day.
    /// </summary>
    /// <param name="customerId">The identifier of the customer.</param>
    /// <param name="day">The day of the trip.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TripMeasureKind?> FindMeasureAsync(Guid customerId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>The vehicles, customers and locations with these identifiers, whatever their status.</summary>
    /// <param name="vehicleIds">Identifiers of vehicles.</param>
    /// <param name="customerIds">Identifiers of customers.</param>
    /// <param name="locationIds">Identifiers of locations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TripNames> DescribeAsync(
        IReadOnlyCollection<Guid> vehicleIds,
        IReadOnlyCollection<Guid> customerIds,
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken);

    /// <summary>The drivers with these identifiers, whatever their status.</summary>
    /// <param name="driverIds">Identifiers of drivers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TripDriverName>> DescribeDriversAsync(
        IReadOnlyCollection<Guid> driverIds, CancellationToken cancellationToken);

    /// <summary>
    /// The customers, drivers, vehicles and locations that <paramref name="search"/> matches, whatever their
    /// status: a case-insensitive "contains" on the company name and VAT number of a customer, the first name,
    /// last name or both of a driver, the plate and description of a vehicle, and the name and municipality of
    /// a location.
    /// </summary>
    /// <param name="search">The search text; an empty one matches nothing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TripSearchMatches> FindMatchesAsync(string search, CancellationToken cancellationToken);

    /// <summary>
    /// Judges which answers of a trip still hold against the Registry and the contract in force for the
    /// customer on the day (RF-08ter, RF-08quater, PRD §5.4). An answer not given is <c>true</c>: there is
    /// nothing to empty.
    /// </summary>
    /// <param name="answers">The answers to judge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TripAnswersCheck> CheckAnswersAsync(TripAnswers answers, CancellationToken cancellationToken);

    /// <summary>
    /// Judges whether the answers of a trip hold against the Registry on <paramref name="answers"/>'s day: the
    /// leading vehicle active and able to lead, the trailer rules, the customer active with a contract in
    /// force that day, and the locations the billing level names present, active and rated, with any other
    /// location active. The leading vehicle and the customer are required. Any fact that does not hold is the
    /// conflict <c>Registry.Trip.AnswersChanged</c>.
    /// </summary>
    /// <param name="answers">The answers to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> ValidateTripAsync(TripAnswers answers, CancellationToken cancellationToken);

    /// <summary>
    /// Prices an ended trip with the contract in force for the customer on its day and the night band of this
    /// moment, and returns everything the trip keeps. No status of a vehicle, trailer, customer or location is
    /// checked. A day with no contract of the customer, or a location or pair without a rate, is
    /// <c>Registry.Trip.NotPriceable</c>, and so is an amount too large; kilograms or kilometres that are not
    /// the ones the contract's pricing mode measures are the conflict <c>Registry.Trip.MeasureChanged</c>; a
    /// value out of range is a validation error.
    /// </summary>
    /// <param name="trip">The trip to price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PricedTrip>> PriceTripAsync(TripToPrice trip, CancellationToken cancellationToken);
}
