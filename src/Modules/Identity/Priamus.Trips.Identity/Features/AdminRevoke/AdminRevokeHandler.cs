using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.AdminRevoke;

/// <summary>
/// Handles <c>POST /api/identity/auth/admin/tokens/{tokenId}/revoke</c>: a back-office or admin
/// user revokes any refresh token by its identifier.
/// </summary>
internal sealed class AdminRevokeHandler
{
    private readonly IdentityDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<AdminRevokeHandler> _logger;

    public AdminRevokeHandler(
        IdentityDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<AdminRevokeHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Revokes the refresh token identified by <paramref name="tokenId"/> on behalf of an admin.
    /// Does NOT enforce ownership — any back-office or admin user can revoke any token.
    /// </summary>
    public async Task<Result> HandleAsync(Guid tokenId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var actorId = _currentUser.UserId;

        // ── 1. Find token by ID ────────────────────────────────────────────────
        var refreshToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(
                rt => rt.Id == new RefreshTokenId(tokenId),
                cancellationToken);

        if (refreshToken is null)
        {
            return RefreshTokenErrors.NotFound;
        }

        // ── 2. Revoke the token ───────────────────────────────────────────────
        try
        {
            refreshToken.Revoke(_clock);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        // ── 3. Write intent audit row ─────────────────────────────────────────
        _db.AuditLog.Add(AuditLogEntry.ForNonDomainEvent(
            AuditEventTypes.TokenRevokedByAdmin,
            now,
            actorId: actorId.Value,
            subjectId: refreshToken.UserId.Value,
            payload: new { tokenId = tokenId.ToString(), reason = "AdminAction" }));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Admin {ActorId} revoked refresh token {TokenId} belonging to user {UserId}",
            actorId.Value, tokenId, refreshToken.UserId.Value);

        return Result.Success();
    }
}
