using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Drivers.Events;

/// <summary>
/// Raised when a <see cref="Driver"/> is deactivated via <see cref="Driver.Deactivate"/>.
/// Not raised on a call that changes nothing.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="DriverId">Identifier of the driver.</param>
public sealed record DriverDeactivated(
    Guid EventId,
    DateTime OccurredAt,
    DriverId DriverId) : IDomainEvent;
