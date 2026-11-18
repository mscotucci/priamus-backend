using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Customers.Events;

/// <summary>
/// Raised when a <see cref="Customer"/> is deactivated via <see cref="Customer.Deactivate"/>.
/// Not raised on a call that changes nothing.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="CustomerId">Identifier of the customer.</param>
public sealed record CustomerDeactivated(
    Guid EventId,
    DateTime OccurredAt,
    CustomerId CustomerId) : IDomainEvent;
