using Priamus.Trips.Registry.Contracts;

namespace Priamus.Trips.Operations.Features.ListTripVehicles;

/// <summary>
/// A vehicle the driver can choose for a trip.
/// </summary>
/// <param name="Id">The vehicle's identifier.</param>
/// <param name="LicensePlate">The licence plate.</param>
/// <param name="Description">The description, for example make and model.</param>
/// <param name="Kind">Whether it is a tractor, a standalone vehicle or a trailer.</param>
/// <param name="RequiresTrailer">Whether a tractor must be chosen together with a trailer.</param>
public sealed record TripVehicleDto(
    Guid Id,
    string LicensePlate,
    string Description,
    TripVehicleKind Kind,
    bool RequiresTrailer);
