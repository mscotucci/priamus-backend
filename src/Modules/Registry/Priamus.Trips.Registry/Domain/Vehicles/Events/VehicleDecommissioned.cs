using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Vehicles.Events;

/// <summary>
/// Raised when a <see cref="Vehicle"/> is decommissioned via <see cref="Vehicle.Decommission"/>.
/// Not raised on a call that changes nothing.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="VehicleId">Identifier of the vehicle.</param>
public sealed record VehicleDecommissioned(
    Guid EventId,
    DateTime OccurredAt,
    VehicleId VehicleId) : IDomainEvent;
