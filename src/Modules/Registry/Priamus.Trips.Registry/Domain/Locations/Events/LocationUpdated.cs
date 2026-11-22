using Priamus.Trips.SharedKernel.Domain;

namespace Priamus.Trips.Registry.Domain.Locations.Events;

/// <summary>
/// Raised when a <see cref="Location"/> is edited via <see cref="Location.Update"/>.
/// Carries the full state after the change.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the update.</param>
/// <param name="LocationId">Identifier of the updated location.</param>
/// <param name="Name">Trimmed name.</param>
/// <param name="Municipality">Trimmed municipality.</param>
/// <param name="Address">Trimmed address, or <c>null</c>.</param>
/// <param name="Status">Status after the change.</param>
public sealed record LocationUpdated(
    Guid EventId,
    DateTime OccurredAt,
    LocationId LocationId,
    string Name,
    string Municipality,
    string? Address,
    LocationStatus Status) : IDomainEvent;
