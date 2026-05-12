using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.Users.Events;

/// <summary>
/// Raised when a <see cref="User"/> transitions from <see cref="UserStatus.Disabled"/>
/// to <see cref="UserStatus.Active"/> via <see cref="User.Enable"/>. Not raised on
/// idempotent no-op calls.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the user was enabled.</param>
/// <param name="UserId">Identifier of the user that was enabled.</param>
public sealed record UserEnabled(
    Guid EventId,
    DateTime OccurredAt,
    UserId UserId) : IDomainEvent;
