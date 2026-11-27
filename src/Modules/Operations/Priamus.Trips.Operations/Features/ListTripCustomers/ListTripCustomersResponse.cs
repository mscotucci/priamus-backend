namespace Priamus.Trips.Operations.Features.ListTripCustomers;

/// <summary>
/// The customers offered today, split as the question shows them.
/// </summary>
/// <param name="Recent">The customers of the driver's latest trips, most recent first, five at most;
/// empty when a search text is given.</param>
/// <param name="Others">The other customers, in the order the Registry gives them (by company name).</param>
public sealed record ListTripCustomersResponse(
    IReadOnlyList<TripCustomerDto> Recent,
    IReadOnlyList<TripCustomerDto> Others);
