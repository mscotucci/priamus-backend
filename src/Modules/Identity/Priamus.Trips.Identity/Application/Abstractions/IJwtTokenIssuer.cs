using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Application.Abstractions;

/// <summary>
/// Issues signed JWT access tokens for authenticated users.
/// </summary>
public interface IJwtTokenIssuer
{
    /// <summary>
    /// Creates a signed HS256 JWT with the standard claims for the given user.
    /// </summary>
    /// <param name="userId">The authenticated user's identifier (mapped to the <c>sub</c> claim).</param>
    /// <param name="role">The user's role (mapped to the <c>role</c> claim).</param>
    /// <param name="driverId">
    /// When the user is a <see cref="UserRole.Driver"/> and a link to a Registry Driver exists,
    /// the Driver's identifier to embed as the <c>driver_id</c> claim. Otherwise <c>null</c>.
    /// </param>
    /// <returns>The compact serialized JWT string.</returns>
    string IssueAccessToken(UserId userId, UserRole role, Guid? driverId);
}
