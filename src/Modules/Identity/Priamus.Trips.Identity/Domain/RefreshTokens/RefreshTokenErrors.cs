using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Identity.Domain.RefreshTokens;

/// <summary>
/// Errors raised by the <see cref="RefreshToken"/> aggregate and its value objects.
/// </summary>
/// <remarks>
/// Codes follow the <c>RefreshToken.{ErrorName}</c> PascalCase convention.
/// HTTP status mapping is driven by <see cref="ErrorType"/>.
/// </remarks>
public static class RefreshTokenErrors
{
    // ─── Value-object validation ──────────────────────────────────────────

    /// <summary>
    /// The supplied hash string is empty or not exactly the SHA-256 hex length.
    /// </summary>
    public static readonly Error HashInvalid = Error.Domain(
        "RefreshToken.HashInvalid",
        "Refresh-token hash must be a non-empty SHA-256 hex string of exactly 64 characters.");

    /// <summary>
    /// The supplied lifetime is non-positive. Refresh-token lifetime is configurable
    /// (ADR 0005) but must always be strictly positive.
    /// </summary>
    public static readonly Error LifetimeInvalid = Error.Domain(
        "RefreshToken.LifetimeInvalid",
        "Refresh-token lifetime must be a strictly positive TimeSpan.");

    // ─── Rotation invariants (ADR 0005) ───────────────────────────────────

    /// <summary>
    /// Rotation was attempted on a token whose <c>RevokedAt</c> is already set
    /// (but which is not the result of a rotation — see
    /// <see cref="CannotRotateAlreadyRotatedToken"/>).
    /// </summary>
    public static readonly Error CannotRotateRevokedToken = Error.Conflict(
        "RefreshToken.CannotRotateRevokedToken",
        "Cannot rotate a refresh token that has already been revoked.");

    /// <summary>
    /// Rotation was attempted on a token whose <c>ExpiresAt</c> is in the past.
    /// </summary>
    public static readonly Error CannotRotateExpiredToken = Error.Conflict(
        "RefreshToken.CannotRotateExpiredToken",
        "Cannot rotate a refresh token that has expired.");

    /// <summary>
    /// Rotation was attempted on a token that has already been rotated
    /// (<c>ReplacedByTokenId</c> is set). This is the hook for theft detection in a
    /// future ADR (ADR 0005 §Negative): seeing a rotation attempt on an already-rotated
    /// token is highly suspicious.
    /// </summary>
    public static readonly Error CannotRotateAlreadyRotatedToken = Error.Conflict(
        "RefreshToken.CannotRotateAlreadyRotatedToken",
        "Cannot rotate a refresh token that has already been rotated. The legitimate client should be using the replacement token.");

    // ─── Revocation invariants ────────────────────────────────────────────

    /// <summary>
    /// Explicit revocation was attempted on a token that has already been rotated.
    /// Rotation implies revocation; an explicit revoke on top of that signals a
    /// contract violation in the caller and is rejected loudly per the Identity
    /// domain brief.
    /// </summary>
    public static readonly Error CannotRevokeRotatedToken = Error.Conflict(
        "RefreshToken.CannotRevokeRotatedToken",
        "Cannot explicitly revoke a refresh token that has been rotated; rotation already implies revocation.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>
    /// The requested refresh token could not be found (e.g. on refresh or admin revoke).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "RefreshToken.NotFound",
        "The requested refresh token was not found.");
}
