using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.EnableUser;

/// <summary>
/// Handles <c>POST /api/identity/users/{id:guid}/enable</c>: re-enables a disabled user account.
/// </summary>
/// <remarks>
/// <para>
/// The version the client read becomes the original value of the concurrency token, so a user
/// changed in the meantime fails the save with <see cref="DbUpdateConcurrencyException"/>, answered
/// as <see cref="UserErrors.ConcurrentUpdate"/>. A stale version is refused even when the user
/// already has the requested status.
/// </para>
/// <para>
/// The domain method <see cref="User.Enable"/> is idempotent: if the target user is
/// already active and the version matches, no domain event is raised and no audit row is written.
/// The handler still persists (no-op <c>SaveChangesAsync</c>) and returns success (204).
/// </para>
/// <para>
/// There is intentionally no <c>CannotEnableSelf</c> guard. A disabled admin cannot
/// authenticate — the login flow rejects with <see cref="UserErrors.AccountDisabled"/>
/// before issuing a JWT — and therefore cannot reach this endpoint. The preclusion is
/// enforced upstream by design.
/// </para>
/// </remarks>
internal sealed class EnableUserHandler
{
    private readonly IdentityDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<EnableUserHandler> _logger;

    public EnableUserHandler(
        IdentityDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<EnableUserHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Enables the user identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The raw <see cref="Guid"/> from the route parameter.</param>
    /// <param name="request">Carries the version the client read.</param>
    /// <param name="cancellationToken">Propagated to all async operations.</param>
    public async Task<Result> HandleAsync(
        Guid id,
        EnableUserRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = _currentUser.UserId;

        // ── 1. Load the user ──────────────────────────────────────────────────
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == new UserId(id), cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // ── 2. Enable (idempotent domain method) ──────────────────────────────
        user.Enable(_clock, actorId);

        // ── 3. Version check and persist ───────────────────────────────────────
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
            "User {TargetUserId} enabled by actor {ActorId}",
            id, actorId.Value);

        return Result.Success();
    }
}
