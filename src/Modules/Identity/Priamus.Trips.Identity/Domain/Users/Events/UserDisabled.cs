using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.Users.Events;

/// <summary>
/// Raised when a <see cref="User"/> transitions from <see cref="UserStatus.Active"/>
/// to <see cref="UserStatus.Disabled"/> via <see cref="User.Disable"/>. Not raised
/// on idempotent no-op calls.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the user was disabled.</param>
/// <param name="UserId">Identifier of the user that was disabled.</param>
public sealed record UserDisabled(
    Guid EventId,
    DateTime OccurredAt,
    UserId UserId) : IDomainEvent;
