using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.DisableUser;

/// <summary>
/// Handles <c>POST /api/identity/users/{id:guid}/disable</c>: disables a user account.
/// </summary>
/// <remarks>
/// <para>
/// Self-disable is rejected before the user is loaded from the database. Comparing
/// the raw <see cref="Guid"/> avoids an unnecessary round-trip when the check fails.
/// </para>
/// <para>
/// The version the client read becomes the original value of the concurrency token, so a user
/// changed in the meantime fails the save with <see cref="DbUpdateConcurrencyException"/>, answered
/// as <see cref="UserErrors.ConcurrentUpdate"/>. A stale version is refused even when the user
/// already has the requested status.
/// </para>
/// <para>
/// The domain method <see cref="User.Disable"/> is idempotent: if the target user is
/// already disabled and the version matches, no domain event is raised and no audit row is written.
/// The handler still persists (no-op <c>SaveChangesAsync</c>) and returns success (204).
/// </para>
/// </remarks>
internal sealed class DisableUserHandler
{
    private readonly IdentityDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<DisableUserHandler> _logger;

    public DisableUserHandler(
        IdentityDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<DisableUserHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Disables the user identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The raw <see cref="Guid"/> from the route parameter.</param>
    /// <param name="request">Carries the version the client read.</param>
    /// <param name="cancellationToken">Propagated to all async operations.</param>
    public async Task<Result> HandleAsync(
        Guid id,
        DisableUserRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = _currentUser.UserId;

        // ── 1. Self-disable guard — check before load ─────────────────────────
        if (id == actorId.Value)
        {
            return UserErrors.CannotDisableSelf;
        }

        // ── 2. Load the user ──────────────────────────────────────────────────
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == new UserId(id), cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // ── 3. Disable (idempotent domain method) ─────────────────────────────
        user.Disable(_clock, actorId);

        // ── 4. Version check and persist ───────────────────────────────────────
        _db.Entry(user).Property(u => u.Version).OriginalValue = request.Version!.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UserErrors.ConcurrentUpdate;
        }

        _logger.LogInformation(
            "User {TargetUserId} disabled by actor {ActorId}",
            id, actorId.Value);

        return Result.Success();
    }
}
