using Priamus.Trips.Operations.Domain.Trips;
using Priamus.Trips.Operations.Features.ListTripCustomers;
using Priamus.Trips.Operations.Features.ListTripLocations;
using Priamus.Trips.Operations.Features.ListTripVehicles;

namespace Priamus.Trips.Operations.Features;

/// <summary>
/// A trip in any state, as the driver app receives it: what was answered, named as the registry names it
/// now, and the times and measures once they exist. It carries no price.
/// </summary>
/// <param name="Id">The trip's identifier.</param>
/// <param name="State">Planned, in progress or ended.</param>
/// <param name="Customer">The customer.</param>
/// <param name="Pickup">The pickup location; <c>null</c> when none was named.</param>
/// <param name="Delivery">The delivery location; <c>null</c> when none was named.</param>
/// <param name="Vehicle">The tractor or standalone vehicle.</param>
/// <param name="Trailer">The trailer; <c>null</c> when none.</param>
/// <param name="Day">The day the trip started; <c>null</c> while it is planned.</param>
/// <param name="Start">The start time; <c>null</c> while it is planned.</param>
/// <param name="End">The end time; <c>null</c> until it is ended.</param>
/// <param name="Kilograms">The kilograms carried; <c>null</c> unless the trip was measured by weight.</param>
/// <param name="Kilometres">The kilometres driven; <c>null</c> unless the trip was measured by distance.</param>
/// <param name="Number">The trip number; <c>null</c> until it is ended.</param>
public sealed record TripDto(
    Guid Id,
    TripState State,
    TripCustomerDto Customer,
    TripLocationDto? Pickup,
    TripLocationDto? Delivery,
    TripVehicleDto Vehicle,
    TripVehicleDto? Trailer,
    DateOnly? Day,
    TimeOnly? Start,
    TimeOnly? End,
    int? Kilograms,
    int? Kilometres,
    long? Number);
