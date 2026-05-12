using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.Users.Events;

/// <summary>
/// Raised when a <see cref="User"/>'s password hash is rotated via <see cref="User.ChangePassword"/>.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the password was changed.</param>
/// <param name="UserId">Identifier of the user whose password was changed.</param>
public sealed record UserPasswordChanged(
    Guid EventId,
    DateTime OccurredAt,
    UserId UserId) : IDomainEvent;
