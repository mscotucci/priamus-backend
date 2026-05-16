using Priamus.Trips.Identity.Domain.Users;

namespace Priamus.Trips.Identity.Features.CreateUser;

/// <summary>
/// Response body for a successful <c>POST /api/users</c> request.
/// </summary>
/// <param name="Id">The newly created user's identifier.</param>
/// <param name="Username">The normalized username.</param>
/// <param name="Role">The functional role assigned to the user.</param>
/// <param name="Status">The initial lifecycle status (always <see cref="UserStatus.Active"/> on creation).</param>
public sealed record CreateUserResponse(
    Guid Id,
    string Username,
    UserRole Role,
    UserStatus Status);
