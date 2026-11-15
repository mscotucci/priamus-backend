using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Security;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.Revoke;

/// <summary>
/// Handles <c>POST /api/identity/auth/revoke</c>: the authenticated user revokes one of their own
/// refresh tokens (self-service logout). Enforces ownership: the token must belong to the
/// caller.
/// </summary>
internal sealed class RevokeHandler
{
    private readonly IdentityDbContext _db;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ILogger<RevokeHandler> _logger;

    public RevokeHandler(
        IdentityDbContext db,
        IRefreshTokenHasher refreshTokenHasher,
        ICurrentUser currentUser,
        IClock clock,
        ILogger<RevokeHandler> logger)
    {
        _db = db;
        _refreshTokenHasher = refreshTokenHasher;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Revokes the supplied refresh token. Returns <see cref="Error.Forbidden"/> if the
    /// token does not belong to the caller.
    /// </summary>
    public async Task<Result> HandleAsync(
        RevokeRequest request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var callerId = _currentUser.UserId;

        // ── 1. Find token by hash ──────────────────────────────────────────────
        var tokenHash = _refreshTokenHasher.Hash(request.RefreshToken);
        // Use the primary constructor for EF Core query translation (value converter).
        var hashVo = new RefreshTokenHash(tokenHash);
        var refreshToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(
                rt => rt.TokenHash == hashVo,
                cancellationToken);

        if (refreshToken is null)
        {
            return RefreshTokenErrors.NotFound;
        }

        // ── 2. Enforce ownership ──────────────────────────────────────────────
        if (refreshToken.UserId.Value != callerId.Value)
        {
            _logger.LogWarning(
                "User {CallerId} attempted to revoke token {TokenId} owned by {OwnerId}",
                callerId.Value, refreshToken.Id.Value, refreshToken.UserId.Value);

            return RefreshTokenErrors.NotOwner;
        }

        // ── 3. Revoke the token ───────────────────────────────────────────────
        try
        {
            refreshToken.Revoke(_clock);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        // ── 4. Write intent audit row ─────────────────────────────────────────
        _db.AuditLog.Add(AuditLogEntry.ForNonDomainEvent(
            AuditEventTypes.TokenRevokedByOwner,
            now,
            actorId: callerId.Value,
            subjectId: callerId.Value,
            payload: new { tokenId = refreshToken.Id.Value.ToString(), reason = "Logout" }));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} revoked their own refresh token {TokenId}",
            callerId.Value, refreshToken.Id.Value);

        return Result.Success();
    }
}
