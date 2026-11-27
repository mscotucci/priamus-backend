namespace Priamus.Trips.Registry.Contracts;

/// <summary>A vehicle a trip can use or has used.</summary>
/// <param name="Id">The identifier of the vehicle.</param>
/// <param name="LicensePlate">The plate, in the form it is stored.</param>
/// <param name="Description">The free-text description of the vehicle.</param>
/// <param name="Kind">Whether it is a tractor, a standalone vehicle or a trailer.</param>
/// <param name="RequiresTrailer">Whether a trip led by this vehicle must name a trailer.</param>
public sealed record TripVehicle(
    Guid Id,
    string LicensePlate,
    string Description,
    TripVehicleKind Kind,
    bool RequiresTrailer);
