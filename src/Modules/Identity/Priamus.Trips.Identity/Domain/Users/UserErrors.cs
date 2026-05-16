using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Identity.Domain.Users;

/// <summary>
/// Errors raised by the <see cref="User"/> aggregate and its value objects.
/// </summary>
/// <remarks>
/// Codes follow the <c>User.{ErrorName}</c> PascalCase convention. The
/// <see cref="ErrorType"/> of each error is chosen to drive correct HTTP mapping
/// in <c>Priamus.Trips.SharedKernel.Http.ResultHttpExtensions</c>.
/// </remarks>
public static class UserErrors
{
    // ─── Value-object validation ──────────────────────────────────────────

    /// <summary>
    /// The supplied username failed normalization-aware validation: length out of
    /// <see cref="ValueObjects.Username.MinLength"/>..<see cref="ValueObjects.Username.MaxLength"/>,
    /// disallowed character, or non-alphanumeric leading character.
    /// </summary>
    public static readonly Error UsernameInvalid = Error.Domain(
        "User.UsernameInvalid",
        "Username must be 3..64 chars, contain only a-z, 0-9, '.', '_', '-', and start with an alphanumeric.");

    /// <summary>
    /// The supplied password-hash string is empty or exceeds the defensive maximum length.
    /// </summary>
    public static readonly Error PasswordHashInvalid = Error.Domain(
        "User.PasswordHashInvalid",
        "Password hash must be a non-empty string within the defensive length bound.");

    // ─── Aggregate invariants ─────────────────────────────────────────────

    /// <summary>
    /// Attempted to change the password on a user whose status is <see cref="UserStatus.Disabled"/>.
    /// The user must be re-enabled before the password can be rotated.
    /// </summary>
    public static readonly Error CannotChangePasswordOnDisabledUser = Error.Conflict(
        "User.CannotChangePasswordOnDisabledUser",
        "Cannot change the password of a disabled user. Re-enable the user first.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>
    /// The requested user could not be found by the application-layer query (e.g. login, password change).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "User.NotFound",
        "The requested user was not found.");

    /// <summary>
    /// A new user cannot be created with a username already in use. Uniqueness is enforced
    /// by the persistence layer (unique index) and validated upfront by the application layer.
    /// </summary>
    public static readonly Error UsernameAlreadyTaken = Error.Conflict(
        "User.UsernameAlreadyTaken",
        "The chosen username is already in use.");

    // ─── Self-action guards ───────────────────────────────────────────────

    /// <summary>
    /// An admin cannot disable their own account. Self-disable would cause instant self-lockout
    /// with no recovery path without another admin intervening.
    /// </summary>
    public static readonly Error CannotDisableSelf = Error.Conflict(
        "User.CannotDisableSelf",
        "Cannot disable your own account.");

    // ─── Authentication errors ────────────────────────────────────────────

    /// <summary>
    /// The supplied credentials are invalid (username not found OR password wrong).
    /// Used for BOTH unknown username and wrong password to prevent username enumeration.
    /// The handler distinguishes internally for audit logging only (the API surface must not).
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(
        "User.InvalidCredentials",
        "Invalid username or password.");

    /// <summary>
    /// Login or refresh was rejected because the user account is disabled.
    /// </summary>
    public static readonly Error AccountDisabled = Error.Unauthorized(
        "User.AccountDisabled",
        "The user account is disabled. Please contact an administrator.");
}
