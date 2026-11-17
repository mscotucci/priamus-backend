using Priamus.Trips.Registry.Domain.Vehicles.ValueObjects;
using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Vehicles.Events;

/// <summary>
/// Raised when a <see cref="Vehicle"/> is edited via <see cref="Vehicle.Update"/>.
/// Carries the full state after the change.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the update.</param>
/// <param name="VehicleId">Identifier of the updated vehicle.</param>
/// <param name="LicensePlate">Plate in normal form.</param>
/// <param name="Description">Trimmed description.</param>
/// <param name="Type">Vehicle type.</param>
/// <param name="RequiresTrailer">Whether a trip led by the vehicle must name a trailer.</param>
/// <param name="Status">Status after the change.</param>
public sealed record VehicleUpdated(
    Guid EventId,
    DateTime OccurredAt,
    VehicleId VehicleId,
    LicensePlate LicensePlate,
    string Description,
    VehicleType Type,
    bool RequiresTrailer,
    VehicleStatus Status) : IDomainEvent;
