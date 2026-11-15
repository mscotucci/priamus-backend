using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Priamus.Trips.Identity.Application.Abstractions;
using Priamus.Trips.Identity.Domain.RefreshTokens;
using Priamus.Trips.Identity.Domain.RefreshTokens.ValueObjects;
using Priamus.Trips.Identity.Domain.Users;
using Priamus.Trips.Identity.Domain.Users.ValueObjects;
using Priamus.Trips.Identity.Persistence;
using Priamus.Trips.Registry.Contracts;
using Priamus.Trips.SharedKernel.Results;
using Priamus.Trips.SharedKernel.Time;

namespace Priamus.Trips.Identity.Features.Login;

/// <summary>
/// Handles <c>POST /api/identity/auth/login</c>: validates credentials, issues an access JWT
/// and an opaque refresh token.
/// </summary>
internal sealed class LoginHandler
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly IJwtTokenIssuer _jwtIssuer;
    private readonly IDriverLookup _driverLookup;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        IdentityDbContext db,
        IPasswordHasher<User> passwordHasher,
        IRefreshTokenHasher refreshTokenHasher,
        IJwtTokenIssuer jwtIssuer,
        IDriverLookup driverLookup,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<LoginHandler> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _refreshTokenHasher = refreshTokenHasher;
        _jwtIssuer = jwtIssuer;
        _driverLookup = driverLookup;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates the user with the supplied credentials.
    /// </summary>
    public async Task<Result<LoginResponse>> HandleAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // ── 1. Find user by username ──────────────────────────────────────────
        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        // Use the primary constructor (not Username.Create) to build the query parameter;
        // EF Core's value converter maps Username → string, so it can translate this comparison.
        var usernameVo = Username.Create(normalizedUsername);
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == usernameVo, cancellationToken);

        if (user is null)
        {
            // Unknown username — do NOT write audit row (no subject_id available).
            // Serilog structured log at Information level per the brief.
            _logger.LogInformation(
                "Login failed for unknown username. {submittedUsername} {eventType}",
                normalizedUsername, "LoginFailedUnknownUser");

            // Return the same generic error as wrong-password to prevent enumeration.
            return UserErrors.InvalidCredentials;
        }

        // ── 2. Verify password ────────────────────────────────────────────────
        var verificationResult = _passwordHasher.VerifyHashedPassword(
            user, user.PasswordHash.Value, request.Password);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            // Known user — write LoginFailed audit row with reason BadPassword.
            _db.AuditLog.Add(AuditLogEntry.ForNonDomainEvent(
                AuditEventTypes.LoginFailed,
                now,
                actorId: null,
                subjectId: user.Id.Value,
                payload: new { submittedUsername = normalizedUsername, reason = "BadPassword" }));

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Login failed for user {UserId}: bad password",
                user.Id.Value);

            return UserErrors.InvalidCredentials;
        }

        // ── 3. Check account status ───────────────────────────────────────────
        if (user.Status == UserStatus.Disabled)
        {
            _db.AuditLog.Add(AuditLogEntry.ForNonDomainEvent(
                AuditEventTypes.LoginFailed,
                now,
                actorId: null,
                subjectId: user.Id.Value,
                payload: new { submittedUsername = normalizedUsername, reason = "AccountDisabled" }));

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Login rejected for disabled user {UserId}",
                user.Id.Value);

            return UserErrors.AccountDisabled;
        }

        // ── 4. Driver claim enrichment (ADR 0008, ADR 0010) ──────────────────
        Guid? driverId = null;
        if (user.Role == UserRole.Driver)
        {
            driverId = await _driverLookup.GetDriverIdByUserIdAsync(
                user.Id, cancellationToken);

            if (driverId is null)
            {
                _logger.LogWarning(
                    "Driver user {UserId} has no linked Driver record in Registry; " +
                    "JWT will be issued without driver_id claim.",
                    user.Id.Value);
            }
        }

        // ── 5. Generate refresh token ─────────────────────────────────────────
        var rawToken = GenerateRawRefreshToken();
        var tokenHash = _refreshTokenHasher.Hash(rawToken);
        var refreshTokenHash = RefreshTokenHash.Create(tokenHash);

        var refreshToken = RefreshToken.Issue(
            RefreshTokenId.New(),
            user.Id,
            refreshTokenHash,
            _clock,
            _jwtOptions.RefreshTokenLifetime);

        _db.RefreshTokens.Add(refreshToken);

        // ── 6. Emit UserLoggedIn audit row ────────────────────────────────────
        _db.AuditLog.Add(AuditLogEntry.ForNonDomainEvent(
            AuditEventTypes.UserLoggedIn,
            now,
            actorId: null,
            subjectId: user.Id.Value,
            payload: new
            {
                username = user.Username.Value,
                role = user.Role.ToString(),
                driverIdEnriched = driverId.HasValue,
            }));

        await _db.SaveChangesAsync(cancellationToken);

        // ── 7. Issue JWT ──────────────────────────────────────────────────────
        var accessToken = _jwtIssuer.IssueAccessToken(
            user.Id, user.Role, driverId);

        _logger.LogInformation(
            "User {UserId} logged in successfully with role {Role}",
            user.Id.Value, user.Role);

        return new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: rawToken,
            AccessTokenExpiresAt: now + _jwtOptions.AccessTokenLifetime,
            RefreshTokenExpiresAt: refreshToken.ExpiresAt);
    }

    private static string GenerateRawRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        // base64url encoding (no padding, URL-safe chars) → 43 chars
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
