using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Locations.Events;

/// <summary>
/// Raised when a <see cref="Location"/> is reactivated via <see cref="Location.Reactivate"/>.
/// Not raised on a call that changes nothing.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="LocationId">Identifier of the location.</param>
public sealed record LocationReactivated(
    Guid EventId,
    DateTime OccurredAt,
    LocationId LocationId) : IDomainEvent;
