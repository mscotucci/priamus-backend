using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Domain;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.Refresh;

/// <summary>
/// Handles <c>POST /api/identity/auth/refresh</c>: validates the opaque refresh token,
/// rotates it, and issues a new access JWT + refresh token.
/// </summary>
internal sealed class RefreshHandler
{
    private readonly IdentityDbContext _db;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly IJwtTokenIssuer _jwtIssuer;
    private readonly IDriverLookup _driverLookup;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<RefreshHandler> _logger;

    public RefreshHandler(
        IdentityDbContext db,
        IRefreshTokenHasher refreshTokenHasher,
        IJwtTokenIssuer jwtIssuer,
        IDriverLookup driverLookup,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<RefreshHandler> logger)
    {
        _db = db;
        _refreshTokenHasher = refreshTokenHasher;
        _jwtIssuer = jwtIssuer;
        _driverLookup = driverLookup;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Rotates the supplied refresh token and issues a new access JWT.
    /// </summary>
    public async Task<Result<RefreshResponse>> HandleAsync(
        RefreshRequest request,
        CancellationToken cancellationToken)
    {
        // ── 1. Compute hash to look up the token ──────────────────────────────
        var tokenHash = _refreshTokenHasher.Hash(request.RefreshToken);

        // Use the primary constructor (not RefreshTokenHash.Create) for the query parameter
        // so EF Core's value converter can translate the comparison to SQL.
        var hashVo = new RefreshTokenHash(tokenHash);
        var refreshToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(
                rt => rt.TokenHash == hashVo,
                cancellationToken);

        if (refreshToken is null)
        {
            return RefreshTokenErrors.NotFound;
        }

        // ── 2. Validate token state ───────────────────────────────────────────
        if (!refreshToken.IsActive(_clock))
        {
            // Distinguish expired from revoked/rotated for better error codes.
            if (refreshToken.RevokedAt is not null || refreshToken.IsRotated)
            {
                return RefreshTokenErrors.NoLongerValid;
            }

            return RefreshTokenErrors.Expired;
        }

        // ── 3. Load the owning user ───────────────────────────────────────────
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == refreshToken.UserId, cancellationToken);

        if (user is null)
        {
            // Consistency error — token exists but user is gone.
            _logger.LogError(
                "Refresh token {RefreshTokenId} references non-existent user {UserId}",
                refreshToken.Id.Value, refreshToken.UserId.Value);
            return RefreshTokenErrors.NotFound;
        }

        // ── 4. Check account status ───────────────────────────────────────────
        if (user.Status == UserStatus.Disabled)
        {
            _logger.LogInformation(
                "Refresh rejected for disabled user {UserId}",
                user.Id.Value);
            return UserErrors.AccountDisabled;
        }

        // ── 5. Driver claim enrichment (ADR 0010: re-query at refresh) ────────
        Guid? driverId = null;
        if (user.Role == UserRole.Driver)
        {
            driverId = await _driverLookup.GetDriverIdByUserIdAsync(
                user.Id, cancellationToken);

            if (driverId is null)
            {
                _logger.LogWarning(
                    "Driver user {UserId} has no linked Driver record in Registry; " +
                    "refreshed JWT will be issued without driver_id claim.",
                    user.Id.Value);
            }
        }

        // ── 6. Rotate the refresh token ───────────────────────────────────────
        var rawNewToken = GenerateRawRefreshToken();
        var newTokenHash = _refreshTokenHasher.Hash(rawNewToken);
        var newTokenHashVo = RefreshTokenHash.Create(newTokenHash);

        RefreshToken newRefreshToken;
        try
        {
            newRefreshToken = refreshToken.Rotate(
                RefreshTokenId.New(),
                newTokenHashVo,
                _clock,
                _jwtOptions.RefreshTokenLifetime);
        }
        catch (DomainException ex)
        {
            return ex.Error;
        }

        _db.RefreshTokens.Add(newRefreshToken);

        // ── 7. Persist (with concurrency catch per ADR 0011) ──────────────────
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return RefreshTokenErrors.ConcurrentRefresh;
        }

        // ── 8. Issue new JWT ──────────────────────────────────────────────────
        var accessToken = _jwtIssuer.IssueAccessToken(
            user.Id, user.Role, driverId);

        var now = _clock.UtcNow;

        _logger.LogInformation(
            "Refresh token rotated for user {UserId}; new token {NewRefreshTokenId}",
            user.Id.Value, newRefreshToken.Id.Value);

        return new RefreshResponse(
            AccessToken: accessToken,
            RefreshToken: rawNewToken,
            AccessTokenExpiresAt: now + _jwtOptions.AccessTokenLifetime,
            RefreshTokenExpiresAt: newRefreshToken.ExpiresAt);
    }

    private static string GenerateRawRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
