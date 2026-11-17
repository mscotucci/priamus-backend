using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Features.ListVehicles;

/// <summary>
/// A single item in the paginated list of vehicles.
/// </summary>
/// <param name="Id">The vehicle's identifier.</param>
/// <param name="LicensePlate">The plate in normal form.</param>
/// <param name="Description">The description.</param>
/// <param name="Type">The vehicle type.</param>
/// <param name="RequiresTrailer">Whether a trip led by this vehicle must name a trailer.</param>
/// <param name="Status">The status.</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this vehicle.</param>
public sealed record VehicleListItemDto(
    Guid Id,
    string LicensePlate,
    string Description,
    VehicleType Type,
    bool RequiresTrailer,
    VehicleStatus Status,
    DateTime LastModifiedAt,
    int Version);
