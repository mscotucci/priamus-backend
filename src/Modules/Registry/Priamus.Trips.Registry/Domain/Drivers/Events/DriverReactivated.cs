using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Drivers.Events;

/// <summary>
/// Raised when a <see cref="Driver"/> is reactivated via <see cref="Driver.Reactivate"/>.
/// Not raised on a call that changes nothing.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="DriverId">Identifier of the driver.</param>
public sealed record DriverReactivated(
    Guid EventId,
    DateTime OccurredAt,
    DriverId DriverId) : IDomainEvent;
