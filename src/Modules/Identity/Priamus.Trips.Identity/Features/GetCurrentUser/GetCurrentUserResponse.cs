using Priamus.Trips.Identity.Domain.Users;

namespace Priamus.Trips.Identity.Features.GetCurrentUser;

/// <summary>
/// The signed-in user as the database knows it now.
/// </summary>
/// <param name="Id">The user's identifier.</param>
/// <param name="Username">The user's normalised user name.</param>
/// <param name="Role">The user's role, serialised by name.</param>
/// <param name="DriverId">The linked Registry driver for a <c>Driver</c> user; null when unlinked or for any other role.</param>
public sealed record GetCurrentUserResponse(
    Guid Id,
    string Username,
    UserRole Role,
    Guid? DriverId);
