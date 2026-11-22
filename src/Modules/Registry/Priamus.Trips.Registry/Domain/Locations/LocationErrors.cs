using Priamus.Trips.SharedKernel.Results;

namespace Priamus.Trips.Registry.Domain.Locations;

/// <summary>
/// Errors raised by the <see cref="Location"/> aggregate and the application-layer lookups on
/// locations.
/// </summary>
/// <remarks>
/// Codes follow the <c>Registry.Location.{ErrorName}</c> PascalCase convention.
/// </remarks>
public static class LocationErrors
{
    // ─── Aggregate validation ─────────────────────────────────────────────

    /// <summary>
    /// The name is empty or longer than <see cref="Location.NameMaxLength"/> characters once
    /// trimmed (PRD §5.1, RF-23).
    /// </summary>
    public static readonly Error NameInvalid = Error.Validation(
        "Registry.Location.NameInvalid",
        "Name is required and must be at most 100 characters.");

    /// <summary>
    /// The municipality is empty or longer than <see cref="Location.MunicipalityMaxLength"/>
    /// characters once trimmed (PRD §5.1, RF-23).
    /// </summary>
    public static readonly Error MunicipalityInvalid = Error.Validation(
        "Registry.Location.MunicipalityInvalid",
        "Municipality is required and must be at most 100 characters.");

    /// <summary>
    /// The address is longer than <see cref="Location.AddressMaxLength"/> characters once trimmed
    /// (PRD §5.1, RF-23).
    /// </summary>
    public static readonly Error AddressInvalid = Error.Validation(
        "Registry.Location.AddressInvalid",
        "Address must be at most 200 characters.");

    // ─── Application-layer lookups ────────────────────────────────────────

    /// <summary>The requested location was not found.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "Registry.Location.NotFound",
        "The requested location was not found.");

    /// <summary>
    /// Another active location has the same name and municipality, compared ignoring case and
    /// repeated spaces (PRD §5.1).
    /// </summary>
    public static readonly Error Taken = Error.Conflict(
        "Registry.Location.Taken",
        "A location with the same name and municipality is already registered.");

    /// <summary>
    /// A deactivated location has the same name and municipality and can be reactivated instead
    /// (PRD §5.1).
    /// </summary>
    public static readonly Error TakenByDeactivated = Error.Conflict(
        "Registry.Location.TakenByDeactivated",
        "A deactivated location with the same name and municipality is already registered.");

    /// <summary>
    /// The version sent with a write does not match the stored one: someone else changed the
    /// location in the meantime.
    /// </summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Registry.Location.ConcurrentUpdate",
        "The location was modified by another user in the meantime.");
}
