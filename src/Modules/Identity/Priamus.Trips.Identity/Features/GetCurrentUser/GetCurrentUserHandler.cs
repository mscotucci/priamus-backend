using Microsoft.EntityFrameworkCore;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;

namespace Priamus.Trips.Identity.Features.GetCurrentUser;

/// <summary>
/// Handles <c>GET /api/identity/me</c>: answers the authenticated user from the database,
/// not from the token claims, so a user disabled or removed after signing in is reported as such.
/// </summary>
internal sealed class GetCurrentUserHandler
{
    private readonly IdentityDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDriverLookup _driverLookup;

    public GetCurrentUserHandler(
        IdentityDbContext db,
        ICurrentUser currentUser,
        IDriverLookup driverLookup)
    {
        _db = db;
        _currentUser = currentUser;
        _driverLookup = driverLookup;
    }

    /// <summary>
    /// Loads the current user without tracking and resolves the driver link for a <c>Driver</c>.
    /// </summary>
    public async Task<Result<GetCurrentUserResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return UserErrors.InvalidCredentials;
        }

        if (user.Status == UserStatus.Disabled)
        {
            return UserErrors.AccountDisabled;
        }

        Guid? driverId = null;
        if (user.Role == UserRole.Driver)
        {
            driverId = await _driverLookup.GetDriverIdByUserIdAsync(user.Id, cancellationToken);
        }

        return new GetCurrentUserResponse(user.Id.Value, user.Username.Value, user.Role, driverId);
    }
}
