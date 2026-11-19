using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Registry.Domain.Drivers.Events;

/// <summary>
/// Raised when a <see cref="Driver"/> is changed via <see cref="Driver.Update"/>.
/// Carries the full state after the change.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant of the change.</param>
/// <param name="DriverId">Identifier of the driver.</param>
/// <param name="FirstName">Trimmed first name.</param>
/// <param name="LastName">Trimmed last name.</param>
/// <param name="Phone">Trimmed phone number, or <c>null</c>.</param>
/// <param name="UserId">Linked user account, or <c>null</c>.</param>
/// <param name="Status">Status after the change.</param>
public sealed record DriverUpdated(
    Guid EventId,
    DateTime OccurredAt,
    DriverId DriverId,
    string FirstName,
    string LastName,
    string? Phone,
    UserId? UserId,
    DriverStatus Status) : IDomainEvent;
