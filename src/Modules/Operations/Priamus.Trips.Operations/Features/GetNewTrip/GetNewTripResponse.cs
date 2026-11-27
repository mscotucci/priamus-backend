using Priamus.Trips.Operations.Features.ListTripVehicles;

namespace Priamus.Trips.Operations.Features.GetNewTrip;

/// <summary>
/// What "Nuovo viaggio" proposes when it opens.
/// </summary>
/// <param name="Vehicle">The leading vehicle of the driver's most recent trip started today, when it still
/// holds; <c>null</c> otherwise.</param>
/// <param name="Trailer">The trailer of that trip, when the vehicle and the trailer still hold; <c>null</c>
/// otherwise. A trailer that is missing is not judged: a tractor that now requires one is proposed
/// without it.</param>
/// <param name="HasTripInProgress">Whether the driver has a trip in progress.</param>
public sealed record GetNewTripResponse(
    TripVehicleDto? Vehicle,
    TripVehicleDto? Trailer,
    bool HasTripInProgress);
