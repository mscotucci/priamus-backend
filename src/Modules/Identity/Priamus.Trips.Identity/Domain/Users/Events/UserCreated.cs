using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Domain.Users.Events;

/// <summary>
/// Raised when a new <see cref="User"/> is created via <see cref="User.Create"/>.
/// </summary>
/// <param name="EventId">Fresh per-instance identifier of this event.</param>
/// <param name="OccurredAt">UTC instant at which the user was created. Sourced from <c>IClock.UtcNow</c>.</param>
/// <param name="UserId">Identifier of the new user.</param>
/// <param name="Role">Functional role assigned at creation.</param>
public sealed record UserCreated(
    Guid EventId,
    DateTime OccurredAt,
    UserId UserId,
    UserRole Role) : IDomainEvent;
