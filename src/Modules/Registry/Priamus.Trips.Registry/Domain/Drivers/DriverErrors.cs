using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Drivers;

/// <summary>
/// Errors raised by the <see cref="Driver"/> aggregate and the application-layer lookups on drivers.
/// </summary>
/// <remarks>
/// Codes follow the <c>Registry.Driver.{ErrorName}</c> PascalCase convention.
/// </remarks>
public static class DriverErrors
{
    // ─── Aggregate validation ─────────────────────────────────────────────

    /// <summary>
    /// The first name is empty or longer than <see cref="Driver.FirstNameMaxLength"/> characters
    /// once trimmed (PRD §6.5, RF-24).
    /// </summary>
    public static readonly Error FirstNameInvalid = Error.Validation(
        "Registry.Driver.FirstNameInvalid",
        "First name is required and must be at most 60 characters.");

    /// <summary>
    /// The last name is empty or longer than <see cref="Driver.LastNameMaxLength"/> characters
    /// once trimmed (PRD §6.5, RF-24).
    /// </summary>
    public static readonly Error LastNameInvalid = Error.Validation(
        "Registry.Driver.LastNameInvalid",
        "Last name is required and must be at most 60 characters.");

    /// <summary>
    /// The phone number is longer than <see cref="Driver.PhoneMaxLength"/> characters once
    /// trimmed, or holds something other than digits and spaces with an optional leading "+"
    /// (PRD §6.5, RF-24).
    /// </summary>
    public static readonly Error PhoneInvalid = Error.Validation(
        "Registry.Driver.PhoneInvalid",
        "Phone must be at most 20 characters, made of digits and spaces with an optional leading +.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>The requested driver was not found.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Registry.Driver.NotFound",
        "The requested driver was not found.");

    /// <summary>The user account is already linked to another active driver.</summary>
    public static readonly Error AccountTaken = Error.Conflict(
        "Registry.Driver.AccountTaken",
        "The account is already linked to another driver.");

    /// <summary>The user account is already linked to a deactivated driver.</summary>
    public static readonly Error AccountTakenByDeactivated = Error.Conflict(
        "Registry.Driver.AccountTakenByDeactivated",
        "The account is already linked to a deactivated driver.");

    /// <summary>
    /// The user account does not exist, is not a driver account or is disabled; also returned when
    /// a save lost the race on the unique index and the holder can no longer be read.
    /// </summary>
    public static readonly Error AccountUnavailable = Error.Conflict(
        "Registry.Driver.AccountUnavailable",
        "The chosen account is not an enabled driver account.");

    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed
    /// the driver in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.Driver.ConcurrentUpdate",
        "The driver was modified by another user in the meantime.");
}
