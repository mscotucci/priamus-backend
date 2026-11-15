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
        "Identity.RefreshToken.HashInvalid",
        "Refresh-token hash must be a non-empty SHA-256 hex string of exactly 64 characters.");

    /// <summary>
    /// The supplied lifetime is non-positive. Refresh-token lifetime is configurable
    /// (ADR 0005) but must always be strictly positive.
    /// </summary>
    public static readonly Error LifetimeInvalid = Error.Domain(
        "Identity.RefreshToken.LifetimeInvalid",
        "Refresh-token lifetime must be a strictly positive TimeSpan.");

    // ─── Rotation invariants (ADR 0005) ───────────────────────────────────

    /// <summary>
    /// Rotation was attempted on a token whose <c>RevokedAt</c> is already set
    /// (but which is not the result of a rotation — see
    /// <see cref="CannotRotateAlreadyRotatedToken"/>).
    /// </summary>
    public static readonly Error CannotRotateRevokedToken = Error.Conflict(
        "Identity.RefreshToken.CannotRotateRevokedToken",
        "Cannot rotate a refresh token that has already been revoked.");

    /// <summary>
    /// Rotation was attempted on a token whose <c>ExpiresAt</c> is in the past.
    /// </summary>
    public static readonly Error CannotRotateExpiredToken = Error.Conflict(
        "Identity.RefreshToken.CannotRotateExpiredToken",
        "Cannot rotate a refresh token that has expired.");

    /// <summary>
    /// Rotation was attempted on a token that has already been rotated
    /// (<c>ReplacedByTokenId</c> is set). This is the hook for theft detection in a
    /// future ADR (ADR 0005 §Negative): seeing a rotation attempt on an already-rotated
    /// token is highly suspicious.
    /// </summary>
    public static readonly Error CannotRotateAlreadyRotatedToken = Error.Conflict(
        "Identity.RefreshToken.CannotRotateAlreadyRotatedToken",
        "Cannot rotate a refresh token that has already been rotated. The legitimate client should be using the replacement token.");

    // ─── Revocation invariants ────────────────────────────────────────────

    /// <summary>
    /// Explicit revocation was attempted on a token that has already been rotated.
    /// Rotation implies revocation; an explicit revoke on top of that signals a
    /// contract violation in the caller and is rejected loudly per the Identity
    /// domain brief.
    /// </summary>
    public static readonly Error CannotRevokeRotatedToken = Error.Conflict(
        "Identity.RefreshToken.CannotRevokeRotatedToken",
        "Cannot explicitly revoke a refresh token that has been rotated; rotation already implies revocation.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>
    /// The requested refresh token could not be found (e.g. on refresh or admin revoke).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "Identity.RefreshToken.NotFound",
        "The requested refresh token was not found.");

    // ─── Application-layer authentication errors ──────────────────────────

    /// <summary>
    /// The refresh token has expired. Application-layer counterpart to the aggregate's
    /// <see cref="CannotRotateExpiredToken"/> domain error — used before calling <c>Rotate</c>
    /// when the handler detects expiry from the <see cref="Identity.RefreshToken.IsActive"/> check.
    /// </summary>
    public static readonly Error Expired = Error.Unauthorized(
        "Identity.RefreshToken.Expired",
        "The refresh token has expired. Please log in again.");

    /// <summary>
    /// The refresh token is no longer valid (it has been revoked or already rotated).
    /// Distinct code from <see cref="NotFound"/> for audit clarity: the token exists
    /// but is unusable. HTTP mapping is 401 (same as <see cref="Expired"/>).
    /// </summary>
    public static readonly Error NoLongerValid = Error.Unauthorized(
        "Identity.RefreshToken.NoLongerValid",
        "The refresh token is no longer valid. Please log in again.");

    /// <summary>
    /// A concurrent request rotated the same refresh token before this request could.
    /// The client should retry the refresh operation.
    /// </summary>
    public static readonly Error ConcurrentRefresh = Error.Conflict(
        "Identity.RefreshToken.ConcurrentRefresh",
        "The refresh token was rotated by a concurrent request. Please retry.");

    // ─── Ownership ─────────────────────────────────────────────────────────

    /// <summary>The caller tried to revoke a refresh token that belongs to another user.</summary>
    public static readonly Error NotOwner = Error.Forbidden(
        "Identity.RefreshToken.NotOwner",
        "You can only revoke your own refresh tokens.");
}
