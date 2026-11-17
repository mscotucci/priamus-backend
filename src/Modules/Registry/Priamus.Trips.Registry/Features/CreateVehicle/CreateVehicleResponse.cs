using Priamus.Trips.Registry.Domain.Vehicles;

namespace Priamus.Trips.Registry.Features.CreateVehicle;

/// <summary>
/// Response body for a successful <c>POST /api/registry/vehicles</c> request.
/// </summary>
/// <param name="Id">The newly created vehicle's identifier.</param>
/// <param name="LicensePlate">The plate in normal form.</param>
/// <param name="Description">The trimmed description.</param>
/// <param name="Type">The vehicle type.</param>
/// <param name="RequiresTrailer">Whether a trip led by this vehicle must name a trailer.</param>
/// <param name="Status">The status (always <see cref="VehicleStatus.Active"/> on creation).</param>
/// <param name="LastModifiedAt">UTC instant of the last change.</param>
/// <param name="Version">Version the client sends back with its next write on this vehicle.</param>
public sealed record CreateVehicleResponse(
    Guid Id,
    string LicensePlate,
    string Description,
    VehicleType Type,
    bool RequiresTrailer,
    VehicleStatus Status,
    DateTime LastModifiedAt,
    int Version);
